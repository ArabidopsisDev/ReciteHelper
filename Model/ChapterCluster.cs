using System.Text.Json.Serialization;

namespace ReciteHelper.Model;

/// <summary>
/// Represents a cluster of chapters grouped together, identified by a number.
/// </summary>
public class ChapterCluster
{
    [JsonPropertyName("names")]
    public List<string>? Chapters { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("uname")]
    public string? UnifiedName { get; set; }
}
