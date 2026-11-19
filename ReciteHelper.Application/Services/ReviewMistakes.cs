using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.Services;
using System.Text.RegularExpressions;

namespace ReciteHelper.Application.Services;

public static partial class ReviewMistakes
{
    public static ReviewMistake? Record(Project project, Question question, DateTime time, bool correct,
        AiAnswerAssessment? assessment, bool isExam = false, IReadOnlyList<string>? missedBlanks = null)
    {
        var entry = Create(question, time, correct, assessment, isExam, missedBlanks);
        if (entry is not null)
        {
            project.ReviewMistakes.Add(entry);
            project.NotifyReviewMistakesChanged();
        }
        return entry;
    }

    public static IReadOnlyList<ReviewMistake> Read(Project project)
    {
        var saved = project.ReviewMistakes.ToList();
        var existing = saved.Where(entry => !entry.IsExam).Select(entry => (entry.QuestionId, entry.Time)).ToHashSet();
        foreach (var question in ReviewQuestionCatalog.Roots(project).SelectMany(WithFacets))
        {
            if (question.LastAiAssessment is not { } assessment || question.ReviewTag.MaxBy(tag => tag.Time) is not { } tag ||
                !existing.Add((question.ReviewIdentity, tag.Time))) continue;
            if (Create(question, tag.Time, assessment.IsCorrect, assessment, false, null) is { } entry &&
                entry.Corrections.Any(correction => correction.Action != "复核"))
                saved.Add(entry);
        }
        return saved.OrderByDescending(entry => entry.Time).ToArray();
    }

    private static IEnumerable<Question> WithFacets(Question question)
        => new[] { question }.Concat(question.ReviewFacets.Concat(question.ArchivedReviewFacets).Select(facet => facet.Question));

    public static ReviewMistake? Create(Question question, DateTime time, bool correct, AiAnswerAssessment? assessment,
        bool isExam, IReadOnlyList<string>? missedBlanks = null)
    {
        var corrections = new List<MistakeCorrection>();
        if (assessment is not null)
        {
            foreach (var omission in assessment.MissingPoints) Add(corrections, "补充", omission);
            foreach (var correction in assessment.Corrections) Add(corrections, "订正", correction);
            if (assessment.Corrections.Count == 0)
            {
                var count = assessment.Facets.Count;
                if (count > 1 && assessment.UserAnswer.StartsWith("【第1问】", StringComparison.Ordinal))
                {
                    for (var index = 0; index < count; index++)
                        AddLegacy(corrections, EssayAnswerLayout.ForPart(assessment, index, count), $"第 {index + 1} 问");
                }
                else AddLegacy(corrections, assessment);
            }
        }
        else if (!correct)
        {
            foreach (var blank in missedBlanks ?? []) Add(corrections, "订正", blank);
            if (question.IsSingleChoice || question.IsTrueFalse) Add(corrections, "订正", question.GetCorrectAnswerText());
        }

        if (!correct && corrections.Count == 0)
            Add(corrections, "复核", "结合资料与解析核对本题的扣分处。");

        return corrections.Count == 0 ? null : new ReviewMistake(question.ReviewIdentity, time,
            EssayAnswerLayout.Stem(question, EssayAnswerLayout.Parts(question)), isExam, corrections);
    }

    private static void AddLegacy(List<MistakeCorrection> corrections, AiAnswerAssessment assessment, string? part = null)
    {
        foreach (var mark in assessment.Annotations.Where(annotation => annotation.Status is "incorrect" or "partial"))
        {
            if (CorrectionPhrase(mark.Comment) is { } phrase)
                Add(corrections, "订正", phrase, part);
        }
        if (assessment.HasFallacy && CorrectionPhrase(assessment.FallacyReason) is { } correction)
            Add(corrections, "订正", correction, part);
    }

    private static string? CorrectionPhrase(string comment)
    {
        // Older assessments can mix a correction with remarks about correct parts of the answer.
        var match = CorrectionCue().Matches(comment).LastOrDefault();
        if (match is null) return null;
        var statement = match.Groups["text"].Value.Trim().Trim('“', '”', '「', '」', '。', '，', ',');
        return statement.Length > 0 ? statement + "。" : null;
    }

    private static void Add(List<MistakeCorrection> corrections, string action, string value, string? part = null)
    {
        var text = value.Trim();
        if (text.Length == 0) return;
        var prefix = PartPrefix().Match(text);
        if (prefix.Success)
        {
            part ??= prefix.Groups["part"].Value;
            text = text[prefix.Length..].Trim();
        }

        var key = Key(text);
        if (key.Length == 0 || corrections.Any(item => (item.Part is null || part is null || item.Part == part) &&
            (Key(item.Text).Contains(key, StringComparison.Ordinal) || key.Contains(Key(item.Text), StringComparison.Ordinal)))) return;
        corrections.Add(new(action, text, part));
    }

    private static string Key(string value)
        => string.Concat(value.Where(char.IsLetterOrDigit));

    [GeneratedRegex(@"(?:^|[，,。；;：:\s]|但|则)(?:可改为|宜改为|宜表述为|宜写为|应改为|应明确为|应表述为|应写为?|应明确|应指出|应补充|需补充|还需补充|补充|正确的是|而是)[：:\s]*(?<text>“[^”]+”|「[^」]+」|[^。！？；;\r\n]+)")]
    private static partial Regex CorrectionCue();

    [GeneratedRegex(@"^(?<part>第\s*\d+\s*问)[：:]\s*")]
    private static partial Regex PartPrefix();
}
