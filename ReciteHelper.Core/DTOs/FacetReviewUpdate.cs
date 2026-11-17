namespace ReciteHelper.Core.DTOs;

public sealed record FacetReviewUpdate(
    string ParentQuestion,
    string? FacetName,
    double? Weight,
    double MasteryBefore,
    double MasteryAfter,
    IReadOnlyList<string> WeakFacets)
{
    public double ParentNextIntervalDays { get; init; }
}
