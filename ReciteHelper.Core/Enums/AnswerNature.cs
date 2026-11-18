using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Enums;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<AnswerNature>))]
public enum AnswerNature
{
    None = 0,
    Conceptual = 1,
    Expressive = 2
}
