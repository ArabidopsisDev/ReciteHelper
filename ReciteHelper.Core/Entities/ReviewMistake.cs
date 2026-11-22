using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Entities;

public sealed record ReviewMistake(Guid QuestionId, DateTime Time, string Question, bool IsExam,
    IReadOnlyList<MistakeCorrection> Corrections)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MistakeAnswerSpan>? Answer { get; init; }
}

public sealed record MistakeCorrection(string Action, string Text, string? Part = null);

public sealed record MistakeAnswerSpan(string Text, string Status = "correct");
