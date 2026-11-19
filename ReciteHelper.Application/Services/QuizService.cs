using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Application.Services;

public class QuizService : IQuizService
{
    private readonly IConfigService _configService;
    private readonly IAnswerJudge _judgeService;
    private readonly IReviewScheduler _scheduler;
    private readonly IAiAnswerGrader _aiGrader;
    private readonly IFacetReviewService _facetReviews;

    public QuizService(IConfigService configService, IAnswerJudge judgeService, IReviewScheduler scheduler, IAiAnswerGrader aiGrader,
        IFacetReviewService? facetReviews = null)
    {
        _configService = configService;
        _judgeService = judgeService;
        _scheduler = scheduler;
        _aiGrader = aiGrader;
        _facetReviews = facetReviews ?? new FacetReviewService(scheduler);
    }

    public async Task<AnswerResult> ProcessAnswerAsync(Project project, Question question, string userAnswer, DateTime startTime)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(question);
        userAnswer ??= string.Empty;
        var submittedAt = DateTime.Now;
        var config = await _configService.LoadAsync();

        var assessment = question.RequiresAiGrading || question.IsWritten &&
            (project.TemplateType == Core.Enums.ProjectTemplateType.RuleBasedReview || ModelAccess.HasTextGeneration(config))
            ? await _aiGrader.GradeAsync(question, userAnswer) : null;
        var missedBlanks = new List<string>();
        var (isCorrect, similarity, scoreRatio) = assessment is not null
            ? (assessment.IsCorrect, assessment.ScoreRatio, assessment.RawScoreRatio ?? assessment.ScoreRatio)
            : await EvaluateAnswerAsync(question, userAnswer, missedBlanks);

        var now = submittedAt;
        var responseSeconds = Math.Max(0d, (now - startTime).TotalSeconds);
        var rate = userAnswer.Length / Math.Max(0.1d, responseSeconds);
        var rRelative = config.RStandard > 0 ? rate / config.RStandard : 0d;

        var facetUpdate = assessment is null ? null : _facetReviews.RecordAssessment(project, question, assessment, now);
        // Each observed component uses the FSRS binary recall interface.
        var grade = ReviewGrader.FromJudgement(isCorrect);
        var outcome = _scheduler.Record(project, question, grade, now);
        if (facetUpdate is not null)
        {
            var parent = ReviewQuestionCatalog.FindFacet(project, question)?.Parent ?? question;
            facetUpdate = facetUpdate with { ParentNextIntervalDays = CfsrsPolicy.NextReviewInterval(
                parent, now, _scheduler.DesiredRetention, _scheduler.ParametersFor(project)) };
        }

        var reviewTag = new ReviewTag
        {
            Time = now,
            Similarity = similarity,
            ScoreRatio = scoreRatio,
            Rate = rRelative,
            QValue = isCorrect ? 4 : 1,
            Grade = (int)grade,
            IsCorrect = isCorrect,
            Verdict = AiGradingPolicy.GetRecordedVerdict(isCorrect,
                assessment?.RawScoreRatio ?? assessment?.ScoreRatio, assessment?.HasFallacy is true),
            EFValue = question.EFValue,
            NextIntervalDays = !question.IsReviewFacet && facetUpdate is not null
                ? facetUpdate.ParentNextIntervalDays : outcome.NextIntervalDays,
            ElapsedDays = outcome.ElapsedDays,
            Retrievability = outcome.RetrievabilityBefore,
            ResponseSeconds = responseSeconds,
            AnswerLength = userAnswer.Length,
            Stability = outcome.StateAfter.Stability,
            Difficulty = outcome.StateAfter.Difficulty
        };
        reviewTag.SetId(question.ReviewTag.Count + 1);
        question.ReviewTag.Add(reviewTag);
        question.Status = isCorrect;
        question.SubmittedAnswer = userAnswer;
        question.AnswerCleared = false;
        question.LastAiAssessment = assessment;
        ReviewMistakes.Record(project, question, now, isCorrect, assessment, missedBlanks: missedBlanks);
        if (assessment is not null)
        {
            if (question.AnswerNature == Core.Enums.AnswerNature.None)
                question.AnswerNature = assessment.AnswerNature;
        }

        return new AnswerResult
        {
            AiAssessment = assessment,
            FacetUpdate = facetUpdate,
            IsCorrect = isCorrect,
            Similarity = similarity,
            RRelative = rRelative,
            ReviewTag = reviewTag,
            Outcome = outcome
        };
    }

    private async Task<(bool IsCorrect, double Similarity, double ScoreRatio)> EvaluateAnswerAsync(
        Question question,
        string userAnswer,
        List<string> missedBlanks)
    {
        if (question.IsSingleChoice)
        {
            var correctAnswer = question.GetCorrectAnswerText();
            var correct = question.IsCorrectChoiceAnswer(userAnswer);
            return (correct, await _judgeService.CalculateSimilarityAsync(userAnswer, correctAnswer), correct ? 1d : 0d);
        }

        if (question.IsTrueFalse)
        {
            var correctAnswer = question.GetCorrectAnswerText();
            var correct = question.IsCorrectTrueFalseAnswer(userAnswer);
            var similarity = await _judgeService.CalculateSimilarityAsync(Question.NormalizeTrueFalseAnswer(userAnswer), correctAnswer);
            return (correct, similarity, correct ? 1d : 0d);
        }

        if (question.IsFillBlank)
        {
            var userAnswers = Question.SplitBlankAnswers(userAnswer);
            var correctAnswers = question.GetCorrectAnswers();
            if (userAnswers.Count != correctAnswers.Count || correctAnswers.Count == 0)
                return (false, 0d, 0d);

            var correctCount = 0;
            var similarity = 0d;
            for (var index = 0; index < correctAnswers.Count; index++)
            {
                if (await _judgeService.JudgeAsync(userAnswers[index], correctAnswers[index])) correctCount++;
                else missedBlanks.Add($"第 {index + 1} 空填写「{correctAnswers[index]}」。");
                similarity += await _judgeService.CalculateSimilarityAsync(userAnswers[index], correctAnswers[index]);
            }

            return (correctCount == correctAnswers.Count, similarity / correctAnswers.Count, (double)correctCount / correctAnswers.Count);
        }

        var answer = question.GetCorrectAnswerText();
        var isCorrect = await _judgeService.JudgeAsync(userAnswer, answer);
        var writtenSimilarity = await _judgeService.CalculateSimilarityAsync(userAnswer, answer);
        return (isCorrect, writtenSimilarity, Math.Clamp(writtenSimilarity, 0, 1));
    }
}
