namespace ReciteHelper.Core.Entities;

public sealed record ReviewMistake(Guid QuestionId, DateTime Time, string Question, bool IsExam,
    IReadOnlyList<MistakeCorrection> Corrections);

public sealed record MistakeCorrection(string Action, string Text, string? Part = null);
