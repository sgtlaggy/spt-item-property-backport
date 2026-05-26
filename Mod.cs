using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

using Path = System.IO.Path;

namespace ItemPropertyBackport;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 4)]
public class Mod(
    DatabaseService _db,
#if DEBUG
    JsonUtil _json,
#endif
    ModHelper _modHelper,
    DataService _dataService,
    ISptLogger<Mod> _logger
) : IOnLoad
{
    private string modDir = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

    public async Task OnLoad()
    {
        Config? config;
        try
        {
            config = _dataService.GetConfig();
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] {e.Message}");
            return;
        }

        await UpdateItems(config);
    }

    private async Task UpdateItems(Config config)
    {
        Dictionary<MongoId, ItemProperties>? changes;
        try
        {
            changes = await _dataService.GetItemChanges();
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] {e.Message}");
            return;
        }

        var items = _db.GetItems();

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "items_before.json"), _json.Serialize(items, true));
#endif

        TemplateItem? item;
        TemplateItemProperties? dbProps;

        if (config.IncludeItems.Count > 0)
        {
            foreach (var id in config.IncludeItems)
            {
                items.TryGetValue(id, out item);
                dbProps = item?.Properties;
                if (dbProps is null)
                {
#if DEBUG
                    _logger.Warning($"[ItemPropertyBackport] Item {id} not found in DB.");
#endif
                    continue;
                }

                ItemProperties? props;
                var found = changes.TryGetValue(id, out props);
                if (!found || (props is null))
                {
                    _logger.Warning($"[ItemPropertyBackport] No changes for item {id}.");
                    continue;
                }

                UpdateItem(config, dbProps, props);
            }
        }
        else
        {
            foreach (var change in changes)
            {
                var id = change.Key;
                if (config.ExcludeItems.Contains(id))
                {
#if DEBUG
                    _logger.Warning($"[ItemPropertyBackport] Skipping item {id}.");
#endif
                    continue;
                }

                items.TryGetValue(id, out item);
                dbProps = item?.Properties;
                if (dbProps is null)
                {
#if DEBUG
                    _logger.Warning($"[ItemPropertyBackport] Item {id} not found in DB.");
#endif
                    continue;
                }

                UpdateItem(config, dbProps, change.Value);
            }
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "items_after.json"), _json.Serialize(items, true));
#endif
    }

    private void UpdateItem(Config config, TemplateItemProperties dbProps, ItemProperties props)
    {
        var type_props = typeof(TemplateItemProperties).GetProperties();
        foreach (var prop in type_props)
        {
            var new_value = prop.GetValue(props);
            if ((new_value is null) || config.ExcludeProperties.Contains(prop.Name))
            {
                continue;
            }

            prop.SetValue(dbProps, new_value);
        }

        if ((props.ConflictingItemsDiff is not null) && !config.ExcludeProperties.Contains("ConflictingItems"))
        {
            var added = props.ConflictingItemsDiff[0];
            var removed = props.ConflictingItemsDiff[1];
            if (dbProps.ConflictingItems is null)
            {
                dbProps.ConflictingItems = added;
            }
            else
            {
                dbProps.ConflictingItems.ExceptWith(removed);
                dbProps.ConflictingItems.UnionWith(added);
            }
        }
    }
}
