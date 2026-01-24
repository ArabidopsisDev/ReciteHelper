using JiebaNet.Segmenter;

namespace ReciteHelper.Utils;

public class CosineSimilarity
{
    private readonly JiebaSegmenter _segmenter = new JiebaSegmenter();

    public double Calculate(string textA, string textB)
    {
        if (string.IsNullOrWhiteSpace(textA) || string.IsNullOrWhiteSpace(textB))
            return 0.0;

        var tokensA = ChineseTokenize(textA);
        var tokensB = ChineseTokenize(textB);


        if (tokensA.Count <= 2 || tokensB.Count <= 2)
        {
            Console.WriteLine("检测到短文本，使用混合相似度计算");
            return CalculateMixedSimilarity(textA, textB, tokensA, tokensB);
        }

        return CalculateWordLevelSimilarity(tokensA, tokensB);
    }

    // Danger: The effectiveness of the hybrid cosine similarity algorithm in processing
    //         short texts has not been proven
    private double CalculateMixedSimilarity(string textA, string textB,
                                          List<string> tokensA, List<string> tokensB)
    {
        double wordSimilarity = CalculateWordLevelSimilarity(tokensA, tokensB);

        double charSimilarity = CalculateCharLevelSimilarity(textA, textB);
        double finalScore = wordSimilarity * 0.6 + charSimilarity * 0.4;

        return finalScore;
    }

    private double CalculateWordLevelSimilarity(List<string> tokensA, List<string> tokensB)
    {
        if (!tokensA.Any() || !tokensB.Any())
            return 0.0;

        var vocabulary = tokensA.Union(tokensB).Distinct().ToList();
        var vectorA = CreateVector(tokensA, vocabulary);
        var vectorB = CreateVector(tokensB, vocabulary);

        double similarity = ComputeCosineSimilarity(vectorA, vectorB);

        return similarity;
    }

    private double CalculateCharLevelSimilarity(string textA, string textB)
    {
        var charsA = textA.Where(c => !char.IsWhiteSpace(c)).Distinct().ToList();
        var charsB = textB.Where(c => !char.IsWhiteSpace(c)).Distinct().ToList();

        if (!charsA.Any() || !charsB.Any())
            return 0.0;

        var intersection = charsA.Intersect(charsB).Count();
        var union = charsA.Union(charsB).Count();

        double jaccard = union == 0 ? 0.0 : (double)intersection / union;

        return jaccard;
    }

    private List<string> ChineseTokenize(string text)
    {
        return _segmenter.Cut(text)
                         .Select(t => t.Trim())
                         .Where(t => !string.IsNullOrEmpty(t))
                         .ToList();
    }

    private double[] CreateVector(List<string> tokens, List<string> vocabulary)
    {
        var vector = new double[vocabulary.Count];
        var tokenFreq = tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

        for (int i = 0; i < vocabulary.Count; i++)
        {
            if (tokenFreq.ContainsKey(vocabulary[i]))
            {
                vector[i] = tokenFreq[vocabulary[i]];
            }
        }
        return vector;
    }

    private double ComputeCosineSimilarity(double[] vecA, double[] vecB)
    {
        double dot = 0.0, magA = 0.0, magB = 0.0;

        for (int i = 0; i < vecA.Length; i++)
        {
            dot += vecA[i] * vecB[i];
            magA += vecA[i] * vecA[i];
            magB += vecB[i] * vecB[i];
        }

        if (magA == 0 || magB == 0)
            return 0.0;

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}