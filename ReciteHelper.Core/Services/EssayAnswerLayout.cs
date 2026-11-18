using System.Text.RegularExpressions;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Services;

/// <summary>One answer field per natural subquestion, with a readable persisted answer.</summary>
public static class EssayAnswerLayout
{
    public static IReadOnlyList<QuestionPart> Parts(Question question)
    {
        if (question.IsReviewFacet) return [];
        IReadOnlyList<QuestionPart> saved = question.AnswerParts.Count > 0 ? question.AnswerParts
            : question.ReviewFacets.Count > 1
                ? question.ReviewFacets.Select(f => new QuestionPart { Key = f.Key, Prompt = f.Question.Text ?? f.Name,
                    ReferenceAnswer = f.Question.CorrectAnswer ?? string.Empty, Weight = f.Weight,
                    AnswerNature = f.Question.AnswerNature }).ToList()
                : question.AnswerParts;
        if (!question.IsWritten) return saved;

        // Earlier project files have only a stem. Recognise explicit numbered questions and common course headings.
        var text = question.Text ?? string.Empty;
        var numbered = Regex.Matches(text, @"(?:（[一二三四五六七八九十\d]+）|\([1-9]\)|(?<!\d)[1-9][、.])\s*([^（]+?)(?=(?:（[一二三四五六七八九十\d]+）|\([1-9]\)|(?<!\d)[1-9][、.])|$)");
        var prompts = numbered.Count > 1 ? numbered.Select(m => m.Groups[1].Value.Trim()).ToList() : [];
        if (prompts.Count == 0)
        {
            var sentences = Regex.Matches(text, @"[^？?]+[？?]").Select(m => m.Value.Trim()).ToList();
            if (sentences.Count > 1) prompts = sentences;
        }
        if (prompts.Count == 0)
        {
            const string headings = "病症|症状|病原|病害循环|发病循环|病害特征|传播途径|防治措施|发病条件|基本特点|主要操作|操作位置|基本原理|作用机制|应用场景|优点|缺点";
            var aspects = Regex.Match(text, @"^(.*?的)(" + headings + @")([、与和及].+?)[。？?]?$", RegexOptions.Singleline);
            if (aspects.Success)
            {
                var names = Regex.Split(aspects.Groups[2].Value + aspects.Groups[3].Value.TrimEnd('。', '？', '?'), "[、与和及]");
                if (names.All(n => Regex.IsMatch(n.Trim(), "^(" + headings + ")$")))
                    prompts = names.Select(n => aspects.Groups[1].Value + n.Trim() + "。").ToList();
            }
        }
        // Top-level tasks define the answer fields; finer scoring points do not.
        return prompts.Count > 1 && (saved.Count <= 1 || prompts.Count < saved.Count)
            ? prompts.Select((p, i) => new QuestionPart { Key = $"part{i + 1}", Prompt = p,
                ReferenceAnswer = question.GetCorrectAnswerText(), Weight = 1d / prompts.Count }).ToList()
            : saved;
    }

    public static bool NeedsFacetRegrouping(Question question)
    {
        if (question.ReviewFacets.Count == 0 || question.IsReviewFacet) return false;
        var parts = Parts(question);
        return parts.Count > 0 && (parts.Count != question.ReviewFacets.Count ||
            parts.Any(part => !question.ReviewFacets.Any(f => string.Equals(f.Key, part.Key, StringComparison.OrdinalIgnoreCase))));
    }

    public static string Join(IReadOnlyList<string> answers)
        => answers.All(string.IsNullOrWhiteSpace) ? string.Empty : string.Join("\n\n", answers.Select((a, i) => $"【第{i + 1}问】\n{a.Trim()}"));

    public static IReadOnlyList<string> Split(string? answer, int count)
    {
        var text = answer ?? string.Empty;
        return Enumerable.Range(0, count).Select(i =>
        {
            var (start, length) = PartRange(text, i);
            return text.Substring(start, length);
        }).ToList();
    }

    private static (int Start, int Length) PartRange(string text, int index)
    {
        if (!text.StartsWith("【第1问】", StringComparison.Ordinal)) return index == 0 ? (0, text.Length) : (0, 0);
        var label = $"【第{index + 1}问】";
        var start = text.IndexOf(label, StringComparison.Ordinal);
        if (start < 0) return (0, 0);
        start += label.Length;
        if (start < text.Length && text[start] == '\n') start++;
        var end = text.IndexOf($"\n\n【第{index + 2}问】", start, StringComparison.Ordinal);
        return (start, (end < 0 ? text.Length : end) - start);
    }

    public static List<AnswerAnnotation> MergeAnnotations(string answer, IReadOnlyList<AiAnswerAssessment> parts)
    {
        var marks = new List<AnswerAnnotation>();
        var cursor = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var (start, length) = PartRange(answer, i);
            if (start > cursor) marks.Add(new() { Text = answer[cursor..start], Status = "correct" });
            marks.AddRange(parts[i].Annotations);
            cursor = Math.Max(cursor, start + length);
        }
        if (cursor < answer.Length) marks.Add(new() { Text = answer[cursor..], Status = "correct" });
        return marks;
    }

    public static AiAnswerAssessment ForPart(AiAnswerAssessment whole, int index, int count)
    {
        var answers = Split(whole.UserAnswer, count);
        var answer = answers[index];
        var (start, _) = PartRange(whole.UserAnswer, index);
        var cursor = 0;
        var marks = new List<AnswerAnnotation>();
        foreach (var mark in whole.Annotations)
        {
            var from = Math.Max(start, cursor);
            var to = Math.Min(start + answer.Length, cursor + mark.Text.Length);
            if (to > from) marks.Add(new() { Text = mark.Text[(from - cursor)..(to - cursor)], Status = mark.Status, Comment = mark.Comment });
            cursor += mark.Text.Length;
        }
        var point = whole.Facets.ElementAtOrDefault(index);
        return new() { UserAnswer = answer, ScoreRatio = point?.ScoreRatio ?? whole.ScoreRatio,
            RawScoreRatio = point?.RawScoreRatio ?? whole.RawScoreRatio, IsCorrect = point?.IsCorrect ?? whole.IsCorrect,
            AnswerNature = point?.AnswerNature ?? whole.AnswerNature,
            HasFallacy = point?.HasFallacy ?? whole.HasFallacy, FallacyReason = point?.FallacyReason ?? whole.FallacyReason,
            Explanation = point?.Explanation ?? whole.Explanation, Annotations = marks,
            MissingPoints = point is { IsCorrect: false } ? [point.ReferenceAnswer] : [] };
    }
}
