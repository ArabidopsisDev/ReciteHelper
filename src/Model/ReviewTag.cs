using System.Text.Json.Serialization;

namespace ReciteHelper.Model;

/// <summary>
/// Represents a tag associated with a review, including the review time and correctness status.
/// </summary>
public class ReviewTag
{
    [JsonPropertyName("similarity")]
    public double Similarity { get; set; }

    [JsonPropertyName("rate")]
    public double Rate {  get; set; }

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("q_value")]
    public int QValue {  get; set; }
}
