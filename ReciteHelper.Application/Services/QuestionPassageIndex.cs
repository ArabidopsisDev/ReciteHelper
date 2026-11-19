using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;
using ReciteHelper.Core.ValueObjects;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ReciteHelper.Application.Services;

internal sealed partial class QuestionPassageIndex
{
    private static readonly ConditionalWeakTable<ReviewMaterial, QuestionPassageIndex> Materials = new();
    private static readonly ConditionalWeakTable<FileVectorStore, QuestionPassageIndex> Stores = new();
    private static readonly HashSet<string> CommonTerms = ["说明", "概述", "简述", "比较", "主要", "特征", "典型", "根据", "哪些", "什么", "如何"];

    private readonly Passage[] _passages;
    private sealed record Passage(string Title, string Text, string Normalized, HashSet<string> Terms);

    private QuestionPassageIndex(IEnumerable<(string Title, string Text)> content)
    {
        _passages = content.Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .SelectMany(item => Passages(item.Text).Select(text =>
                new Passage(item.Title, text, SourceEvidenceOrder.Normalize(text), Terms(text)))).ToArray();
    }

    public static IReadOnlyList<KnowledgeBaseMatch> Search(Question question, IEnumerable<ReviewMaterial> materials,
        FileVectorStore? store, CancellationToken cancellationToken)
    {
        var passages = materials.SelectMany(material => Materials.GetValue(material,
            file => new QuestionPassageIndex([(file.Name, file.Content)]))._passages).ToList();
        if (store is not null)
            passages.AddRange(Stores.GetValue(store, value => new QuestionPassageIndex(value.Entries.Select(entry =>
                (entry.Semantics.Summary?.Trim() is { Length: > 0 } title ? title : "项目资料", entry.Text))))._passages);

        var quotes = question.SourceExcerpts.Where(text => !string.IsNullOrWhiteSpace(text)).Distinct().ToArray();
        var topic = Terms(question.Text ?? string.Empty);
        var required = Terms(question.GetCorrectAnswerText());
        var query = topic.Union(required, StringComparer.OrdinalIgnoreCase).ToArray();
        var frequency = query.ToDictionary(term => term, term => passages.Count(passage => passage.Terms.Contains(term)));
        var scores = new List<KnowledgeBaseMatch>();
        foreach (var passage in passages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchingTerms = query.Where(passage.Terms.Contains).ToArray();
            if (matchingTerms.Length == 0) continue;
            var relevance = matchingTerms.Sum(term =>
                Math.Log(1 + (passages.Count + 0.5) / (frequency[term] + 0.5)) * (topic.Contains(term) ? 1.8 : 1))
                / Math.Sqrt(Math.Max(60, passage.Terms.Count));
            if (matchingTerms.Length == 1 && matchingTerms[0].Length == 2) continue;
            scores.Add(new KnowledgeBaseMatch(passage.Title, passage.Text, (float)relevance)
                { Highlights = matchingTerms.OrderByDescending(term => term.Length).ThenBy(term => frequency[term]).Take(6).ToArray() });
        }

        var evidence = new List<KnowledgeBaseMatch>();
        foreach (var quote in quotes)
        {
            var normalized = SourceEvidenceOrder.Normalize(quote);
            if (normalized.Length == 0) continue;
            var passage = passages.FirstOrDefault(item => item.Normalized.Contains(normalized, StringComparison.Ordinal));
            evidence.Add(passage is null
                ? new KnowledgeBaseMatch("出题资料摘录", quote, 1) { Highlights = [quote] }
                : new KnowledgeBaseMatch(passage.Title, passage.Text, 1) { Highlights = [quote] });
        }
        return Merge(evidence.Concat(scores.OrderByDescending(match => match.Score)));
    }

    public static IReadOnlyList<KnowledgeBaseMatch> Merge(IEnumerable<KnowledgeBaseMatch> matches)
    {
        var result = new List<(KnowledgeBaseMatch Match, string Text)>();
        foreach (var match in matches)
        {
            var normalized = SourceEvidenceOrder.Normalize(match.Content);
            if (normalized.Length == 0 || result.Any(entry =>
                    entry.Text.Contains(normalized, StringComparison.Ordinal) || normalized.Contains(entry.Text, StringComparison.Ordinal) ||
                    entry.Match.Title == match.Title && Overlaps(entry.Text, normalized))) continue;
            result.Add((match, normalized));
            if (result.Count == 5) break;
        }
        return result.Select(entry => entry.Match).ToArray();
    }

    private static bool Overlaps(string left, string right)
    {
        const int comparisonLength = 90;
        return left.Length >= 3 * comparisonLength && right.Length >= 3 * comparisonLength &&
            (left.Contains(right[..comparisonLength], StringComparison.Ordinal) && left.Contains(right[comparisonLength..(2 * comparisonLength)], StringComparison.Ordinal)
             || right.Contains(left[..comparisonLength], StringComparison.Ordinal) && right.Contains(left[comparisonLength..(2 * comparisonLength)], StringComparison.Ordinal));
    }

    private static IEnumerable<string> Passages(string input)
    {
        const int width = 760;
        const int overlap = 160;
        var text = input.Trim();
        for (var start = 0; start < text.Length;)
        {
            var length = Math.Min(width, text.Length - start);
            if (start + length < text.Length)
            {
                var end = text.LastIndexOfAny(['。', '！', '？', '\n'], start + length - 1, length / 3);
                if (end >= start) length = end - start + 1;
            }
            yield return text.Substring(start, length).Trim();
            if (start + length >= text.Length) break;
            start += length - overlap;
        }
    }

    private static HashSet<string> Terms(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Words().Matches(text.Normalize(NormalizationForm.FormKC)))
        {
            var word = match.Value;
            if (word[0] is >= '\u3400' and <= '\u9FFF')
            {
                for (var index = 0; index < word.Length - 1; index++)
                {
                    var pair = word.Substring(index, 2);
                    if (!CommonTerms.Contains(pair)) terms.Add(pair);
                    if (index + 3 <= word.Length && !CommonTerms.Contains(pair) && !CommonTerms.Contains(word.Substring(index + 1, 2)))
                        terms.Add(word.Substring(index, 3));
                }
            }
            else if (word.Length >= 2)
                terms.Add(word);
        }
        return terms;
    }

    [GeneratedRegex(@"[\u3400-\u9FFF]+|[a-zA-Z0-9]+")]
    private static partial Regex Words();
}
