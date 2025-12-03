namespace ReciteHelper.Model;

/// <summary>
/// Represents the configuration settings for an exam, including course information, timing, question details, and
/// chapter weighting.
/// </summary>
/// <remarks>Use this class to specify the parameters required to generate or administer an exam. All properties
/// should be set before the exam begins. The chapter weights determine the relative importance of each chapter when
/// distributing questions or calculating scores.</remarks>
public class ExamSettings
{
    public string CourseNumber { get; set; }
    public int ExamTimeMinutes { get; set; }
    public int QuestionCount { get; set; }
    public int ScorePerQuestion { get; set; }
    public Dictionary<string, double> ChapterWeights { get; set; }
}
