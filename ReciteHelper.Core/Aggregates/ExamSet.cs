using ReciteHelper.Core.Entities;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Aggregates;

public sealed class ExamSet
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subject_name")]
    public string SubjectName { get; set; } = string.Empty;

    [JsonPropertyName("small_title")]
    public string SmallTitle { get; set; } = string.Empty;

    [JsonPropertyName("main_title")]
    public string MainTitle { get; set; } = string.Empty;

    [JsonPropertyName("source_file_name")]
    public string SourceFileName { get; set; } = string.Empty;

    [JsonPropertyName("suggested_duration_minutes")]
    public int SuggestedDurationMinutes { get; set; } = 60;

    [JsonPropertyName("imported_at_utc")]
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("questions")]
    public List<ExamSetQuestion> Questions { get; set; } = [];

    [JsonPropertyName("review_mistakes")]
    public List<ReviewMistake> ReviewMistakes { get; set; } = [];

    [JsonIgnore]
    public string DisplayName => $"{Title}（{Questions.Count}题）";

    [JsonIgnore]
    public string ResolvedSmallTitle => string.IsNullOrWhiteSpace(SmallTitle) ? Title : SmallTitle;

    [JsonIgnore]
    public string ResolvedMainTitle => string.IsNullOrWhiteSpace(MainTitle) ? SubjectName : MainTitle;
}
