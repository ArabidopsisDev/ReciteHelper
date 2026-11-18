namespace ReciteHelper.Core.DTOs;

public class ReviewOptions
{
    public int TotalCount { get; set; } = 20;

    /// <summary>Retained for callers that saved this setting before smart review excluded new questions.</summary>
    public double NewQuestionRatio { get; set; } = 0.3;

    /// <summary>Serve the questions with the lowest predicted recall first.</summary>
    public bool PrioritizeWeakPoints { get; set; } = true;
}
