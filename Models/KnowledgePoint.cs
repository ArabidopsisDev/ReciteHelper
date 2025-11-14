using System.Text.Json.Serialization;

namespace ReciteHelper.Models;

public class KnowledgePoint
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("content")]
    public string? ContentMarkdown { get; set; }

    /// <summary>
    /// Mark the knowledge point mastery status as false.
    /// </summary>
    public bool IsMastered { get; set; } = false;
}
