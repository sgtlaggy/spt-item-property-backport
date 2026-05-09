using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace ItemPropertyBackport;

[Injectable]
public class DataService
{
    private JsonUtil _json;

    private string modDir;
    private string configFile;
    private string itemsFile;

    private Config? config;

    public DataService(JsonUtil json, ModHelper modHelper)
    {
        _json = json;

        modDir = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        configFile = Path.Join(modDir, "config.json");
        itemsFile = Path.Join(modDir, "db", "items.json");

        config = json.DeserializeFromFile<Config>(configFile);
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
        var items = await _json.DeserializeFromFileAsync<Dictionary<MongoId, ItemProperties>>(itemsFile);

        if (items is null)
        {
            throw new Exception("Failed to load item changes.");
        }

        return items;
    }
}
