using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;

namespace ReciteHelper.Core.Scheduling;

public static class FacetAssessmentValidator
{
    public static IReadOnlyList<FacetAssessment> Prepare(Question question, AiAnswerAssessment assessment, bool requireFacets = false)
    {
        if (!double.IsFinite(assessment.ScoreRatio) || assessment.ScoreRatio is < 0 or > 1 ||
            assessment.IsCorrect != (!assessment.HasFallacy && (assessment.RawScoreRatio ?? assessment.ScoreRatio) >= AiGradingPolicy.MasteryThreshold)) throw Invalid();
        if (assessment.Facets is null) throw Invalid();
        if (question.IsReviewFacet)
        {
            if (assessment.Facets.Count > 0) throw Invalid();
            return [];
        }
        if (assessment.Facets.Count == 0)
        {
            if (question.ReviewFacets.Count > 0 && !EssayAnswerLayout.NeedsFacetRegrouping(question) ||
                requireFacets && EssayAnswerLayout.Parts(question).Count > 1) throw Invalid();
            return [];
        }
        if (assessment.Facets.Count is < 2 or > 16 || assessment.Facets.Any(f => f is null ||
            string.IsNullOrWhiteSpace(f.Key) || string.IsNullOrWhiteSpace(f.Name) ||
            string.IsNullOrWhiteSpace(f.Question) || string.IsNullOrWhiteSpace(f.ReferenceAnswer) ||
            string.IsNullOrWhiteSpace(f.WeightReason) || !double.IsFinite(f.Weight) || f.Weight <= 0 ||
            !double.IsFinite(f.ScoreRatio) || f.ScoreRatio is < 0 or > 1 || f.IsCorrect != (f.ScoreRatio >= 1 - 1e-9))) throw Invalid();
        if (assessment.Facets.Select(f => f.Key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assessment.Facets.Count ||
            assessment.Facets.Select(f => f.Question.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assessment.Facets.Count ||
            assessment.Facets.Any(f => string.Equals(f.Question.Trim(), question.Text?.Trim(), StringComparison.OrdinalIgnoreCase))) throw Invalid();
        var total = assessment.Facets.Sum(f => f.Weight);
        if (!double.IsFinite(total) || total <= 0) throw Invalid();
        var prepared = assessment.Facets.Select(f => new FacetAssessment
        {
            Key = f.Key.Trim(), Name = f.Name.Trim(), Weight = f.Weight / total,
            WeightReason = f.WeightReason.Trim(), ScoreRatio = f.ScoreRatio, RawScoreRatio = f.RawScoreRatio, IsCorrect = f.IsCorrect,
            AnswerNature = f.AnswerNature,
            HasFallacy = f.HasFallacy, FallacyReason = f.FallacyReason, Explanation = f.Explanation,
            Question = f.Question.Trim(), ReferenceAnswer = f.ReferenceAnswer.Trim()
        }).ToList();
        if (Math.Abs(prepared.Sum(f => f.Weight * f.ScoreRatio) - assessment.ScoreRatio) > 0.03) throw Invalid();
        if (question.ReviewFacets.Count > 0)
        {
            var known = EssayAnswerLayout.NeedsFacetRegrouping(question)
                ? EssayAnswerLayout.Parts(question).ToDictionary(p => p.Key, p => p.Weight, StringComparer.OrdinalIgnoreCase)
                : question.ReviewFacets.ToDictionary(f => f.Key, f => f.Weight, StringComparer.OrdinalIgnoreCase);
            if (known.Count != prepared.Count || prepared.Any(f => !known.TryGetValue(f.Key, out var oldWeight) || Math.Abs(oldWeight - f.Weight) > 1e-6)) throw Invalid();
        }
        return prepared;
    }

    private static InvalidDataException Invalid() => new("AI 返回的切面范围、占比或评分不一致，请重新提交答案。");
}
