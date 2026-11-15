using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Interfaces.Services;

public interface IExamAnswerService
{
    Task<DTOs.AiAnswerAssessment?> GradeAsync(Question question, string? userAnswer);
    bool IsCorrect(Question question, string? userAnswer);
}
