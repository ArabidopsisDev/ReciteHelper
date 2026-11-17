using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Entities;

/// <summary>A stable rubric component and its independently scheduled question.</summary>
public sealed class ReviewFacet
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("weight")]
    public double Weight { get; set; }
    [JsonPropertyName("weight_reason")]
    public string WeightReason { get; set; } = string.Empty;
    [JsonPropertyName("last_score_ratio")]
    public double LastScoreRatio { get; set; }
    [JsonPropertyName("needs_review")]
    public bool NeedsReview { get; set; }
    [JsonPropertyName("review_available_at")]
    public DateTime? ReviewAvailableAt { get; set; }
    [JsonPropertyName("question")]
    public Question Question { get; set; } = new();
}
