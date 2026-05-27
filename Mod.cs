using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    private Dictionary<string, List<(double?, double?)>> localeOverrides = [];

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

        if (config.UpdateGunsmith)
        {
            await UpdateQuests(config);
            UpdateLocales();
        }
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

    private async Task UpdateQuests(Config config)
    {
        var changes = await _dataService.GetQuestChanges();
        var quests = _db.GetQuests();

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "quests_before.json"), _json.Serialize(quests, true));
#endif

        foreach (var (questId, questConditions) in changes)
        {
            Quest? quest;
            if (!quests.TryGetValue(questId, out quest))
            {
                _logger.Warning($"[ItemPropertyBackport] Quest {questId} not found.");
                continue;
            }
            else if (quest.Conditions.AvailableForFinish is null)
            {
                _logger.Warning($"[ItemPropertyBackport] Quest {questId} has null conditions.");
                continue;
            }

            List<(double?, double?)> descriptionReplacements = [];
            foreach (var sptCondition in quest.Conditions.AvailableForFinish)
            {
                QuestCondition? newCondition;
                if (!questConditions.TryGetValue(sptCondition.Id, out newCondition))
                {
                    continue;
                }

                foreach (var prop in typeof(QuestCondition).GetProperties())
                {
                    // ignore Id, DynamicLocale, ConditionType
                    if (prop.GetCustomAttribute<RequiredMemberAttribute>() is not null)
                    {
                        continue;
                    }

                    var newProp = prop.GetValue(newCondition);
                    if (newProp is null)
                    {
                        continue;
                    }

                    var liveCompare = newProp as ValueCompare;
                    if (liveCompare is not null)
                    {
                        var sptCompare = (ValueCompare)prop.GetValue(sptCondition)!;
                        descriptionReplacements.Add((liveCompare.Value, sptCompare.Value));
                    }

                    prop.SetValue(sptCondition, newProp);
                }
            }

            if (descriptionReplacements.Count > 0)
            {
                localeOverrides.Add(quest.Description, descriptionReplacements);
            }
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "quests_after.json"), _json.Serialize(quests, true));
#endif
    }

    private void UpdateLocales()
    {
        var locales = _db.GetLocales().Global;

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "locale_before.json"), _json.Serialize(locales["en"].Value, true));
#endif

        foreach (var locale in _db.GetLocales().Global.Values)
        {
            locale.AddTransformer(UpdateLocale);
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "locale_after.json"), _json.Serialize(locales["en"].Value, true));
#endif
    }

    private Dictionary<string, string>? UpdateLocale(Dictionary<string, string>? loc)
    {
        if (loc is null)
        {
            return loc;
        }

        foreach (var (key, replacements) in localeOverrides)
        {
            string? desc;
            if (!loc.TryGetValue(key, out desc))
            {
                continue;
            }

            foreach (var (live, spt) in replacements)
            {
                desc = Regex.Replace(desc, $"\\b{spt}\\b", live.ToString()!);
            }
            loc[key] = desc;
        }

        return loc;
    }
}
