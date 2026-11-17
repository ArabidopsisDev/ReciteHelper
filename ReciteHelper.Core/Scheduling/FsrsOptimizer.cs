using System.Diagnostics;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Scheduling;

/// <summary>
/// One question's graded review history, expressed in whole study days between
/// consecutive reviews (the first entry always has <c>ElapsedDays = 0</c>).
/// </summary>
public sealed record ReviewSequence(IReadOnlyList<(int ElapsedDays, ReviewGrade Grade)> Reviews,
    IReadOnlyList<double>? ObservationWeights = null)
{
    public double WeightAt(int index)
    {
        var weight = ObservationWeights is null ? 1d : ObservationWeights[index];
        return double.IsFinite(weight) ? Math.Clamp(weight, 0, 1) : 0;
    }
}

public sealed record FitResult(
    SchedulerParameters Parameters,
    double InitialLoss,
    double FinalLoss,
    int Examples,
    int Iterations,
    TimeSpan Elapsed)
{
    public bool Improved => FinalLoss < InitialLoss;
}

/// <summary>
/// Fits the FSRS-6 parameters to a learner's own review history by minimising
///
///   L(w) = mean over predictable reviews of BCE(y, R) + γ · Σ_j ((w_j − w0_j)/σ_j)²
///
/// where R is the recall probability predicted from the state before the review,
/// y ∈ {0,1} is the judged outcome and the second term is the regulariser toward the
/// population defaults used by the FSRS reference optimiser (σ = published parameter
/// spread).  Same-day reviews update the state but are not scored, mirroring the
/// benchmark protocol.  Gradients are exact (forward-mode autodiff); the optimiser is
/// full-batch Adam with projection onto the admissible box after every step, so the
/// result is deterministic.
/// </summary>
public sealed class FsrsOptimizer
{
    private static readonly double[] DefaultSpread =
    [
        6.43, 9.66, 17.58, 27.85, 0.57, 0.28, 0.6, 0.12, 0.39, 0.18, 0.33, 0.3, 0.09, 0.16,
        0.57, 0.25, 1.03, 0.31, 0.32, 0.14, 0.27
    ];

    private const double Epsilon = 1e-7;

    public FsrsOptimizer(double gamma = 1.0, double learningRate = 0.02, int maxIterations = 300)
    {
        if (gamma < 0) throw new ArgumentOutOfRangeException(nameof(gamma));
        if (learningRate <= 0) throw new ArgumentOutOfRangeException(nameof(learningRate));
        if (maxIterations <= 0) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        Gamma = gamma;
        LearningRate = learningRate;
        MaxIterations = maxIterations;
    }

    public double Gamma { get; }
    public double LearningRate { get; }
    public int MaxIterations { get; }

    /// <summary>Extracts every question history with at least two reviews from the project.</summary>
    public static List<ReviewSequence> ExtractSequences(Project project)
    {
        var sequences = new List<ReviewSequence>();
        foreach (var question in ReviewQuestionCatalog.All(project))
        {
            var tags = question.ReviewTag.AsEnumerable();
            if (question.ReviewFacets.Count > 0)
            {
                var cutoff = question.FacetsCreatedAt ?? question.ReviewFacets.SelectMany(f => f.Question.ReviewTag)
                    .Select(tag => tag.Time).DefaultIfEmpty(DateTime.MinValue).Min();
                tags = tags.Where(tag => tag.Time < cutoff);
            }
            var ordered = tags.OrderBy(tag => tag.Time).ToList();
            if (ordered.Count < 2) continue;
            var reviews = new List<(int, ReviewGrade)>();
            var weights = new List<double>();
            DateTime? previous = null;
            foreach (var tag in ordered)
            {
                var grade = tag.Grade > 0 ? (ReviewGrade)tag.Grade : ReviewGrader.FromLegacyQuality(tag.QValue);
                var elapsed = previous is null ? 0 : ReviewCalendar.ElapsedDays(previous.Value, tag.Time);
                reviews.Add((elapsed, grade));
                weights.Add(tag.OptimizerWeight);
                previous = tag.Time;
            }
            sequences.Add(new ReviewSequence(reviews, weights));
        }
        return sequences;
    }

    /// <summary>Effective number of predictable observations, after rubric weighting.</summary>
    public static int CountExamples(IReadOnlyList<ReviewSequence> sequences)
        => (int)Math.Floor(sequences.Sum(s => s.Reviews.Select((r, i) => i > 0 && r.ElapsedDays >= 1 ? s.WeightAt(i) : 0).Sum()) + 1e-9);

    /// <summary>Loss and its exact gradient at the given weights.</summary>
    public (double Loss, double[] Gradient) Evaluate(IReadOnlyList<ReviewSequence> sequences, IReadOnlyList<double> weights)
    {
        var model = new FsrsDualModel(weights);
        var total = new Dual(0.0);
        var examples = 0d;

        foreach (var sequence in sequences)
        {
            if (sequence.Reviews.Count == 0) continue;
            var (s, d) = model.InitialState(sequence.Reviews[0].Grade);
            for (var k = 1; k < sequence.Reviews.Count; k++)
            {
                var (elapsed, grade) = sequence.Reviews[k];
                if (elapsed >= 1)
                {
                    var r = Dual.Clamp(model.Retrievability(s, elapsed), Epsilon, 1.0 - Epsilon);
                    var weight = sequence.WeightAt(k);
                    total += weight * (ReviewGrader.IsRecall(grade) ? -Dual.Log(r) : -Dual.Log(1.0 - r));
                    examples += weight;
                }
                (s, d) = model.NextState(s, d, elapsed, grade);
            }
        }

        var loss = examples > 0 ? total / examples : new Dual(0.0);
        for (var j = 0; j < SchedulerParameters.Count; j++)
        {
            var z = (model[j] - SchedulerParameters.Default[j]) / DefaultSpread[j];
            loss += Gamma * z * z;
        }

        var gradient = new double[SchedulerParameters.Count];
        for (var j = 0; j < SchedulerParameters.Count; j++) gradient[j] = loss.G[j];
        return (loss.V, gradient);
    }

    /// <summary>
    /// Runs the optimisation from <paramref name="start"/> (defaults if null).
    /// The returned parameters are always admissible; if the objective could not be
    /// improved the starting point is returned unchanged.
    /// </summary>
    public FitResult Fit(IReadOnlyList<ReviewSequence> sequences, SchedulerParameters? start = null)
    {
        var sw = Stopwatch.StartNew();
        start ??= SchedulerParameters.Default;
        var w = start.Weights.ToArray();
        var examples = CountExamples(sequences);

        var (initialLoss, _) = Evaluate(sequences, w);
        if (examples == 0)
            return new FitResult(start, initialLoss, initialLoss, 0, 0, sw.Elapsed);

        var best = (double[])w.Clone();
        var bestLoss = initialLoss;
        var m = new double[SchedulerParameters.Count];
        var v = new double[SchedulerParameters.Count];
        const double beta1 = 0.9, beta2 = 0.999, eps = 1e-8;
        var stale = 0;
        var iterations = 0;

        for (var t = 1; t <= MaxIterations; t++)
        {
            var (loss, grad) = Evaluate(sequences, w);
            iterations = t;
            if (loss < bestLoss - 1e-9)
            {
                bestLoss = loss;
                Array.Copy(w, best, w.Length);
                stale = 0;
            }
            else if (++stale >= 20)
            {
                break;
            }

            for (var j = 0; j < SchedulerParameters.Count; j++)
            {
                m[j] = beta1 * m[j] + (1 - beta1) * grad[j];
                v[j] = beta2 * v[j] + (1 - beta2) * grad[j] * grad[j];
                var mHat = m[j] / (1 - Math.Pow(beta1, t));
                var vHat = v[j] / (1 - Math.Pow(beta2, t));
                w[j] -= LearningRate * mHat / (Math.Sqrt(vHat) + eps);
                w[j] = Math.Clamp(w[j], SchedulerParameters.LowerBound(j), SchedulerParameters.UpperBound(j));
            }
        }

        var parameters = bestLoss < initialLoss ? new SchedulerParameters(best) : start;
        return new FitResult(parameters, initialLoss, bestLoss, examples, iterations, sw.Elapsed);
    }
}
