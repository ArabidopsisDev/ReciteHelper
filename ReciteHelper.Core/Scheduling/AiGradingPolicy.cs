using ReciteHelper.Core.DTOs;

namespace ReciteHelper.Core.Scheduling;

public static class AiGradingPolicy
{
    public const double MasteryThreshold = 0.7;
    public const double PartialThreshold = 0.45;

    public static string GetVerdict(double coverage, bool hasFallacy = false)
        => hasFallacy ? "FL" : coverage >= MasteryThreshold ? "AC" : coverage >= PartialThreshold ? "PT" : "WA";

    // A critical misconception cannot pass; retain its actual coverage separately.
    private static double Credit(double coverage, bool hasFallacy)
        => hasFallacy ? Math.Min(coverage, Math.BitDecrement(MasteryThreshold))
            : coverage >= MasteryThreshold ? 1 : coverage;

    /// <summary>Keep observed coverage for feedback; apply the same pass rule to each natural part.</summary>
    public static void Apply(AiAnswerAssessment assessment)
    {
        if (!double.IsFinite(assessment.ScoreRatio) || assessment.ScoreRatio is < 0 or > 1 ||
            assessment.Facets.Any(f => !double.IsFinite(f.ScoreRatio) || f.ScoreRatio is < 0 or > 1 || !double.IsFinite(f.Weight) || f.Weight <= 0))
            throw new InvalidDataException("AI 返回的得分比例有误，请重新提交答案。");
        if (assessment.Facets.Count > 0 && Math.Abs(assessment.Facets.Sum(f => f.Weight * f.ScoreRatio) /
            assessment.Facets.Sum(f => f.Weight) - assessment.ScoreRatio) > 0.03)
            throw new InvalidDataException("AI 返回的分问得分与总分不一致，请重新提交答案。");
        assessment.RawScoreRatio ??= assessment.ScoreRatio;
        foreach (var facet in assessment.Facets)
        {
            facet.RawScoreRatio ??= facet.ScoreRatio;
            facet.IsCorrect = !facet.HasFallacy && facet.RawScoreRatio >= MasteryThreshold;
            facet.ScoreRatio = Credit(facet.RawScoreRatio.Value, facet.HasFallacy);
        }
        if (assessment.Facets.Count > 0)
        {
            var total = assessment.Facets.Sum(f => f.Weight);
            assessment.HasFallacy = assessment.Facets.Any(f => f.HasFallacy);
            assessment.ScoreRatio = assessment.Facets.Sum(f => f.Weight * f.ScoreRatio) / total;
            assessment.IsCorrect = assessment.Facets.All(f => f.IsCorrect);
        }
        else
        {
            assessment.IsCorrect = !assessment.HasFallacy && assessment.RawScoreRatio >= MasteryThreshold;
            assessment.ScoreRatio = Credit(assessment.RawScoreRatio.Value, assessment.HasFallacy);
        }
    }
}
