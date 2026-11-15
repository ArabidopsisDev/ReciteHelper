using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Entities;
using ReciteHelper.Infrastructure.Utilities;

namespace ReciteHelper.Infrastructure.Services;

public sealed class ExamAnswerService(IAiAnswerGrader aiGrader) : IExamAnswerService
{
    public async Task<Core.DTOs.AiAnswerAssessment?> GradeAsync(Question question, string? userAnswer)
    {
        if (!question.RequiresAiGrading) return null;
        var assessment = await aiGrader.GradeAsync(question, userAnswer ?? string.Empty);
        question.LastAiAssessment = assessment;
        return assessment;
    }

    public bool IsCorrect(Question question, string? userAnswer)
    {
        if (question.RequiresAiGrading)
            return question.LastAiAssessment is { } assessment && assessment.UserAnswer == (userAnswer ?? string.Empty) && assessment.IsCorrect;
        return JudgeAnswer.Run(question, userAnswer);
    }
}
