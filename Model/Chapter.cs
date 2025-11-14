using System.Text.Json.Serialization;

namespace ReciteHelper.Model;

public class Chapter
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("bank")]
    public List<Question>? Questions { get; set; }

    [JsonPropertyName("know")]
    public List<KnowledgePoint>? KnowledgePoints { get; set; }
}
