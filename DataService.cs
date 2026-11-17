using System.Net;
using System.Text.Json.Nodes;

using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

using Path = System.IO.Path;

namespace ItemPropertyBackport;

using ItemsDict = Dictionary<MongoId, ItemProperties>;
using QuestsDict = Dictionary<MongoId, Dictionary<MongoId, QuestCondition>>;


[Injectable(InjectionType = InjectionType.Singleton)]
public class DataService
(
    JsonUtil _json,
    ModHelper _modHelper,
    TemplateTable _templates,
    GlobalTable _globals,
    ISptLogger<DataService> _logger
)
{
    /// <summary>Where the upstream mod publishes its (currently stale) db files.</summary>
    private static string baseUrl = "https://raw.githubusercontent.com/sgtlaggy/spt-item-property-backport/refs/heads/master/Resources/db/";

    /// <summary>
    /// The tarkov.dev GraphQL endpoint has been down since 2026-07-21, but the flat
    /// JSON API it is built from is still updated, so the mod reads from there.
    /// </summary>
    private static string liveItemsUrl = "https://json.tarkov.dev/regular/items";

    private static string liveTasksUrl = "https://json.tarkov.dev/regular/tasks";

    private string modDir = _modHelper.GetAbsolutePathToModFolder();

    private JsonNode? liveItems;
    private JsonNode? liveTasks;
    private JsonNode? stimulatorBuffs;
    private bool liveItemsFailed;
    private bool liveTasksFailed;

    public async Task<ItemsDict> GetItemChanges(bool autoUpdate, int autoUpdateDays)
    {
        if (NeedsRefresh(Path.Join(modDir, "db", "items.json"), autoUpdate, autoUpdateDays))
        {
            var built = await BuildItemsFromLive();
            if (built is not null)
            {
                return built;
            }

            _logger.Warning("[ItemPropertyBackport] live update failed, falling back to the cached db file.");
        }

        var changes = await ReadOrRedownload<ItemsDict>("items.json");

        if (changes is null)
        {
            throw new Exception("Failed to load item changes.");
        }

        return changes;
    }

    public async Task<QuestsDict> GetQuestChanges(bool autoUpdate, int autoUpdateDays)
    {
        if (NeedsRefresh(Path.Join(modDir, "db", "quests.json"), autoUpdate, autoUpdateDays))
        {
            var built = await BuildQuestsFromLive();
            if (built is not null)
            {
                return built;
            }

            _logger.Warning("[ItemPropertyBackport] live update failed, falling back to the cached db file.");
        }

        var changes = await ReadOrRedownload<QuestsDict>("quests.json");

        if (changes is null)
        {
            throw new Exception("Failed to load quest changes.");
        }

        return changes;
    }

    /// <summary>
    /// Ids of items that have a live flea market price. Used purely as the
    /// "this item is tradable" signal for lifting the flea blacklist.
    /// </summary>
    public async Task<HashSet<MongoId>> GetFleaTradable(bool autoUpdate, int autoUpdateDays)
    {
        if (NeedsRefresh(Path.Join(modDir, "db", "flea.json"), autoUpdate, autoUpdateDays))
        {
            var built = await BuildFleaTradableFromLive();
            if (built is not null)
            {
                return built;
            }

            _logger.Warning(
                "[ItemPropertyBackport] live update failed, falling back to the cached db file."
            );
        }

        var cached = await ReadOrRedownload<HashSet<MongoId>>("flea.json");

        return cached ?? [];
    }

    // ------------------------------------------------------------ live builds

    private async Task<ItemsDict?> BuildItemsFromLive()
    {
        var live = await GetLiveItems();
        if (live is null)
        {
            return null;
        }

        try
        {
            _logger.Info("[ItemPropertyBackport] building item data from json.tarkov.dev ...");

            var json = LiveDataConverter.BuildItems(live, GetSptProperties, GetStimulatorBuffs());
            var result = _json.Deserialize<ItemsDict>(json);

            if (result is null || result.Count == 0)
            {
                _logger.Error("[ItemPropertyBackport] live item data came back empty.");
                return null;
            }

            SaveCache("items.json", json);
            _logger.Success($"[ItemPropertyBackport] item data updated: {result.Count} entries.");

            return result;
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] failed to build item data: {e.Message}");
            return null;
        }
    }

    private async Task<QuestsDict?> BuildQuestsFromLive()
    {
        var live = await GetLiveTasks();
        if (live is null)
        {
            return null;
        }

        try
        {
            _logger.Info("[ItemPropertyBackport] building quest data from json.tarkov.dev ...");

            var quests = JsonNode.Parse(_json.Serialize(_templates.Quests)!)!;
            var json = LiveDataConverter.BuildQuests(live, quests);
            var result = _json.Deserialize<QuestsDict>(json);

            if (result is null)
            {
                _logger.Error("[ItemPropertyBackport] live quest data came back empty.");
                return null;
            }

            SaveCache("quests.json", json);
            _logger.Success($"[ItemPropertyBackport] quest data updated: {result.Count} quests.");

            return result;
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] failed to build quest data: {e.Message}");
            return null;
        }
    }

    private async Task<HashSet<MongoId>?> BuildFleaTradableFromLive()
    {
        var live = await GetLiveItems();
        if (live is null)
        {
            return null;
        }

        try
        {
            _logger.Info("[ItemPropertyBackport] building flea tradable list from json.tarkov.dev ...");

            var json = LiveDataConverter.BuildFleaTradable(live, ExistsInSpt);
            var result = _json.Deserialize<HashSet<MongoId>>(json);

            if (result is null)
            {
                _logger.Error("[ItemPropertyBackport] live flea data came back empty.");
                return null;
            }

            SaveCache("flea.json", json);
            _logger.Success($"[ItemPropertyBackport] flea tradable list updated: {result.Count} items.");

            return result;
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] failed to build flea list: {e.Message}");
            return null;
        }
    }

    private bool ExistsInSpt(string id)
    {
        return IsValidMongoId(id) && _templates.Items.ContainsKey(id);
    }

    private JsonNode? GetSptProperties(string id)
    {
        // tarkov.dev also lists a handful of pseudo items with non-hex ids
        // (e.g. "customdogtags12345678910"), which MongoId rejects outright.
        if (!IsValidMongoId(id))
        {
            return null;
        }

        if (!_templates.Items.TryGetValue(id, out var item) || item.Properties is null)
        {
            return null;
        }

        if (JsonNode.Parse(_json.Serialize(item.Properties)!) is not JsonObject props)
        {
            return null;
        }

        // SPT serialises enum-keyed dictionaries with numeric keys ("3", "7"),
        // while the db files use the enum names ("Energy", "Pain").
        NormalizeEnumKeys(props, "effects_health", Enum.GetNames<HealthFactor>());
        NormalizeEnumKeys(props, "effects_damage", Enum.GetNames<DamageEffectType>());

        return props;
    }

    private static void NormalizeEnumKeys(JsonObject props, string field, string[] names)
    {
        if (props[field] is not JsonObject dict)
        {
            return;
        }

        var normalized = new JsonObject();
        foreach (var (key, value) in dict)
        {
            var name = int.TryParse(key, out var index) && index >= 0 && index < names.Length
                ? names[index]
                : key;

            normalized[name] = value?.DeepClone();
        }

        props.Remove(field);
        props[field] = normalized;
    }

    private static bool IsValidMongoId(string id)
    {
        if (id.Length != 24)
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private JsonNode GetStimulatorBuffs()
    {
        return stimulatorBuffs ??= JsonNode.Parse(
            _json.Serialize(_globals.Configuration.Health.Effects.Stimulator.Buffs)!
        )!;
    }

    private async Task<JsonNode?> GetLiveItems()
    {
        if (liveItems is not null)
        {
            return liveItems;
        }

        if (liveItemsFailed)
        {
            return null;
        }

        var text = await Download(liveItemsUrl, "item");
        if (text is null)
        {
            liveItemsFailed = true;
            return null;
        }

        liveItems = JsonNode.Parse(text);
        return liveItems;
    }

    private async Task<JsonNode?> GetLiveTasks()
    {
        if (liveTasks is not null)
        {
            return liveTasks;
        }

        if (liveTasksFailed)
        {
            return null;
        }

        var text = await Download(liveTasksUrl, "quest");
        if (text is null)
        {
            liveTasksFailed = true;
            return null;
        }

        liveTasks = JsonNode.Parse(text);
        return liveTasks;
    }

    private async Task<string?> Download(string url, string label)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ItemPropertyBackport/1.5.2");

            _logger.Info($"[ItemPropertyBackport] downloading {label} data, this may take a while ...");

            var text = await client.GetStringAsync(url);

            _logger.Info($"[ItemPropertyBackport] downloaded {label} data ({text.Length / 1024} KiB).");

            return text;
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] could not download {label} data: {e.Message}");
            return null;
        }
    }

    private void SaveCache(string filename, string json)
    {
        try
        {
            var directory = Path.Join(modDir, "db");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Join(directory, filename), json);
        }
        catch (Exception e)
        {
            _logger.Warning($"[ItemPropertyBackport] could not cache {filename}: {e.Message}");
        }
    }

    private static bool NeedsRefresh(string file, bool autoUpdate, int autoUpdateDays)
    {
        if (!autoUpdate)
        {
            return false;
        }

        if (!File.Exists(file))
        {
            return true;
        }

        return autoUpdateDays > 0
               && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(autoUpdateDays);
    }

    // ------------------------------------------------------------- file cache

    private async Task<T?> ReadOrRedownload<T>(string filename)
    {
        var file = Path.Join(modDir, "db", filename);
        if (File.Exists(file))
        {
            return await _json.DeserializeFromFileAsync<T>(file);
        }

        var url = baseUrl + filename;
        _logger.Warning($"[ItemPropertyBackport] {filename} not found, redownloading.");
        var response = await GetWithRetries(url, 2);
        if (response is not null)
        {
            var changes = _json.Deserialize<T>(response);

            var directory = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(file, response);

            return changes;
        }

        return default;
    }

    // Adapted from LiveFleaPrices’ ‘GetWithRetries’ method
    // Moved loop into the ‘using’ block and changed mod name in log.
    private async Task<string?> GetWithRetries(string url, int retries)
    {
        using (HttpClient client = new HttpClient())
        {
            for (var i = 0; i <= retries; i++)
            {
                try
                {
                    return await client.GetStringAsync(url);
                }
                catch (Exception e)
                {
                    _logger.Error($"[ItemPropertyBackport] Error downloading JSON, attempt {i + 1}/{retries + 1}: {e.Message}");
                }
            }
        }

        return null;
    }
}
