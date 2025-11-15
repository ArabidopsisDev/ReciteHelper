using FuzzyString;
using ReciteHelper.Model;

namespace ReciteHelper.Utils;

internal class JudgeAnswer
{
    internal static bool Run(string? userAnswer, string? correctAnswer)
    {
        ArgumentNullException.ThrowIfNull(userAnswer, nameof(userAnswer));
        ArgumentNullException.ThrowIfNull(correctAnswer, nameof(correctAnswer));

        var question = new Question()
        {
            UserAnswer = userAnswer,
            CorrectAnswer = correctAnswer,
        };

        return Run(new QuestionItem() { Question = question ,UserAnswer = userAnswer});
    }

    internal static bool Run(ExamQuestionItem question)
    {
        ArgumentNullException.ThrowIfNull(question, nameof(question));

        return Run(question.UserAnswer, question.Question!.CorrectAnswer);
    }

    internal static bool Run(QuestionItem question)
    {
        if (string.IsNullOrEmpty(question.UserAnswer)) return false;

        // Traditional algorithm
        var tolerance = FuzzyStringComparisonTolerance.Strong;
        var comparisonOptions = new List<FuzzyStringComparisonOptions>
        {
            FuzzyStringComparisonOptions.UseOverlapCoefficient,
            FuzzyStringComparisonOptions.UseLongestCommonSubsequence,
            FuzzyStringComparisonOptions.UseLongestCommonSubstring
        };

        // Calculate text cosine similarity
        var similarity = new CosineSimilarity();
        var score = similarity.Calculate(question.UserAnswer,
            question.Question!.CorrectAnswer!);

        bool isCorrect = question.UserAnswer.ApproximatelyEquals(
            question.Question!.CorrectAnswer, comparisonOptions, tolerance);
        isCorrect = isCorrect | (score > .4);

        return isCorrect;
    }
}
