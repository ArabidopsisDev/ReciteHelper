using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.Services;
using ReciteHelper.Core.ValueObjects;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReciteHelper.Application.Services;

public sealed record QuickReviewPrompt(Question Topic, Question Source, int Priority, string Text,
    IReadOnlyList<string> Choices, IReadOnlyList<string> AcceptedAnswers)
{
    public bool Accepts(string answer) => AcceptedAnswers.Any(value => Normalize(value) == Normalize(answer));

    public bool IsAvailable => IsAvailableFor(Topic, Source);

    internal static bool IsAvailableFor(Question topic, Question source) => !topic.IsArchived &&
        (ReferenceEquals(topic, source) ? topic.ReviewFacets.Count == 0
            : ReviewQuestionCatalog.ActiveFacets(topic).Any(facet => ReferenceEquals(facet.Question, source)));

    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Trim().TrimEnd('。', '.').Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
}

public sealed partial class QuickReviewService(IReviewScheduler scheduler, IAiChatService chat)
{
    public async IAsyncEnumerable<QuickReviewPrompt> PrepareAsync(Project project, IReadOnlySet<Guid> exclude)
    {
        var now = DateTime.Now;
        var candidates = ReviewQuestionCatalog.Roots(project).Where(root => !root.IsArchived && !exclude.Contains(root.ReviewIdentity))
            .Select(root => new
            {
                Root = root,
                Weakest = (root.ReviewFacets.Count == 0 ? Enumerable.Repeat(root, 1)
                        : ReviewQuestionCatalog.ActiveFacets(root).Select(facet => facet.Question))
                    .Where(source => (ReviewQuestionCatalog.HasAnswerRecord(source) || source.ExamHistory is { Count: > 0 }) &&
                        !string.IsNullOrWhiteSpace(source.Text) && !string.IsNullOrWhiteSpace(source.GetCorrectAnswerText()))
                    .Select(source => new { Source = source, Mastery = EstimateMastery(project, source, now) })
                    .MinBy(item => item.Mastery)
            })
            .Where(item => item.Weakest is not null)
            .OrderBy(item => item.Weakest!.Mastery).Take(3)
            .Select((item, priority) => new Candidate(item.Root, item.Weakest!.Source, priority)).ToArray();

        var needsModel = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            if (!QuickReviewPrompt.IsAvailableFor(candidate.Topic, candidate.Source)) continue;
            if (MakeLocalPrompt(candidate) is { } prompt) yield return prompt;
            else needsModel.Add(candidate);
        }
        needsModel.RemoveAll(candidate => !QuickReviewPrompt.IsAvailableFor(candidate.Topic, candidate.Source));
        if (needsModel.Count == 0) yield break;

        foreach (var prompt in await MakeModelPromptsAsync(needsModel))
            if (prompt.IsAvailable) yield return prompt;
    }

    private double EstimateMastery(Project project, Question question, DateTime now)
    {
        var assessment = question.LastAiAssessment;
        var latest = question.ReviewTag.Concat(question.ExamHistory ?? []).MaxBy(tag => tag.Time);
        var quality = latest?.ScoreRatio ?? assessment?.RawScoreRatio ?? assessment?.ScoreRatio
            ?? (latest is { Similarity: > 0 } ? latest.Similarity : (latest?.IsCorrect ?? question.Status) is true ? 1 : 0);
        if (quality > 1) quality /= 100;
        return Math.Clamp(quality, 0, 1) * (scheduler.PredictRecall(project, question, now) ?? 1);
    }

    private static QuickReviewPrompt? MakeLocalPrompt(Candidate candidate)
    {
        var question = candidate.Source;
        if (question.IsSingleChoice && question.GetCorrectOptionIds() is [var answerId])
        {
            var answer = question.Options.FirstOrDefault(option => QuestionOption.NormalizeId(option.Id) == answerId);
            var choices = question.Options.Select(option => option.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (answer is not null && choices.Length is >= 2 and <= 5 && choices.All(ShortAnswer))
                return Make(candidate, question.Text!, choices, [answer.Text.Trim()]);
        }
        if (question.IsTrueFalse && question.GetCorrectAnswerText() is "正确" or "错误")
            return Make(candidate, question.Text!, ["正确", "错误"], [question.GetCorrectAnswerText()]);
        if (question.IsFillBlank && question.BlankCount == 1 && question.GetCorrectAnswers() is [var correct])
        {
            var aliases = correct.Split(['/', '／', '、', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var text = FillBlankTextNormalizer.NormalizeForDisplay(question.Text!, question.GetCorrectAnswers());
            if (BlankMarkers().Count(text) == 1 && aliases.Length > 0 && aliases.All(ShortAnswer))
                return Make(candidate, text, [], aliases);
        }
        return null;
    }

    private async Task<IReadOnlyList<QuickReviewPrompt>> MakeModelPromptsAsync(IReadOnlyList<Candidate> candidates)
    {
        var data = JsonSerializer.Serialize(candidates.Select(item => new
        {
            source = item.Priority,
            question = item.Source.Text,
            reference = item.Source.GetCorrectAnswerText(),
            missing = item.Source.LastAiAssessment?.MissingPoints.Take(3),
            corrections = item.Source.LastAiAssessment?.Corrections.Take(3)
        }), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        try
        {
            var response = await chat.RunAsync(string.Empty, data, """
                将每个 source 给出一道十秒左右能答完的复习题，优先从已知漏项或订正中取一条。
                依据 reference 与 corrections，每题只考一个事实，提供题干与正确答案，答案不要出现在题干里。
                题型为三选一或一个空。三选一的 choices 含三个不同且简短的选项，answer 与正确选项逐字一致。
                填空题只出现一个 ________，choices 为 []，answer 是一个简短词语，aliases 可包含等价写法。
                回传 JSON 对象 {"items":[{"source":0,"question":"题干","choices":[],"answer":"正确答案","aliases":[]}]}。
                原样返回 source 编号，没有合适事实的可以略过。不要返回 markdown 或解释。
                """);
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end < start) return [];
            var items = JsonSerializer.Deserialize<ModelResponse>(response[start..(end + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Items ?? [];
            return candidates.Select(candidate => items.FirstOrDefault(item => item.Source == candidate.Priority) is { } item
                    ? Validate(candidate, item) : null)
                .OfType<QuickReviewPrompt>().ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Quick review preparation failed ({ex.GetType().Name}).");
            return [];
        }
    }

    private static QuickReviewPrompt? Validate(Candidate candidate, ModelPrompt item)
    {
        var text = item.Question?.Trim();
        var answer = item.Answer?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 180 || !ShortAnswer(answer)) return null;
        var choices = item.Choices?.Where(ShortAnswer).Select(choice => choice.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var markers = BlankMarkers().Count(text);
        if (choices.Length is >= 2 and <= 4 && markers == 0 && choices.Contains(answer!))
            return Make(candidate, text, choices, [answer!]);
        if (choices.Length == 0 && markers == 1)
        {
            var aliases = (item.Aliases ?? []).Where(ShortAnswer).Select(alias => alias.Trim()).Take(4);
            return Make(candidate, text, [], [answer!, .. aliases]);
        }
        return null;
    }

    private static QuickReviewPrompt Make(Candidate candidate, string text, string[] choices, string[] answers)
    {
        Random.Shared.Shuffle(choices);
        return new(candidate.Topic, candidate.Source, candidate.Priority, text, choices, answers);
    }

    private static bool ShortAnswer(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 44 && !text.Contains('\n');

    [GeneratedRegex(@"_{2,}|[（(]\s*[）)]")]
    private static partial Regex BlankMarkers();

    private sealed record Candidate(Question Topic, Question Source, int Priority);
    private sealed record ModelResponse(ModelPrompt[]? Items);
    private sealed record ModelPrompt(int Source, string? Question, string[]? Choices, string? Answer, string[]? Aliases);
}
