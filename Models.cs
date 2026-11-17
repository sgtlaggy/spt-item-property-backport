using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ItemPropertyBackport;

public record Config
{
    [JsonPropertyName("ExcludeProperties")]
    public required HashSet<string> ExcludeProperties { get; set; }

    [JsonPropertyName("IncludeItems")]
    public required HashSet<MongoId> IncludeItems { get; set; }

    [JsonPropertyName("ExcludeItems")]
    public required HashSet<MongoId> ExcludeItems { get; set; }

    [JsonPropertyName("RemoveAmmoboxFleaLimit")]
    public bool RemoveAmmoboxFleaLimit { get; set; }

    [JsonPropertyName("UpdateGunsmith")]
    public bool UpdateGunsmith { get; set; }

    /// <summary>
    /// Lift SPT's flea blacklist for items that have a live flea market price.
    /// Prices themselves are never modified.
    /// </summary>
    [JsonPropertyName("UnblacklistPricedItems")]
    public bool UnblacklistPricedItems { get; set; } = true;

    /// <summary>
    /// When the db files are missing (or older than <see cref="AutoUpdateDays"/>), pull
    /// fresh data from json.tarkov.dev instead of the mod's bundled files.
    /// </summary>
    [JsonPropertyName("AutoUpdate")]
    public bool AutoUpdate { get; set; } = true;

    /// <summary>
    /// Re-download when the db files are older than this many days. 0 (default) only
    /// refreshes when a file is missing, i.e. delete items.json to force an update.
    /// </summary>
    [JsonPropertyName("AutoUpdateDays")]
    public int AutoUpdateDays { get; set; }
}

public record SpecialCaseProperties
{
    [JsonPropertyName("ConflictingItems")]
    public List<HashSet<MongoId>>? ConflictingItems { get; set; }

    [JsonPropertyName("Buffs")]
    public Dictionary<int, Buff?>? Buffs { get; set; }

    [JsonPropertyName("EffectsHealth")]
    public Dictionary<HealthFactor, EffectsHealthProperties?>? EffectsHealth { get; set; }

    [JsonPropertyName("EffectsDamage")]
    public Dictionary<DamageEffectType, EffectsDamageProperties?>? EffectsDamage { get; set; }
}

public record ItemProperties
{
    [JsonPropertyName("properties")]
    public TemplateItemProperties? Properties { get; set; }

    [JsonPropertyName("special_properties")]
    public SpecialCaseProperties? SpecialProperties { get; set; }
}
