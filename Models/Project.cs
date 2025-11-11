using System.Text.Json.Serialization;

namespace ReciteHelper.Models;

public class Project
{
    [JsonPropertyName("name")]
    public string? ProjectName { get;  set; }

    [JsonPropertyName("path")]
    public string? StoragePath { get;  set; }

    [JsonPropertyName("bankfile")]
    public string? QuestionBankPath { get;  set; }

    [JsonPropertyName("chapter")]
    public List<Chapter>? Chapters { get; set; }

    [JsonPropertyName("last_accessed")]
    public DateTime LastAccessed { get; set; }
}
