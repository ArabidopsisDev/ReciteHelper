using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Entities;
using ReciteHelper.Infrastructure.Utilities;

namespace ReciteHelper.Infrastructure.Services;

public sealed class ExamAnswerService(IAiAnswerGrader aiGrader, IFacetReviewService? facetReviews = null) : IExamAnswerService
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

    public async Task<Core.DTOs.AiAnswerAssessment?> GradeForReviewAsync(Core.Aggregates.Project project, Question question,
        string? userAnswer, DateTime reviewedAt)
    {
        var original = Core.Scheduling.ReviewQuestionCatalog.FindOriginal(project, question);
        if (original is null) return await GradeAsync(question, userAnswer);
        if (project.TemplateType == Core.Enums.ProjectTemplateType.RuleBasedReview) original.RequiresAiGrading = true;
        var assessment = await GradeAsync(original, userAnswer);
        if (assessment is not null && facetReviews is not null)
            facetReviews.RecordExamAssessment(project, original, assessment, reviewedAt);
        if (assessment is not null)
        {
            original.Status = assessment.IsCorrect;
            question.LastAiAssessment = assessment;
        }
        return assessment;
    }
}
