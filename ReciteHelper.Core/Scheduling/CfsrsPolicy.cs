using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Scheduling;

/// <summary>Component review policy around the unchanged FSRS-6 memory model.</summary>
public static class CfsrsPolicy
{
    public const string Name = "cFSRS";
    public static readonly TimeSpan RelearningDelay = TimeSpan.FromMinutes(10);

    public static void Observe(ReviewFacet facet, double score, bool mastered, DateTime reviewedAt)
    {
        facet.LastScoreRatio = score;
        facet.NeedsReview = !mastered;
        facet.ReviewAvailableAt = mastered ? null : reviewedAt + RelearningDelay;
    }

    public static bool IsAvailable(ReviewFacet facet, DateTime now, double? recall, double desiredRetention)
        => facet.NeedsReview ? facet.ReviewAvailableAt is null || facet.ReviewAvailableAt <= now
            : recall is null || recall < desiredRetention;

    public static double NextReviewInterval(Question parent, DateTime now, double desiredRetention, SchedulerParameters parameters)
        => parent.ReviewFacets.Any(f => f.NeedsReview)
            ? Math.Max(0, parent.ReviewFacets.Where(f => f.NeedsReview).Min(f => ((f.ReviewAvailableAt ?? now) - now).TotalDays))
            : FacetMemoryProjection.IntervalForRetention(parent, now, desiredRetention, parameters);
}
