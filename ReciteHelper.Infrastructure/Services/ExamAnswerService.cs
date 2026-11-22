using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Entities;
using ReciteHelper.Infrastructure.Utilities;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed class ExamAnswerService(IAiAnswerGrader aiGrader, IConfigService? configService = null) : IExamAnswerService
{
    public async Task<Core.DTOs.AiAnswerAssessment?> GradeAsync(Question question, string? userAnswer)
    {
        if (!question.RequiresAiGrading && (!question.IsWritten || configService is null ||
            !ModelAccess.HasTextGeneration(await configService.LoadAsync()))) return null;
        return await aiGrader.GradeAsync(question, userAnswer ?? string.Empty);
    }

    public bool IsCorrect(Question question, string? userAnswer)
    {
        return !question.RequiresAiGrading && JudgeAnswer.Run(question, userAnswer);
    }

    public IReadOnlyList<int> FindMissedBlanks(Question question, string? userAnswer)
    {
        var expected = question.GetCorrectAnswers();
        var answers = Question.SplitBlankAnswers(userAnswer);
        return Enumerable.Range(0, expected.Count)
            .Where(index => index >= answers.Count || !JudgeAnswer.Run(answers[index], expected[index])).ToArray();
    }

    public Task<Core.DTOs.AiAnswerAssessment?> GradeForReviewAsync(Core.Aggregates.Project project, Question question,
        string? userAnswer, DateTime reviewedAt)
    {
        // Use the source rubric without writing exam observations into daily review history.
        var original = Core.Scheduling.ReviewQuestionCatalog.FindOriginal(project, question) ?? question;
        var examQuestion = JsonSerializer.Deserialize<Question>(JsonSerializer.Serialize(original))!;
        if (project.TemplateType == Core.Enums.ProjectTemplateType.RuleBasedReview && examQuestion.IsWritten)
            examQuestion.RequiresAiGrading = true;
        return GradeAsync(examQuestion, userAnswer);
    }
}
