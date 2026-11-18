using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.Services;

namespace ReciteHelper.Application.Services;

public sealed class FacetReviewService(IReviewScheduler scheduler) : IFacetReviewService
{
    public FacetReviewUpdate? RecordAssessment(Project project, Question question, AiAnswerAssessment assessment, DateTime reviewedAt)
    {
        var points = FacetAssessmentValidator.Prepare(question, assessment);
        if (question.Memory is { } existing && !existing.State.IsValid)
            throw new InvalidDataException("题目记忆记录无效，请检查项目文件。");
        var parameters = scheduler.ParametersFor(project);
        if (question.IsReviewFacet)
        {
            var source = ReviewQuestionCatalog.FindFacet(project, question)
                ?? throw new InvalidOperationException("找不到切面对应的原题，请重新打开项目。");
            var (parent, facet) = source;
            var before = FacetMemoryProjection.Mastery(parent, reviewedAt, parameters);
            var oldContribution = facet.LastScoreRatio * FacetMemoryProjection.Recall(question.Memory, reviewedAt, parameters);
            var weight = facet.Weight / parent.ReviewFacets.Sum(f => f.Weight);
            // The caller records this direct attempt next; R=1 immediately after that observation.
            var after = Math.Clamp(before + weight * (assessment.ScoreRatio - oldContribution), 0, 1);
            CfsrsPolicy.Observe(facet, assessment.ScoreRatio, assessment.IsCorrect, reviewedAt);
            return Update(parent, facet.Name, weight, before, after);
        }

        if (points.Count == 0)
        {
            if (EssayAnswerLayout.NeedsFacetRegrouping(question)) ArchiveFacets(question);
            return null;
        }
        if (question.ReviewFacets.Count == 0 && points.All(point => point.IsCorrect)) return null;
        var masteryBefore = question.ReviewFacets.Count > 0
            ? FacetMemoryProjection.Mastery(question, reviewedAt, parameters)
            : question.Status is true ? 1d : 0d;
        if (EssayAnswerLayout.NeedsFacetRegrouping(question))
        {
            ArchiveFacets(question);
        }
        if (question.ReviewFacets.Count == 0)
        {
            question.FacetsCreatedAt = reviewedAt;
            question.ReviewFacets = points.Select(point => new ReviewFacet
            {
                Key = point.Key, Name = point.Name, Weight = point.Weight, WeightReason = point.WeightReason,
                Question = new Question { Text = point.Question, CorrectAnswer = point.ReferenceAnswer,
                    Type = QuestionType.ShortAnswer, AnswerNature = point.AnswerNature,
                    RequiresAiGrading = true, ReviewFacetId = Guid.NewGuid(), SourceExcerpts = question.SourceExcerpts.ToList() }
            }).ToList();
            foreach (var facet in question.ReviewFacets)
                SeedKnownSuccesses(question, facet.Question, reviewedAt);
        }
        // Validate every existing component before changing any of them.
        foreach (var facet in question.ReviewFacets)
            if (facet.Question.Memory is { } memory && !memory.State.IsValid)
                throw new InvalidDataException("切面记忆记录无效，请检查项目文件。");

        foreach (var point in points)
        {
            var facet = question.ReviewFacets.Single(f => string.Equals(f.Key, point.Key, StringComparison.OrdinalIgnoreCase));
            if (point.AnswerNature != AnswerNature.None)
                facet.Question.AnswerNature = point.AnswerNature;
            var grade = ReviewGrader.FromJudgement(point.IsCorrect);
            var outcome = scheduler.Record(project, facet.Question, grade, reviewedAt);
            var tag = new ReviewTag
            {
                Time = reviewedAt, Similarity = point.ScoreRatio, Grade = (int)grade, QValue = point.IsCorrect ? 4 : 1,
                IsCorrect = point.IsCorrect, ElapsedDays = outcome.ElapsedDays,
                Retrievability = outcome.RetrievabilityBefore, Stability = outcome.StateAfter.Stability,
                Difficulty = outcome.StateAfter.Difficulty, OptimizerWeight = facet.Weight
            };
            tag.SetId(facet.Question.ReviewTag.Count + 1);
            facet.Question.ReviewTag.Add(tag);
            facet.Question.Status = point.IsCorrect;
            CfsrsPolicy.Observe(facet, point.ScoreRatio, point.IsCorrect, reviewedAt);
        }
        question.Status = assessment.IsCorrect;
        return Update(question, null, null, masteryBefore, FacetMemoryProjection.Mastery(question, reviewedAt, parameters));
    }

    private static void ArchiveFacets(Question question)
    {
        question.ArchivedReviewFacets.AddRange(question.ReviewFacets);
        question.ReviewFacets = [];
        question.FacetsCreatedAt = null;
    }

    private static FacetReviewUpdate Update(Question parent, string? name, double? weight, double before, double after)
        => new(parent.Text ?? string.Empty, name, weight, before, after,
            parent.ReviewFacets.Where(f => f.NeedsReview).Select(f => f.Name).ToList());

    private static void SeedKnownSuccesses(Question parent, Question facet, DateTime createdAt)
    {
        // A complete successful answer is evidence for every component. A failed whole
        // answer cannot identify which component failed, so it supplies no component label.
        // These context observations already belong to the parent's pre-split training history.
        foreach (var original in parent.ReviewTag.Where(tag => tag.IsCorrect is true && tag.Similarity >= 1 - 1e-9 && tag.Time < createdAt).OrderBy(tag => tag.Time))
        {
            var grade = original.Grade > 0 ? (ReviewGrade)original.Grade : ReviewGrader.FromLegacyQuality(original.QValue);
            if (!ReviewGrader.IsRecall(grade)) continue;
            var tag = new ReviewTag { Time = original.Time, Similarity = 1, IsCorrect = true,
                Grade = (int)grade, QValue = 4, OptimizerWeight = 0 };
            tag.SetId(facet.ReviewTag.Count + 1);
            facet.ReviewTag.Add(tag);
        }
    }
}
