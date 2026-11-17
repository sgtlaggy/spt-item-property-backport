using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Utils;

using Path = System.IO.Path;

namespace ItemPropertyBackport;

[Injectable(TypePriority = OnLoadOrder.Preload + 4)]
public class Mod(
    Config config,
    RagfairConfig _ragfairConfig,
    TemplateTable _templates,
    GlobalTable _globals,
    LocaleTable _locales,
#if DEBUG
    JsonUtil _json,
#endif
    ItemBaseClassService _baseClassService,
    ModHelper _modHelper,
    DataService _dataService,
    ISptLogger<Mod> _logger
) : IOnLoad
{
    private string modDir = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
    private Dictionary<string, List<(double?, double?)>> localeOverrides = [];

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        await UpdateItems();

        if (config.UpdateGunsmith)
        {
            await UpdateQuests();
            UpdateLocales();
        }

        if (config.UnblacklistPricedItems)
        {
            await UpdateFleaBlacklist();
        }

        if (config.RemoveAmmoboxFleaLimit)
        {
            _ragfairConfig.Dynamic.OfferItemCount.Remove(BaseClasses.AMMO_BOX);
            _ragfairConfig.Dynamic.ShowAsSingleStack.Remove(BaseClasses.AMMO_BOX);
        }
    }

    private async Task UpdateItems()
    {
        Dictionary<MongoId, ItemProperties>? changes;
        try
        {
            changes = await _dataService.GetItemChanges(config.AutoUpdate, config.AutoUpdateDays);
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] {e.Message}");
            return;
        }

        var items = _templates.Items;

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

            var hasChanges = changes.TryGetValue(item.Id, out liveProps);
            if (!hasChanges || (liveProps is null))
            {
                continue;
            }

            if (ContainsItemOrParent(config.ExcludeItems, item))
            {
                continue;
            }

            if ((config.IncludeItems.Count > 0) && !ContainsItemOrParent(config.IncludeItems, item))
            {
                continue;
            }

            if (liveProps.Properties is not null)
            {
                try
                {
                    UpdateBasicProperties(config, item.Properties, liveProps.Properties);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to update all properties for {item.Id}.", ex);
                }
            }
            if (liveProps.SpecialProperties is not null)
            {
                try
                {
                    UpdateSpecialProperties(config, item.Properties, liveProps.SpecialProperties);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to update all special properties for {item.Id}.", ex);
                }
            }
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "items_after.json"), _json.Serialize(items, true));
#endif
    }

    public bool ContainsItemOrParent(HashSet<MongoId> set, TemplateItem item)
    {
        return set.Contains(item.Id) || _baseClassService.ItemHasBaseClass(item.Id, set);
    }

    private void UpdateBasicProperties(Config config, TemplateItemProperties dbProps, TemplateItemProperties props)
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
    }

    private void UpdateSpecialProperties(Config config, TemplateItemProperties dbProps, SpecialCaseProperties props)
    {
        if ((props.ConflictingItems is not null) && !config.ExcludeProperties.Contains("ConflictingItems"))
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

        if ((props.Buffs is not null) && !config.ExcludeProperties.Contains("Buffs"))
        {
            var allBuffs = _globals.Configuration.Health.Effects.Stimulator.Buffs;
            if ((!String.IsNullOrEmpty(dbProps.StimulatorBuffs))
                && allBuffs.TryGetValue(dbProps.StimulatorBuffs, out var _buffs))
            {
                var buffs = _buffs.ToList();
                List<int> indicesToRemove = [];
                foreach (var (index, buff) in props.Buffs)
                {
                    if (buff is null)
                    {
                        indicesToRemove.Add(index);
                    }
                    else if (index >= buffs.Count)
                    {
                        buffs.Add(buff);
                    }
                    else
                    {
                        buffs[index] = buff;
                    }
                }

                indicesToRemove.Sort();
                indicesToRemove.Reverse();
                foreach (var index in indicesToRemove)
                {
                    buffs.RemoveAt(index);
                }

                allBuffs[dbProps.StimulatorBuffs] = buffs;
            }
        }

        if ((props.EffectsHealth is not null) && !config.ExcludeProperties.Contains("EffectsHealth"))
        {
            if (dbProps.EffectsHealth is null)
            {
                dbProps.EffectsHealth = [];
            }
            UpdateOrRemoveFromDictionary(dbProps.EffectsHealth, props.EffectsHealth);
        }

        if ((props.EffectsDamage is not null) && !config.ExcludeProperties.Contains("EffectsDamage"))
        {
            if (dbProps.EffectsDamage is null)
            {
                dbProps.EffectsDamage = [];
            }
            UpdateOrRemoveFromDictionary(dbProps.EffectsDamage, props.EffectsDamage);
        }
    }

    private void UpdateOrRemoveFromDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> dict,
        Dictionary<TKey, TValue?> updateFrom
    ) where TKey : notnull
    {
        foreach (var (key, value) in updateFrom)
        {
            if (value is not null)
            {
                dict[key] = value;
            }
            else if (dict.ContainsKey(key))
            {
                dict.Remove(key);
            }
        }
    }

    private async Task UpdateQuests()
    {
        var changes = await _dataService.GetQuestChanges(config.AutoUpdate, config.AutoUpdateDays);
        var quests = _templates.Quests;

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

    /// <summary>
    /// Lifts SPT's flea blacklist for every item that actually trades on the flea
    /// market in live Tarkov (i.e. it has a 24h average price).
    ///
    /// This deliberately does NOT touch any price value — it only flips
    /// 'CanSellOnRagfair' and drops the id from the custom blacklist.
    /// </summary>
    private async Task UpdateFleaBlacklist()
    {
        HashSet<MongoId> tradable;
        try
        {
            tradable = await _dataService.GetFleaTradable(config.AutoUpdate, config.AutoUpdateDays);
        }
        catch (Exception e)
        {
            _logger.Error($"[ItemPropertyBackport] {e.Message}");
            return;
        }

        if (tradable.Count == 0)
        {
            _logger.Warning("[ItemPropertyBackport] no flea tradable data available, blacklist left as is.");
            return;
        }

        var items = _templates.Items;
        var custom = _ragfairConfig.Dynamic.Blacklist.Custom;
        var inclusive = config.IncludeItems.Count > 0;

        var liftedNow = 0;
        var alreadyTradable = 0;
        var removedFromCustom = 0;

        foreach (var id in tradable)
        {
            if (!items.TryGetValue(id, out var item) || item.Properties is null)
            {
                continue;
            }

            if (ContainsItemOrParent(config.ExcludeItems, item))
            {
                continue;
            }

            if (inclusive && !ContainsItemOrParent(config.IncludeItems, item))
            {
                continue;
            }

            if (item.Properties.CanSellOnRagfair != true)
            {
                item.Properties.CanSellOnRagfair = true;
                liftedNow++;
            }
            else
            {
                alreadyTradable++;
            }

            if (custom.Remove(id))
            {
                removedFromCustom++;
            }
        }

        _logger.Success(
            $"[ItemPropertyBackport] flea blacklist: {tradable.Count} priced items are tradable "
            + $"({alreadyTradable} already were, {liftedNow} lifted now), "
            + $"{removedFromCustom} removed from the custom list; prices untouched."
        );
    }

    private void UpdateLocales()
    {
        var locales = _locales.Global;

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "locale_before.json"), _json.Serialize(locales["en"].Value, true));
#endif

        foreach (var locale in _locales.Global.Values)
        {
            locale.AddTransformer(UpdateLocale);
        }

#if DEBUG
        File.WriteAllText(Path.Join(modDir, "locale_after.json"), _json.Serialize(locales["en"].Value, true));
#endif
    }

    private GlobalLocaleDictionary? UpdateLocale(GlobalLocaleDictionary? loc)
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
