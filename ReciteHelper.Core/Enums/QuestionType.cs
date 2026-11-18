using System.Text.Json.Serialization;
using ReciteHelper.Core.Serialization;

namespace ReciteHelper.Core.Enums;

[JsonConverter(typeof(QuestionTypeJsonConverter))]
public enum QuestionType
{
    Essay = 0,
    SingleChoice = 1,
    FillBlank = 2,
    TrueFalse = 3,
    TermDefinition = 4,
    ShortAnswer = 5,
    Discussion = 6,
    Comprehensive = 7
}
