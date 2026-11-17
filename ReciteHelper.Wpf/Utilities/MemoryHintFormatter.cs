using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Wpf.Utilities;

/// <summary>Human-readable summary of a scheduler outcome for the quiz window.</summary>
public static class MemoryHintFormatter
{
    public static string Format(AnswerResult result)
    {
        var text = Format(result.Outcome, result.AiAssessment is { IsCorrect: false }
            ? CfsrsPolicy.RelearningDelay.TotalDays : result.Outcome.NextIntervalDays);
        if (result.FacetUpdate is not { } update) return text;
        var message = update.FacetName is not null
            ? $"原题掌握度 {update.MasteryBefore:P0} → {update.MasteryAfter:P0}，本切面占 {update.Weight:P0}。"
            : update.WeakFacets.Count > 0
                ? $"薄弱切面：{string.Join("、", update.WeakFacets)}。cFSRS 将在 10 分钟后安排切面重练。"
                : $"原题掌握度 {update.MasteryAfter:P0}，后续按切面安排复习。";
        return text + "\n" + message;
    }

    public static string Format(ReviewOutcome outcome)
        => Format(outcome, outcome.NextIntervalDays);

    private static string Format(ReviewOutcome outcome, double nextIntervalDays)
    {
        var stability = outcome.StateAfter.Stability;
        var stabilityText = stability >= 1
            ? $"{stability:0.#} 天"
            : $"{stability * 24:0.#} 小时";
        var interval = nextIntervalDays < 1
            ? $"{Math.Max(1, Math.Round(nextIntervalDays * 1440)):0} 分钟" : $"{Math.Max(1, Math.Round(nextIntervalDays)):0} 天";
        var recall = outcome.IsFirstReview
            ? "首次作答"
            : $"答前预测回忆率 {outcome.RetrievabilityBefore * 100:0}%";

        return $"{recall}，记忆稳定度 {stabilityText}，建议 {interval}后复习";
    }
}
