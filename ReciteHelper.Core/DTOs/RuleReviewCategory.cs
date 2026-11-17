using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.DTOs;

public sealed class RuleReviewCategory
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public IReadOnlyList<string> ExistingQuestionTexts { get; set; } = [];
    public bool IsAppending { get; set; }
    public IReadOnlyList<ReviewMaterial> Materials { get; set; } = [];
}

public sealed record RuleReviewPreview(string Name, string Prompt, IReadOnlyList<string> QuestionPlan, IReadOnlyList<Question> Questions)
{
    public int QuestionCount => QuestionPlan.Count;
    public IReadOnlyList<string> ExistingQuestionTexts { get; init; } = [];
    public bool IsAppending { get; init; }
    public IReadOnlyList<ReviewMaterial> Materials { get; init; } = [];
    public string CoverageNote { get; init; } = string.Empty;
}
