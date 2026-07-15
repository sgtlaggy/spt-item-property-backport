using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace ItemPropertyBackport;

public record Config
{
    [JsonPropertyName("ExcludeProperties")]
    public HashSet<string> ExcludeProperties { get; set; } = new();

    [JsonPropertyName("IncludeItems")]
    public HashSet<MongoId> IncludeItems { get; set; } = [];

    [JsonPropertyName("ExcludeItems")]
    public HashSet<MongoId> ExcludeItems { get; set; } = [];

    [JsonPropertyName("AllPrices")]
    public bool AllPrices { get; set; }

    [JsonPropertyName("UnblacklistedPrices")]
    public bool UnblacklistedPrices { get; set; }

    [JsonPropertyName("RemoveAmmoboxFleaLimit")]
    public bool RemoveAmmoboxFleaLimit { get; set; }

    [JsonPropertyName("UpdateGunsmith")]
    public bool UpdateGunsmith { get; set; }
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

public record Prices
{
    [JsonPropertyName("Handbook")]
    public double? Handbook { get; set; }

    [JsonPropertyName("Flea")]
    public double? Flea { get; set; }
}
