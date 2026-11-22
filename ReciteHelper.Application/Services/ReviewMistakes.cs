using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.Services;
using System.Text.RegularExpressions;

namespace ReciteHelper.Application.Services;

public static partial class ReviewMistakes
{
    public static ReviewMistake? Record(Project project, Question question, DateTime time, bool correct,
        AiAnswerAssessment? assessment, string userAnswer, bool isExam = false, IReadOnlyList<int>? missedBlanks = null)
    {
        if (!project.IncludesMistakeAt(time)) return null;
        var entry = Create(question, time, correct, assessment, userAnswer, isExam, missedBlanks);
        if (entry is not null)
        {
            project.ReviewMistakes.Add(entry);
            project.NotifyReviewMistakesChanged();
        }
        return entry;
    }

    public static async Task ClearAsync(Project project, IProjectFileService files)
    {
        var previousEntries = project.ReviewMistakes;
        var previousCutoff = project.ReviewMistakesClearedAt;
        project.ReviewMistakes = [];
        project.ReviewMistakesClearedAt = DateTime.UtcNow;
        try
        {
            await files.SaveProjectAsync(project);
        }
        catch
        {
            project.ReviewMistakes = [.. previousEntries, .. project.ReviewMistakes];
            project.ReviewMistakesClearedAt = previousCutoff;
            project.NotifyReviewMistakesChanged();
            throw;
        }
        project.NotifyReviewMistakesChanged();
    }

    public static IReadOnlyList<ReviewMistake> Read(Project project)
    {
        var saved = project.ReviewMistakes.Where(entry => project.IncludesMistakeAt(entry.Time)).ToList();
        var existing = saved.Select((entry, index) => (Entry: entry, Index: index)).Where(item => !item.Entry.IsExam)
            .ToLookup(item => (item.Entry.QuestionId, item.Entry.Time), item => item.Index);
        foreach (var question in ReviewQuestionCatalog.Roots(project).SelectMany(WithFacets))
        {
            if (question.ReviewTag.MaxBy(tag => tag.Time) is not { } tag || !project.IncludesMistakeAt(tag.Time)) continue;
            var assessment = question.LastAiAssessment;
            var indices = existing[(question.ReviewIdentity, tag.Time)];
            if (indices.Any())
            {
                var answer = question.SubmittedAnswer ?? assessment?.UserAnswer;
                if (!question.AnswerCleared && !string.IsNullOrWhiteSpace(answer))
                    foreach (var index in indices)
                        if (saved[index].Answer is null)
                            saved[index] = saved[index] with { Answer = CaptureAnswer(question, assessment, answer, tag.IsCorrect is true, null) };
                continue;
            }
            if (assessment is not null &&
                Create(question, tag.Time, assessment.IsCorrect, assessment, assessment.UserAnswer, isExam: false) is { } entry &&
                (entry.Corrections.Any(correction => correction.Action != "复核") || entry.Answer!.Any(HasMark)))
                saved.Add(entry);
        }
        return saved.OrderByDescending(entry => entry.Time).ToArray();
    }

    private static IEnumerable<Question> WithFacets(Question question)
        => new[] { question }.Concat(question.ReviewFacets.Concat(question.ArchivedReviewFacets).Select(facet => facet.Question));

    public static ReviewMistake? Create(Question question, DateTime time, bool correct, AiAnswerAssessment? assessment,
        string userAnswer, bool isExam, IReadOnlyList<int>? missedBlanks = null)
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
            var reference = question.GetCorrectAnswers();
            foreach (var index in missedBlanks ?? [])
                Add(corrections, "订正", $"第 {index + 1} 空填写「{reference[index]}」。");
            if (question.IsSingleChoice || question.IsTrueFalse) Add(corrections, "订正", question.GetCorrectAnswerText());
        }

        if (!correct && corrections.Count == 0)
            Add(corrections, "复核", "结合资料与解析核对本题的扣分处。");

        var answer = CaptureAnswer(question, assessment, userAnswer, correct, missedBlanks);
        return corrections.Count == 0 && !answer.Any(HasMark) ? null : new ReviewMistake(question.ReviewIdentity, time,
            EssayAnswerLayout.Stem(question, EssayAnswerLayout.Parts(question)), isExam, corrections) { Answer = answer };
    }

    private static bool HasMark(MistakeAnswerSpan span) => span.Status is "incorrect" or "partial";

    private static IReadOnlyList<MistakeAnswerSpan> CaptureAnswer(Question question, AiAnswerAssessment? assessment,
        string answer, bool correct, IReadOnlyList<int>? missedBlanks)
    {
        if (string.IsNullOrWhiteSpace(answer)) return [];
        if (assessment is not null && string.Concat(assessment.Annotations.Select(mark => mark.Text)) == answer)
            return assessment.Annotations.Select(mark => new MistakeAnswerSpan(mark.Text, mark.Status)).ToArray();
        if (question.IsFillBlank)
        {
            var blanks = Question.SplitBlankAnswers(answer);
            var spans = new List<MistakeAnswerSpan>();
            for (var index = 0; index < blanks.Count; index++)
            {
                spans.Add(new MistakeAnswerSpan($"{(index == 0 ? "" : "  ")}第 {index + 1} 空 "));
                var filled = blanks[index].Length > 0;
                spans.Add(filled ? new MistakeAnswerSpan(blanks[index], missedBlanks?.Contains(index) == true ? "incorrect" : "correct")
                    : new MistakeAnswerSpan("留空", "partial"));
            }
            return spans;
        }
        var incorrect = !correct && (question.IsSingleChoice || question.IsTrueFalse);
        var text = question.IsSingleChoice && answer.Trim() == Question.ExtractOptionId(answer)
            ? question.GetOptionDisplayText(answer.Trim()) : answer;
        return [new MistakeAnswerSpan(text, incorrect ? "incorrect" : "correct")];
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
