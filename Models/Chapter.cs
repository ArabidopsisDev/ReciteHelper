using System.Text.Json.Serialization;

namespace ReciteHelper.Models;

public class Chapter
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("bank")]
    public List<Question>? Questions { get; set; }
}
