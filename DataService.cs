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
using PricesDict = Dictionary<MongoId, Prices>;


[Injectable]
public class DataService
(
    JsonUtil _json,
    ModHelper _modHelper,
    ISptLogger<DataService> _logger
)
{
    private static string baseUrl = "https://raw.githubusercontent.com/sgtlaggy/spt-item-property-backport/refs/heads/master/Resources/db/";

    private string modDir = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

    private Config? config;
    private bool triedLoadConfig;

    public Config GetConfig()
    {
        if (!triedLoadConfig)
        {
            triedLoadConfig = true;
            config = _json.DeserializeFromFile<Config>(Path.Join(modDir, "config.json"));

            // Special-case some properties
            if (config is not null)
            {
                // ‘PlayFuzeSound’ is not nullable
                config.ExcludeProperties.Add("PlayFuzeSound");

                if (config.ExcludeProperties.Contains("Durability"))
                {
                    config.ExcludeProperties.Add("MaxDurability");
                }
            }
        }

        if (config is null)
        {
            throw new Exception("Failed to load config.");
        }

        return config;
    }

    public async Task<ItemsDict> GetItemChanges()
    {
        var changes = await ReadOrRedownload<ItemsDict>("items.json");

        if (changes is null)
        {
            throw new Exception("Failed to load item changes.");
        }

        return changes;
    }

    public async Task<QuestsDict> GetQuestChanges()
    {
        var changes = await ReadOrRedownload<QuestsDict>("quests.json");

        if (changes is null)
        {
            throw new Exception("Failed to load quest changes.");
        }

        return changes;
    }

    public async Task<PricesDict> GetPriceChanges()
    {
        var changes = await ReadOrRedownload<PricesDict>("prices.json");

        if (changes is null)
        {
            throw new Exception("Failed to load price changes.");
        }

        return changes;
    }

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
