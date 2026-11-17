using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Application.Services;

/// <summary>FSRS-6 based implementation of <see cref="IReviewScheduler"/>.</summary>
public sealed class FsrsReviewScheduler : IReviewScheduler
{
    public const double DefaultDesiredRetention = 0.9;

    public FsrsReviewScheduler(double desiredRetention = DefaultDesiredRetention)
    {
        if (desiredRetention is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(desiredRetention));
        DesiredRetention = desiredRetention;
    }

    public double DesiredRetention { get; }

    public SchedulerParameters ParametersFor(Project project)
        => project.SchedulerParameters ?? SchedulerParameters.Default;

    public bool EnsureMemory(Project project, Question question)
        => LegacyReviewMigrator.EnsureMemory(question, ParametersFor(project));

    public double? PredictRecall(Project project, Question question, DateTime now)
    {
        if (question.ReviewFacets.Count > 0)
            return FacetMemoryProjection.Recall(question, now, ParametersFor(project));
        EnsureMemory(project, question);
        if (question.Memory is null) return null;
        var elapsed = ReviewCalendar.ElapsedDays(question.Memory.LastReview, now);
        return FsrsAlgorithm.Retrievability(question.Memory.Stability, elapsed, ParametersFor(project));
    }

    public ReviewOutcome Record(Project project, Question question, ReviewGrade grade, DateTime reviewedAt)
    {
        var parameters = ParametersFor(project);
        EnsureMemory(project, question);

        if (question.Memory is null)
        {
            var initial = FsrsAlgorithm.InitialState(grade, parameters);
            question.Memory = MemoryRecord.From(initial, reviewedAt, 1, ReviewGrader.IsRecall(grade) ? 0 : 1);
            return new ReviewOutcome
            {
                Grade = grade,
                ElapsedDays = 0,
                RetrievabilityBefore = 1.0,
                StateAfter = initial,
                NextIntervalDays = FsrsAlgorithm.IntervalForRetention(initial.Stability, DesiredRetention, parameters),
                IsFirstReview = true
            };
        }

        var memory = question.Memory;
        var elapsed = ReviewCalendar.ElapsedDays(memory.LastReview, reviewedAt);
        var before = FsrsAlgorithm.Retrievability(memory.Stability, elapsed, parameters);
        var next = FsrsAlgorithm.NextState(memory.State, elapsed, grade, parameters);
        memory.Apply(next, reviewedAt, grade);

        return new ReviewOutcome
        {
            Grade = grade,
            ElapsedDays = elapsed,
            RetrievabilityBefore = before,
            StateAfter = next,
            NextIntervalDays = FsrsAlgorithm.IntervalForRetention(next.Stability, DesiredRetention, parameters),
            IsFirstReview = false
        };
    }
}
