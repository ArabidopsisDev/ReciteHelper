using System.Text.Json.Serialization;

namespace ReciteHelper.Model;

[Serializable]
public class Text
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}