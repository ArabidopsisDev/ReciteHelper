using System.Text.Json;
using System.Text.Json.Serialization;
using ReciteHelper.Core.Enums;

namespace ReciteHelper.Core.Serialization;

public sealed class QuestionTypeJsonConverter : JsonConverter<QuestionType>
{
    public override QuestionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            return Enum.IsDefined(typeof(QuestionType), number) ? (QuestionType)number : QuestionType.Essay;

        if (reader.TokenType != JsonTokenType.String)
            return QuestionType.Essay;

        return Parse(reader.GetString());
    }

    public static QuestionType Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return QuestionType.Essay;

        var normalized = value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        return normalized switch
        {
            "shortanswer" or "short_answer" or "简答题" => QuestionType.ShortAnswer,
            "discussion" or "论述题" => QuestionType.Discussion,
            "comprehensive" or "综合题" => QuestionType.Comprehensive,
            "essay" or "解答题" => QuestionType.Essay,
            "singlechoice" or "single_choice" or "choice" or "选择题" or "单项选择题" => QuestionType.SingleChoice,
            "fillblank" or "fill_blank" or "blank" or "填空题" => QuestionType.FillBlank,
            "truefalse" or "true_false" or "judgment" or "判断题" => QuestionType.TrueFalse,
            "termdefinition" or "term_definition" or "definition" or "名词解释" => QuestionType.TermDefinition,
            _ when Enum.TryParse<QuestionType>(value, ignoreCase: true, out var parsed) => parsed,
            _ => QuestionType.Essay
        };
    }

    public override void Write(Utf8JsonWriter writer, QuestionType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            QuestionType.SingleChoice => nameof(QuestionType.SingleChoice),
            QuestionType.FillBlank => nameof(QuestionType.FillBlank),
            QuestionType.TrueFalse => nameof(QuestionType.TrueFalse),
            QuestionType.TermDefinition => nameof(QuestionType.TermDefinition),
            QuestionType.ShortAnswer => nameof(QuestionType.ShortAnswer),
            QuestionType.Discussion => nameof(QuestionType.Discussion),
            QuestionType.Comprehensive => nameof(QuestionType.Comprehensive),
            _ => nameof(QuestionType.Essay)
        });
    }
}
