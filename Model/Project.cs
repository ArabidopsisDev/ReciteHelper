using System.Text.Json.Serialization;

namespace ReciteHelper.Model;

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

    public List<Question> ExportQuestions()
    {
        List<Question> questions = [];

        foreach (var chapter in Chapters!)
            questions.AddRange(chapter.Questions!);
        return questions;
    }
}
