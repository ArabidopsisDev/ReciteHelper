using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Interfaces.Services;

public interface IExamAnswerService
{
    Task<DTOs.AiAnswerAssessment?> GradeAsync(Question question, string? userAnswer);
    Task<DTOs.AiAnswerAssessment?> GradeForReviewAsync(Aggregates.Project project, Question question, string? userAnswer, DateTime reviewedAt)
        => GradeAsync(question, userAnswer);
    /// <summary>Checks objective answers. AI answers use the assessment returned by grading.</summary>
    bool IsCorrect(Question question, string? userAnswer);
}
