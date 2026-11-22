namespace ReciteHelper.Core.DTOs;

public sealed record StudyAttempt(
    string ProjectPath, string Subject, Guid QuestionId, Guid TopicId, DateTimeOffset Time,
    bool IsExam, bool? IsCorrect, double? Quality, double? ResponseSeconds, double? ExamDurationSeconds,
    double ExpectedSeconds)
{
    public bool HasFallacy { get; init; }
}
