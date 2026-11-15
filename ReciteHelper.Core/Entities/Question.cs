using ReciteHelper.SharedKernel;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Services;
using ReciteHelper.Core.ValueObjects;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Entities;

public class Question : Entity
{
    private List<QuestionOption> _options = [];
    private List<string> _correctOptionIds = [];
    private List<string> _correctAnswers = [];

    [JsonConstructor]
    public Question() { }

    [JsonPropertyName("status")]
    public bool? Status { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("type")]
    public QuestionType Type { get; set; } = QuestionType.Essay;

    [JsonPropertyName("options")]
    public List<QuestionOption> Options
    {
        get => _options;
        set => _options = value ?? [];
    }

    [JsonPropertyName("correct_option_ids")]
    public List<string> CorrectOptionIds
    {
        get => _correctOptionIds;
        set => _correctOptionIds = value ?? [];
    }

    [JsonPropertyName("correct_answers")]
    public List<string> CorrectAnswers
    {
        get => _correctAnswers;
        set => _correctAnswers = value ?? [];
    }

    /// <summary>
    /// Answer history.  The setter must stay public: System.Text.Json skips properties with a
    /// private setter, which silently dropped every history from disk in earlier versions.
    /// </summary>
    [JsonPropertyName("review_tag")]
    public List<ReviewTag> ReviewTag { get; set; } = [];

    [JsonPropertyName("correct_answer")]
    public string? CorrectAnswer { get; set; }

    [JsonPropertyName("requires_ai_grading")]
    public bool RequiresAiGrading { get; set; }

    [JsonPropertyName("last_ai_assessment")]
    public DTOs.AiAnswerAssessment? LastAiAssessment { get; set; }

    /// <summary>
    /// Legacy SM-2 easiness factor.  Retained so that project files written by earlier
    /// versions still load; scheduling no longer reads or updates it.
    /// </summary>
    [JsonPropertyName("ef_value")]
    public double EFValue { get; set; } = 2.5d;

    /// <summary>FSRS-6 memory state; null until the question has been answered once.</summary>
    [JsonPropertyName("memory")]
    public MemoryRecord? Memory { get; set; }

    private Question(bool? status, string? text, List<ReviewTag> reviewTags, string? correctAnswer, double efValue)
    {
        Status = status;
        Text = text;
        ReviewTag = reviewTags;
        CorrectAnswer = correctAnswer;
        EFValue = efValue;
    }

    [JsonIgnore]
    public bool IsSingleChoice => Type == QuestionType.SingleChoice && Options.Count > 0;

    [JsonIgnore]
    public bool IsFillBlank => Type == QuestionType.FillBlank;

    [JsonIgnore]
    public bool IsTrueFalse => Type == QuestionType.TrueFalse;

    [JsonIgnore]
    public bool IsEssay => Type == QuestionType.Essay;

    [JsonIgnore]
    public bool IsTermDefinition => Type == QuestionType.TermDefinition;

    [JsonIgnore]
    public int BlankCount => IsFillBlank
        ? GetBlankCount()
        : 0;

    [JsonIgnore]
    public int DefaultExamScore => Type switch
    {
        QuestionType.SingleChoice => 3,
        QuestionType.FillBlank => BlankCount,
        QuestionType.TrueFalse => 1,
        QuestionType.TermDefinition => 4,
        _ => 5
    };

    [JsonIgnore]
    public string TypeDisplayName => Type switch
    {
        QuestionType.SingleChoice => "选择题",
        QuestionType.FillBlank => "填空题",
        QuestionType.TrueFalse => "判断题",
        QuestionType.TermDefinition => "名词解释",
        _ => "解答题"
    };

    public string GetCorrectAnswerText()
    {
        if (IsFillBlank)
            return string.Join("；", GetCorrectAnswers().Select((answer, index) => $"{index + 1}. {answer}"));

        if (IsTrueFalse)
            return NormalizeTrueFalseAnswer(CorrectAnswer);

        if (!IsSingleChoice)
            return CorrectAnswer ?? string.Empty;

        var correctOptionId = GetCorrectOptionIds().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correctOptionId))
            return CorrectAnswer ?? string.Empty;

        return GetOptionDisplayText(correctOptionId);
    }

    private int GetBlankCount()
    {
        return FillBlankTextNormalizer.CountEffectiveMarkers(Text ?? string.Empty, GetCorrectAnswers());
    }

    public string GetOptionDisplayText(string? optionId)
    {
        var normalizedId = QuestionOption.NormalizeId(optionId);
        var option = Options.FirstOrDefault(x => QuestionOption.NormalizeId(x.Id) == normalizedId);
        return option is null ? normalizedId : option.DisplayText;
    }

    public IReadOnlyList<string> GetCorrectOptionIds()
    {
        var ids = CorrectOptionIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(QuestionOption.NormalizeId)
            .Distinct()
            .ToList();

        if (ids.Count > 0)
            return ids;

        var answerId = ExtractOptionId(CorrectAnswer);
        return string.IsNullOrWhiteSpace(answerId) ? [] : [answerId];
    }

    public bool IsCorrectChoiceAnswer(string? userAnswer)
    {
        if (!IsSingleChoice || string.IsNullOrWhiteSpace(userAnswer))
            return false;

        var userOptionId = ExtractOptionId(userAnswer);
        if (string.IsNullOrWhiteSpace(userOptionId))
            return false;

        return GetCorrectOptionIds().Contains(userOptionId);
    }

    public IReadOnlyList<string> GetCorrectAnswers()
    {
        var answers = CorrectAnswers
            .Select(answer => answer?.Trim() ?? string.Empty)
            .Where(answer => !string.IsNullOrWhiteSpace(answer))
            .ToList();
        if (answers.Count > 0)
            return answers;

        return string.IsNullOrWhiteSpace(CorrectAnswer)
            ? []
            : [CorrectAnswer.Trim()];
    }

    public bool IsCorrectTrueFalseAnswer(string? userAnswer)
    {
        var correctAnswer = NormalizeTrueFalseAnswer(CorrectAnswer);
        return IsTrueFalse &&
               correctAnswer is "正确" or "错误" &&
               !string.IsNullOrWhiteSpace(userAnswer) &&
               NormalizeTrueFalseAnswer(userAnswer) == correctAnswer;
    }

    public static string NormalizeTrueFalseAnswer(string? answer)
    {
        var value = (answer ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "正确" or "对" or "是" or "true" or "t" or "√" or "yes" => "正确",
            "错误" or "错" or "否" or "false" or "f" or "×" or "x" or "no" => "错误",
            _ => value
        };
    }

    public static string JoinBlankAnswers(IEnumerable<string> answers)
    {
        return string.Join('\u001F', answers.Select(answer => answer.Trim()));
    }

    public static IReadOnlyList<string> SplitBlankAnswers(string? answers)
    {
        if (string.IsNullOrWhiteSpace(answers))
            return [];

        return answers.Contains('\u001F')
            ? answers.Split('\u001F').Select(answer => answer.Trim()).ToList()
            : answers.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string ExtractOptionId(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return string.Empty;

        var trimmed = answer.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var first = char.ToUpperInvariant(trimmed[0]);
        return first is >= 'A' and <= 'Z' ? first.ToString() : string.Empty;
    }
}
