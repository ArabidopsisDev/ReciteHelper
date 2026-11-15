using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.DTOs;

public class AnswerResult
{
    public AiAnswerAssessment? AiAssessment { get; set; }
    public bool IsCorrect { get; set; }

    /// <summary>Similarity between the submitted answer and the reference answer (0..1).</summary>
    public double Similarity { get; set; }

    /// <summary>Typing rate relative to the configured standard rate.</summary>
    public double RRelative { get; set; }

    /// <summary>The review tag that was appended to the question.</summary>
    public required ReviewTag ReviewTag { get; set; }

    /// <summary>Scheduler outcome for this answer.</summary>
    public required ReviewOutcome Outcome { get; set; }
}
