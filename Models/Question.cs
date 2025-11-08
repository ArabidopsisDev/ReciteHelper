using System.Text.Json.Serialization;

namespace ReciteHelper.Models;

public class Question
{
    /// <summary>
    /// The status of the answers is indicated by a value of null (no answer), 
    /// true (correct answer), and false (incorrect answer)
    /// </summary>
    [JsonPropertyName("status")]
    public bool? Status { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("user_answer")]
    public string? UserAnswer { get; set; } = null;

    [JsonPropertyName("correct_answer")]
    public string? CorrectAnswer { get; set; }
}
