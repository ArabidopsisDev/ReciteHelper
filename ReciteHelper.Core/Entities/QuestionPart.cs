using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Entities;

/// <summary>A natural subquestion and the essential course-level answer expected for it.</summary>
public sealed class QuestionPart
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;
    [JsonPropertyName("reference_answer")]
    public string ReferenceAnswer { get; set; } = string.Empty;
    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1;
}
