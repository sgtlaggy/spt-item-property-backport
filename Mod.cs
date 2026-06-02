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
    private HashSet<MongoId> unblacklistedItems = [];

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
            await UpdateQuests();
            UpdateLocales();
        }

        if (config.AllPrices || config.UnblacklistedPrices)
        {
            await UpdatePrices(config);
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

        ItemProperties? liveProps;
        foreach (var item in items.Values)
        {
            if (item.Properties is null)
            {
                continue;
            }

            if ((config.IncludeItems.Count > 0) && !ContainsItemOrParent(config.IncludeItems, item))
            {
                continue;
            }

            if (ContainsItemOrParent(config.ExcludeItems, item))
            {
                continue;
            }

            var hasChanges = changes.TryGetValue(item.Id, out liveProps);
            if (!hasChanges || (liveProps is null))
            {
                continue;
            }

            var couldSellOnFleaBeforeUpdate = item.Properties.CanSellOnRagfair ?? false;

            UpdateItem(config, item.Properties, liveProps);

            var canSellOnFleaAfterUpdate = item.Properties.CanSellOnRagfair ?? false;
            if (canSellOnFleaAfterUpdate && !couldSellOnFleaBeforeUpdate)
            {
                unblacklistedItems.Add(item.Id);
            }
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "items_after.json"), _json.Serialize(items, true));
#endif
    }

    public bool ContainsItemOrParent(HashSet<MongoId> set, TemplateItem item)
    {
        return set.Contains(item.Id) || set.Contains(item.Parent);
    }

    private void UpdateItem(Config config, TemplateItemProperties dbProps, ItemProperties props)
    {
        var typeProps = typeof(TemplateItemProperties).GetProperties();
        foreach (var prop in typeProps)
        {
            var newValue = prop.GetValue(props);
            if ((newValue is null) || config.ExcludeProperties.Contains(prop.Name))
            {
                continue;
            }

            prop.SetValue(dbProps, newValue);
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

    private async Task UpdateQuests()
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

    private async Task UpdatePrices(Config config)
    {
        var handbook = _db.GetHandbook().Items
                       .Select((hbItem) => new KeyValuePair<MongoId, HandbookItem>(hbItem.Id, hbItem))
                       .ToDictionary();
        var fleaPrices = _db.GetPrices();
        var items = _db.GetItems();

        var inclusive = config.IncludeItems.Count > 0;
        var changes = (await _dataService.GetPriceChanges())
                          .Where((kvp) =>
                          {
                              TemplateItem? item;
                              if (!items.TryGetValue(kvp.Key, out item))
                              {
                                  return false;
                              }

                              if (ContainsItemOrParent(config.ExcludeItems, item))
                              {
                                  return false;
                              }

                              if (inclusive && !ContainsItemOrParent(config.IncludeItems, item))
                              {
                                  return false;
                              }

                              return config.AllPrices || unblacklistedItems.Contains(kvp.Key);
                          })
                          .ToDictionary();

#if DEBUG
        Dictionary<string, object> debugData = [];
        debugData.Add("Handbook", handbook);
        debugData.Add("Flea", fleaPrices);

        File.WriteAllText(Path.Join(modDir, "prices_before.json"), _json.Serialize(debugData, true));
#endif

        foreach (var (mongo, price) in changes)
        {
            if ((price.Handbook is not null) && handbook.TryGetValue(mongo, out var hbItem))
            {
                hbItem.Price = price.Handbook;
            }

            if (price.Flea is not null)
            {
                fleaPrices[mongo] = price.Flea.Value;
            }
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "prices_after.json"), _json.Serialize(debugData, true));
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
