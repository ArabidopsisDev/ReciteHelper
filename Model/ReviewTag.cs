namespace ReciteHelper.Model;

/// <summary>
/// Represents a tag associated with a review, including the review time and correctness status.
/// </summary>
public class ReviewTag
{
    public DateTime ReviewTime { get; set; }
    public bool IsCorrect { get; set; }
}
