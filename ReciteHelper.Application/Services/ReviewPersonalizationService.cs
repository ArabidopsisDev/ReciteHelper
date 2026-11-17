using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Application.Services;

/// <summary>
/// Personalisation policy on top of <see cref="FsrsOptimizer"/>:
///   * do nothing until the project holds at least <see cref="MinimumScoredReviews"/>
///     predictable reviews: on public data a fit on fewer reviews improved only a
///     minority of learners, while from 800 reviews on it improved most of them;
///   * refit only after the history has grown by <see cref="RefitGrowthFactor"/>;
///   * keep the previous parameters unless the objective improves by at least
///     <see cref="MinimumRelativeImprovement"/>;
///   * after a successful fit, replay every question history so that the stored
///     memory states are consistent with the new parameters.
/// </summary>
public sealed class ReviewPersonalizationService : IReviewPersonalizationService
{
    public const int MinimumScoredReviews = 800;
    public const double RefitGrowthFactor = 1.25;
    public const double MinimumRelativeImprovement = 1e-3;

    private readonly FsrsOptimizer _optimizer;

    public ReviewPersonalizationService() : this(new FsrsOptimizer()) { }

    public ReviewPersonalizationService(FsrsOptimizer optimizer)
    {
        _optimizer = optimizer;
    }

    public async Task<FitResult?> TryPersonalizeAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var sequences = FsrsOptimizer.ExtractSequences(project);
        var examples = FsrsOptimizer.CountExamples(sequences);
        if (examples < MinimumScoredReviews) return null;
        if (project.SchedulerParameters is not null && examples < project.SchedulerFitReviews * RefitGrowthFactor) return null;

        var start = project.SchedulerParameters;
        var result = await Task.Run(() => _optimizer.Fit(sequences, start), cancellationToken);

        var relative = (result.InitialLoss - result.FinalLoss) / Math.Max(result.InitialLoss, 1e-12);
        if (!result.Improved || relative < MinimumRelativeImprovement)
        {
            project.SchedulerFitReviews = examples; // remember that this history size was evaluated
            return null;
        }

        project.SchedulerParameters = result.Parameters;
        project.SchedulerFitReviews = examples;
        RebuildMemoryRecords(project);
        return result;
    }

    /// <summary>Recomputes every memory record from its history with the project parameters.</summary>
    public static void RebuildMemoryRecords(Project project)
    {
        var parameters = project.SchedulerParameters ?? SchedulerParameters.Default;
        foreach (var question in ReviewQuestionCatalog.All(project))
        {
            var replayed = LegacyReviewMigrator.Replay(question.ReviewTag, parameters);
            if (replayed is not null) question.Memory = replayed;
        }
    }
}
