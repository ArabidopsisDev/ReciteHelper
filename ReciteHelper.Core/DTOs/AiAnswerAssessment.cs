namespace ReciteHelper.Core.DTOs;

public sealed class AiAnswerAssessment
{
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public double ScoreRatio { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<AnswerAnnotation> Annotations { get; set; } = [];
    public List<string> MissingPoints { get; set; } = [];
}

public sealed class AnswerAnnotation
{
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}
