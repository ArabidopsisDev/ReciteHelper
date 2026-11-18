using ReciteHelper.Core.Enums;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.DTOs;

public sealed class AiAnswerAssessment
{
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public double ScoreRatio { get; set; }
    public double? RawScoreRatio { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AnswerNature AnswerNature { get; set; }
    public bool HasFallacy { get; set; }
    public string FallacyReason { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<AnswerAnnotation> Annotations { get; set; } = [];
    public List<string> MissingPoints { get; set; } = [];
    public List<FacetAssessment> Facets { get; set; } = [];
}

/// <summary>Non-overlapping rubric components, including correctly answered ones.</summary>
public sealed class FacetAssessment
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Weight { get; set; }
    public string WeightReason { get; set; } = string.Empty;
    public double ScoreRatio { get; set; }
    public double? RawScoreRatio { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AnswerNature AnswerNature { get; set; }
    public bool HasFallacy { get; set; }
    public string FallacyReason { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string Question { get; set; } = string.Empty;
    public string ReferenceAnswer { get; set; } = string.Empty;
}

public sealed class AnswerAnnotation
{
    public int? SegmentId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}
