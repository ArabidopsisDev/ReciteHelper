using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Core.Interfaces.Services;

/// <summary>
/// Memory-model scheduler: predicts recall, records reviews and derives intervals.
/// Parameters are taken from the project (personalised) or the population defaults.
/// </summary>
public interface IReviewScheduler
{
    /// <summary>Target recall probability at which an item becomes due.</summary>
    double DesiredRetention { get; }

    SchedulerParameters ParametersFor(Project project);

    /// <summary>Creates the memory record from legacy tags if needed; returns true when migrated.</summary>
    bool EnsureMemory(Project project, Question question);

    /// <summary>Predicted recall, or expected rubric coverage for a composite; null for a never-reviewed item.</summary>
    double? PredictRecall(Project project, Question question, DateTime now);

    /// <summary>Applies a graded review to the question's memory record and returns the outcome.</summary>
    ReviewOutcome Record(Project project, Question question, ReviewGrade grade, DateTime reviewedAt);
}
