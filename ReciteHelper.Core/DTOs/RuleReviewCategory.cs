using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.DTOs;

public sealed class RuleReviewCategory
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}

public sealed record RuleReviewPreview(string Name, string Prompt, IReadOnlyList<string> QuestionPlan, IReadOnlyList<Question> Questions)
{
    public int QuestionCount => QuestionPlan.Count;
}
