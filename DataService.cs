using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace ItemPropertyBackport;

[Injectable]
public class DataService
{
    private JsonUtil _json;
    private ISptLogger<DataService> _logger;

    private string modDir;
    private string configFile;
    private string changesFile;
    private string changesUrl = "https://raw.githubusercontent.com/sgtlaggy/spt-item-property-backport/refs/heads/master/Resources/db/items.json";

    private Config? config;

    public DataService(JsonUtil json, ModHelper modHelper, ISptLogger<DataService> logger)
    {
        _json = json;
        _logger = logger;

        modDir = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        configFile = Path.Join(modDir, "config.json");
        changesFile = Path.Join(modDir, "db", "items.json");

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

    public async Task<Dictionary<MongoId, ItemProperties>> GetItemChanges()
    {
        var config = GetConfig();

        Dictionary<MongoId, ItemProperties>? changes = null;
        if (!File.Exists(changesFile))
        {
            _logger.Warning("[ItemPropertyBackport] Item file not found, redownloading.");
            var response = await GetWithRetries(changesUrl, 2);
            if (response is not null)
            {
                changes = _json.Deserialize<Dictionary<MongoId, ItemProperties>>(response);

                var directory = Path.GetDirectoryName(changesFile)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(changesFile, response);
            }
        }
        else
        {
            changes = await _json.DeserializeFromFileAsync<Dictionary<MongoId, ItemProperties>>(changesFile);
        }

        if (changes is null)
        {
            throw new Exception("Failed to load item changes.");
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
