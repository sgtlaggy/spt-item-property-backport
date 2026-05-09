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
    JsonUtil _json,
    ModHelper _modHelper,
    ISptLogger<Mod> _logger
) : IOnLoad
{
    private string modDir = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
    private Config? config;

    public async Task OnLoad()
    {
        try
        {
            config = _json.DeserializeFromFile<Config>(Path.Join(modDir, "config.json"));
            if (config is null)
            {
                throw new Exception("Failed to load config.");
            }
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] {e.Message}");
            return;
        }

        Dictionary<MongoId, ItemProperties>? changes;
        try
        {
            changes = await _json.DeserializeFromFileAsync<Dictionary<MongoId, ItemProperties>>(Path.Join(modDir, "db", "items.json"));
            if (changes is null)
            {
                throw new Exception("Failed to load item changes.");
            }
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] {e.Message}");
            return;
        }

        var items = _db.GetItems();

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "before.json"), _json.Serialize(items, true));
#endif

        TemplateItem? item;
        TemplateItemProperties? dbProps;

        if (config.OnlyItems.Count > 0)
        {
            foreach (var id in config.OnlyItems)
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

                UpdateItem(dbProps, props);
            }
        }
        else
        {
            foreach (var change in changes)
            {
                var id = change.Key;
                if (config.ExemptItems.Contains(id))
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

                UpdateItem(dbProps, change.Value);
            }
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "after.json"), _json.Serialize(items, true));
#endif

        return;
    }

    private void UpdateItem(TemplateItemProperties dbProps, ItemProperties props)
    {
        var changes = config!.ChangeProperties;

        if (changes.BlocksEarpiece && (props.BlocksEarpiece is not null))
        {
            dbProps.BlocksEarpiece = props.BlocksEarpiece;
        }

        if (changes.Weight && (props.Weight is not null))
        {
            dbProps.Weight = props.Weight;
        }

        if (changes.Ergonomics && (props.Ergonomics is not null))
        {
            dbProps.Ergonomics = props.Ergonomics;
        }

        if (changes.Loudness && (props.Loudness is not null))
        {
            dbProps.Loudness = props.Loudness;
        }

        if (changes.Accuracy && (props.Accuracy is not null))
        {
            dbProps.Accuracy = props.Accuracy;
        }

        if (changes.Recoil && (props.Recoil is not null))
        {
            dbProps.Recoil = props.Recoil;
        }

        if (changes.Damage && (props.Damage is not null))
        {
            dbProps.Damage = props.Damage;
        }

        if (changes.ArmorDamage && (props.ArmorDamage is not null))
        {
            dbProps.ArmorDamage = props.ArmorDamage;
        }

        if (changes.InitialSpeed && (props.InitialSpeed is not null))
        {
            dbProps.InitialSpeed = props.InitialSpeed;
        }

        if (changes.Velocity && (props.Velocity is not null))
        {
            dbProps.Velocity = props.Velocity;
        }

        if (changes.LightBleedingDelta && (props.LightBleedingDelta is not null))
        {
            dbProps.LightBleedingDelta = props.LightBleedingDelta;
        }

        if (changes.HeavyBleedingDelta && (props.HeavyBleedingDelta is not null))
        {
            dbProps.HeavyBleedingDelta = props.HeavyBleedingDelta;
        }

        if (changes.PenetrationChanceObstacle && (props.PenetrationChanceObstacle is not null))
        {
            dbProps.PenetrationChanceObstacle = props.PenetrationChanceObstacle;
        }

        if (changes.PenetrationPowerDiviation && (props.PenetrationPowerDiviation is not null))
        {
            dbProps.PenetrationPowerDiviation = props.PenetrationPowerDiviation;
        }

        if (changes.PenetrationPower && (props.PenetrationPower is not null))
        {
            dbProps.PenetrationPower = props.PenetrationPower;
        }

        if (changes.StackMaxSize && (props.StackMaxSize is not null))
        {
            dbProps.StackMaxSize = props.StackMaxSize;
        }

        if (changes.ConflictingItems && (props.ConflictingItems is not null))
        {
            var added = props.ConflictingItems[0];
            var removed = props.ConflictingItems[1];
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
