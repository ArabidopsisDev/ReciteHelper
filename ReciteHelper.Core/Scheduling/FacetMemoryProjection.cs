using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Scheduling;

/// <summary>
/// Expected retained rubric coverage, not the probability of a completely correct whole answer.
/// Component clocks remain independent; focused practice never resets sibling clocks.
/// </summary>
public static class FacetMemoryProjection
{
    public static double Recall(Question parent, DateTime now, SchedulerParameters parameters)
        => Aggregate(parent, facet => Recall(facet.Question.Memory, now, parameters));

    public static double Mastery(Question parent, DateTime now, SchedulerParameters parameters)
        => Aggregate(parent, facet => Math.Clamp(facet.LastScoreRatio, 0, 1) * Recall(facet.Question.Memory, now, parameters));

    public static double Recall(MemoryRecord? memory, DateTime now, SchedulerParameters parameters)
        => memory is null || !memory.State.IsValid ? 0 : FsrsAlgorithm.Retrievability(memory.Stability,
            ReviewCalendar.ElapsedDays(memory.LastReview, now), parameters);

    /// <summary>Invert the weighted component curves with their original elapsed times intact.</summary>
    public static double IntervalForRetention(Question parent, DateTime now, double desiredRetention, SchedulerParameters parameters)
    {
        if (desiredRetention is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(desiredRetention));
        if (parent.ReviewFacets.Count == 0 || Recall(parent, now, parameters) <= desiredRetention) return 0;
        var upper = parent.ReviewFacets.Max(f => FsrsAlgorithm.IntervalForRetention(
            f.Question.Memory?.Stability ?? FsrsAlgorithm.MinStability, desiredRetention, parameters));
        var lower = 0d;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var middle = (lower + upper) / 2;
            var recall = Aggregate(parent, f => f.Question.Memory is { } memory
                ? FsrsAlgorithm.Retrievability(memory.Stability, ReviewCalendar.ElapsedDays(memory.LastReview, now) + middle, parameters)
                : 0);
            if (recall > desiredRetention) lower = middle;
            else upper = middle;
        }
        return (lower + upper) / 2;
    }

    private static double Aggregate(Question parent, Func<ReviewFacet, double> contribution)
    {
        var facets = parent.ReviewFacets.Where(f => double.IsFinite(f.Weight) && f.Weight > 0).ToList();
        var total = facets.Sum(f => f.Weight);
        return total <= 0 ? 0 : Math.Clamp(facets.Sum(f => f.Weight / total * contribution(f)), 0, 1);
    }
}
