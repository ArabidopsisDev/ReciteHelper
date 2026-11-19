using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ReciteHelper.Application.Services;

internal sealed partial class RuleReviewMaterialIndex
{
    // Reuse passages across questions while the project's material objects are open.
    private static readonly ConditionalWeakTable<ReviewMaterial, RuleReviewMaterialIndex> Indexes = new();

    private readonly Passage[] _passages;
    private sealed record Passage(string Title, string Text, string Normalized, HashSet<string> Terms);

    private RuleReviewMaterialIndex(ReviewMaterial material)
    {
        const int stride = 420;
        const int width = 600;
        var content = material.Content;
        _passages = Enumerable.Range(0, (content.Length + stride - 1) / stride)
            .Select(index => content.Substring(index * stride, Math.Min(width, content.Length - index * stride)).Trim())
            .Where(text => text.Length > 0)
            .Select(text => new Passage(material.Name, text, SourceEvidenceOrder.Normalize(text), Terms(text)))
            .ToArray();
    }

    public static IReadOnlyList<KnowledgeBaseMatch> Search(IEnumerable<ReviewMaterial> materials,
        Question question, CancellationToken cancellationToken)
    {
        var evidence = question.SourceExcerpts.Select(SourceEvidenceOrder.Normalize).Where(quote => quote.Length > 0).ToArray();
        var query = Terms($"{question.Text}\n{question.GetCorrectAnswerText()}\n{string.Join('\n', question.SourceExcerpts)}");
        var matches = new List<KnowledgeBaseMatch>();

        foreach (var material in materials)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Indexes.GetValue(material, static file => new RuleReviewMaterialIndex(file));
            foreach (var passage in index._passages)
            {
                var overlap = query.Count(passage.Terms.Contains);
                var hasQuote = evidence.Any(quote => passage.Normalized.Contains(quote, StringComparison.Ordinal));
                if (overlap == 0 && !hasQuote) continue;
                var relevance = (double)overlap / Math.Max(1, query.Count);
                matches.Add(new KnowledgeBaseMatch(passage.Title, passage.Text, (float)(hasQuote ? 0.8 + 0.2 * relevance : 0.8 * relevance)));
            }
        }

        return matches.Count > 0
            ? matches.OrderByDescending(match => match.Score).Take(3).ToArray()
            : question.SourceExcerpts.Where(quote => !string.IsNullOrWhiteSpace(quote)).Distinct().Take(3)
                .Select(quote => new KnowledgeBaseMatch("出题资料摘录", quote, 1)).ToArray();
    }

    private static HashSet<string> Terms(string value)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Words().Matches(value))
        {
            var word = match.Value;
            if (word[0] is >= '\u3400' and <= '\u9FFF')
            {
                for (var index = 0; index < word.Length - 1; index++)
                    terms.Add(word.Substring(index, 2));
            }
            else
                terms.Add(word);
        }
        return terms;
    }

    [GeneratedRegex(@"[\u3400-\u9FFF]+|[a-zA-Z0-9]+")]
    private static partial Regex Words();
}
