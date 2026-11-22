using ReciteHelper.SharedKernel;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Entities;

/// <summary>
/// One answer event.  Besides the fields consumed by the scheduler (<see cref="Time"/>,
/// <see cref="Grade"/>), behavioural features observed while answering are recorded for
/// offline analysis; they do not influence scheduling.
/// </summary>
public class ReviewTag : Entity
{
    /// <summary>Answer similarity to the reference answer, as reported by the judge (0..1 or 0..100 in legacy files).</summary>
    [JsonPropertyName("similarity")]
    public double Similarity { get; set; }

    /// <summary>Observed score on a 0..1 scale before mastery credit is applied.</summary>
    [JsonPropertyName("score_ratio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ScoreRatio { get; set; }

    /// <summary>Typing rate relative to the configured standard rate (legacy feature).</summary>
    [JsonPropertyName("rate")]
    public double Rate { get; set; }

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    /// <summary>
    /// SM-2 style quality.  Earlier versions stored a model prediction here; current versions
    /// store 4 for a correct answer and 1 for a wrong one so that older builds still parse the file.
    /// </summary>
    [JsonPropertyName("q_value")]
    public int QValue { get; set; }

    /// <summary>FSRS grade (1 Again … 4 Easy); 0 in files written before the FSRS scheduler.</summary>
    [JsonPropertyName("grade")]
    public int Grade { get; set; }

    [JsonPropertyName("is_correct")]
    public bool? IsCorrect { get; set; }

    [JsonPropertyName("verdict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Verdict { get; set; }

    [JsonPropertyName("ef_value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? EFValue { get; set; }

    [JsonPropertyName("next_interval_days")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NextIntervalDays { get; set; }

    /// <summary>Whole study days since the previous review of this question.</summary>
    [JsonPropertyName("elapsed_days")]
    public int? ElapsedDays { get; set; }

    /// <summary>Recall probability predicted by the model just before this answer.</summary>
    [JsonPropertyName("retrievability")]
    public double? Retrievability { get; set; }

    /// <summary>Seconds between showing the question and submitting the answer.</summary>
    [JsonPropertyName("response_seconds")]
    public double? ResponseSeconds { get; set; }

    [JsonPropertyName("exam_duration_seconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ExamDurationSeconds { get; set; }

    [JsonPropertyName("answer_length")]
    public int? AnswerLength { get; set; }

    /// <summary>Memory state after this review.</summary>
    [JsonPropertyName("stability")]
    public double? Stability { get; set; }

    [JsonPropertyName("difficulty")]
    public double? Difficulty { get; set; }

    /// <summary>Rubric shares sum to one for components observed in a full-question attempt.</summary>
    [JsonPropertyName("optimizer_weight")]
    public double OptimizerWeight { get; set; } = 1.0;
}
