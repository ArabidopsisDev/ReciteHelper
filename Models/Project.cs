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

    [JsonPropertyName("bank")]
    public List<Question>? QuestionBank { get;  set; }
}
