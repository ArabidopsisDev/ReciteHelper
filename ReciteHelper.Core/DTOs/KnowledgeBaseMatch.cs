namespace ReciteHelper.Core.DTOs;

public sealed record KnowledgeBaseMatch(
    string Title,
    string Content,
    float Score)
{
    public IReadOnlyList<string> Highlights { get; init; } = [];
}
