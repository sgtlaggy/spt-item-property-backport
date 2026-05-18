using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ItemPropertyBackport;

public record Config
{
    [JsonPropertyName("ExcludeProperties")]
    public HashSet<string> ExcludeProperties { get; set; } = new();

    [JsonPropertyName("IncludeItems")]
    public List<MongoId> IncludeItems { get; set; } = [];

    [JsonPropertyName("ExcludeItems")]
    public HashSet<MongoId> ExcludeItems { get; set; } = [];
}

public record ItemProperties : TemplateItemProperties
{
    [JsonPropertyName("ConflictingItemsDiff")]
    public List<HashSet<MongoId>>? ConflictingItemsDiff { get; set; }
}
