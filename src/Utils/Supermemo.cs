using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReciteHelper.Model;
using System.Numerics;

namespace ReciteHelper.Utils;

/// <summary>
/// Provides methods for calculating SuperMemo algorithm values and predicting quality ratings using machine learning
/// models.
/// </summary>
/// <remarks>The Supermemo class includes static methods for working with spaced repetition algorithms, such as
/// calculating the easiness factor (EF) and predicting quality (Q) values. These methods are intended for use in
/// applications that implement or extend the SuperMemo learning methodology. All members are intended for internal use
/// and are not thread-safe.</remarks>
internal class Supermemo
{
    internal static double CalculateEFValue(double ef, int q)
    {
        var newEF = ef + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02));

        return newEF;
    }

    internal static int PredictQValue<TValue>(TValue relRelative, TValue similarity)
        where TValue : struct, INumber<TValue>
    {

        // Load model
        string modelPath = "xgboost_predQ.onnx";
        using var session = new InferenceSession(modelPath);

        // The model is expected to have an accuracy of approximately
        // 70% ​​and is currently undergoing further training
        float[] inputData = [
            float.CreateChecked(relRelative),
            float.CreateChecked(similarity) * 100f
        ];
        int[] dimensions = [1, 2];
        var inputTensor = new DenseTensor<float>(inputData, dimensions);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("float_input", inputTensor)
        };
        using var results = session.Run(inputs);
        var label = results.First(r => r.Name == "label").AsEnumerable<long>().First();
        var probs = results.First(r => r.Name == "probabilities").AsEnumerable<float>().ToArray();

        float maxProb = 0;
        int maxIndex = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            if (probs[i] > maxProb)
                maxIndex = i;
            maxProb = Math.Max(maxProb, probs[i]);
        }

        // Predict result
        return maxIndex;
    }

    internal static List<Question> GenerateReview(Project project, int count)
    {
        var allQuestions = new List<Question>();

        foreach (var chapter in project.Chapters!)
            allQuestions.AddRange(chapter.Questions!);

        if (allQuestions.Count <= 20) return allQuestions;

        var rnd = new Random();
        var shuffle = allQuestions.OrderBy(q => q.EFValue)
            .ThenBy(q => rnd.Next()).Take(count).ToList();

        // Clear status
        foreach (var question in shuffle)
        {
            question.UserAnswer = string.Empty;
            question.Status = null;
        }

        return shuffle;
    }
}
