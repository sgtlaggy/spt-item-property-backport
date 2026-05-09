using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;

namespace ItemPropertyBackport;

public record Config
{
    [JsonPropertyName("ChangeProperties")]
    public ConfigProperties ChangeProperties { get; set; } = new();

    [JsonPropertyName("ExemptItems")]
    public HashSet<MongoId> ExemptItems { get; set; } = [];

    [JsonPropertyName("OnlyItems")]
    public List<MongoId> OnlyItems { get; set; } = [];
}

public record ConfigProperties
{
    [JsonPropertyName("BlocksEarpiece")]
    public bool BlocksEarpiece { get; set; }

    [JsonPropertyName("Weight")]
    public bool Weight { get; set; }

    [JsonPropertyName("Ergonomics")]
    public bool Ergonomics { get; set; }

    [JsonPropertyName("Loudness")]
    public bool Loudness { get; set; }

    [JsonPropertyName("Accuracy")]
    public bool Accuracy { get; set; }

    [JsonPropertyName("Recoil")]
    public bool Recoil { get; set; }

    [JsonPropertyName("Damage")]
    public bool Damage { get; set; }

    [JsonPropertyName("ArmorDamage")]
    public bool ArmorDamage { get; set; }

    [JsonPropertyName("InitialSpeed")]
    public bool InitialSpeed { get; set; }

    [JsonPropertyName("Velocity")]
    public bool Velocity { get; set; }

    [JsonPropertyName("LightBleedingDelta")]
    public bool LightBleedingDelta { get; set; }

    [JsonPropertyName("HeavyBleedingDelta")]
    public bool HeavyBleedingDelta { get; set; }

    [JsonPropertyName("PenetrationChanceObstacle")]
    public bool PenetrationChanceObstacle { get; set; }

    [JsonPropertyName("PenetrationPowerDiviation")]
    public bool PenetrationPowerDiviation { get; set; }

    [JsonPropertyName("PenetrationPower")]
    public bool PenetrationPower { get; set; }

    [JsonPropertyName("StackMaxSize")]
    public bool StackMaxSize { get; set; }

    [JsonPropertyName("ConflictingItems")]
    public bool ConflictingItems { get; set; }
}

public record ItemProperties
{
    [JsonPropertyName("BlocksEarpiece")]
    public bool? BlocksEarpiece { get; set; }

    [JsonPropertyName("Weight")]
    public double? Weight { get; set; }

    [JsonPropertyName("Ergonomics")]
    public double? Ergonomics { get; set; }

    [JsonPropertyName("Loudness")]
    public double? Loudness { get; set; }

    [JsonPropertyName("Accuracy")]
    public double? Accuracy { get; set; }

    [JsonPropertyName("Recoil")]
    public double? Recoil { get; set; }

    [JsonPropertyName("Damage")]
    public double? Damage { get; set; }

    [JsonPropertyName("ArmorDamage")]
    public double? ArmorDamage { get; set; }

    [JsonPropertyName("InitialSpeed")]
    public double? InitialSpeed { get; set; }

    [JsonPropertyName("Velocity")]
    public double? Velocity { get; set; }

    [JsonPropertyName("LightBleedingDelta")]
    public double? LightBleedingDelta { get; set; }

    [JsonPropertyName("HeavyBleedingDelta")]
    public double? HeavyBleedingDelta { get; set; }

    [JsonPropertyName("PenetrationChanceObstacle")]
    public double? PenetrationChanceObstacle { get; set; }

    [JsonPropertyName("PenetrationPowerDiviation")]
    public double? PenetrationPowerDiviation { get; set; }

    [JsonPropertyName("PenetrationPower")]
    public int? PenetrationPower { get; set; }

    [JsonPropertyName("StackMaxSize")]
    public int? StackMaxSize { get; set; }

    [JsonPropertyName("ConflictingItems")]
    public List<HashSet<MongoId>>? ConflictingItems { get; set; }
}
