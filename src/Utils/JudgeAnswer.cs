using FuzzyString;
using ReciteHelper.Model;
using ReciteHelper.ViewModel;

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

        // Are the traditionalists still refusing to admit defeat?
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
        if (question.UserAnswer.Length >= 15) score -= .2d;
        isCorrect = isCorrect | (score > .4);

        return isCorrect;
    }

    public static double CalculateSimilarity(QuestionItem question)
    {
        var userAnswer = question.UserAnswer;
        var correctAnswer = question.Question!.CorrectAnswer;

        if (string.IsNullOrEmpty(userAnswer) && string.IsNullOrEmpty(correctAnswer))
            return 1.0;

        if (string.IsNullOrEmpty(userAnswer) || string.IsNullOrEmpty(correctAnswer))
            return 0.0;

        int maxLength = Math.Max(userAnswer.Length, correctAnswer.Length);
        if (maxLength == 0) return 1.0;

        double distance = ComputeLevenshteinDistance(userAnswer, correctAnswer);
        return 1.0 - (distance / maxLength);
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    public void UpdateEFValue(Question question, int qScore)
    {
        if (question.ReviewTag.Count < 2)
            return;

        double oldEF = question.EFValue;
        double newEF;

        if (qScore >= 3)
        {
            double factor = 0.1 - (5 - qScore) * (0.08 + (5 - qScore) * 0.02);
            newEF = oldEF + factor;
            newEF = Math.Max(1.3, newEF);
        }
        else
        {
            newEF = oldEF - 0.2;
            newEF = Math.Max(1.3, newEF);
        }

        question.EFValue = Math.Round(newEF, 2);
    }

}
