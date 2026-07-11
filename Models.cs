using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

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

public record ItemProperties : TemplateItemProperties
{
    [JsonPropertyName("ConflictingItemsDiff")]
    public List<HashSet<MongoId>>? ConflictingItemsDiff { get; set; }
}

public record Prices
{
    [JsonPropertyName("Handbook")]
    public double? Handbook { get; set; }

    [JsonPropertyName("Flea")]
    public double? Flea { get; set; }
}
