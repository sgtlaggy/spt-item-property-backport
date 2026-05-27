using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

using Path = System.IO.Path;

namespace ItemPropertyBackport;

using ItemsDict = Dictionary<MongoId, ItemProperties>;
using QuestsDict = Dictionary<MongoId, Dictionary<MongoId, QuestCondition>>;


[Injectable]
public class DataService
{
    private JsonUtil _json;
    private ISptLogger<DataService> _logger;

    private string modDir;
    private string configFile;
    private string itemsFile;
    private string questsFile;

    private static string baseUrl = "https://raw.githubusercontent.com/sgtlaggy/spt-item-property-backport/refs/heads/master/Resources/db/";
    private string itemsUrl = baseUrl + "items.json";
    private string questsUrl = baseUrl + "quests.json";

    private Config? config;

    public DataService(JsonUtil json, ModHelper modHelper, ISptLogger<DataService> logger)
    {
        _json = json;
        _logger = logger;

        modDir = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        configFile = Path.Join(modDir, "config.json");
        itemsFile = Path.Join(modDir, "db", "items.json");
        questsFile = Path.Join(modDir, "db", "quests.json");

        config = json.DeserializeFromFile<Config>(configFile);

        // Special-case any misbehaving properties
        if (config is not null)
        {
            // ‘PlayFuzeSound’ is not nullable
            config.ExcludeProperties.Add("PlayFuzeSound");
        }
    }

    public Config GetConfig()
    {
        if (config is null)
        {
            throw new Exception("Failed to load config.");
        }

        return config;
    }

    public async Task<ItemsDict> GetItemChanges()
    {
        ItemsDict? changes = null;
        if (!File.Exists(itemsFile))
        {
            _logger.Warning("[ItemPropertyBackport] Item file not found, redownloading.");
            var response = await GetWithRetries(itemsUrl, 2);
            if (response is not null)
            {
                changes = _json.Deserialize<ItemsDict>(response);

                var directory = Path.GetDirectoryName(itemsFile)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(itemsFile, response);
            }
        }
        else
        {
            changes = await _json.DeserializeFromFileAsync<ItemsDict>(itemsFile);
        }

        if (changes is null)
        {
            throw new Exception("Failed to load item changes.");
        }

        return changes;
    }

    public async Task<QuestsDict> GetQuestChanges()
    {
        QuestsDict? changes = null;
        if (!File.Exists(questsFile))
        {
            _logger.Warning("[ItemPropertyBackport] Quest file not found, redownloading.");
            var response = await GetWithRetries(questsUrl, 2);
            if (response is not null)
            {
                changes = _json.Deserialize<QuestsDict>(response);

                var directory = Path.GetDirectoryName(questsFile)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(questsFile, response);
            }
        }
        else
        {
            changes = await _json.DeserializeFromFileAsync<QuestsDict>(questsFile);
        }

        if (changes is null)
        {
            throw new Exception("Failed to load quest changes.");
        }

        return changes;
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
