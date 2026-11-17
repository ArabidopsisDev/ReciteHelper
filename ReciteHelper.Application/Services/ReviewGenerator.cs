using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Application.Services;

/// <summary>
/// Builds review sessions from predicted recall probabilities.
/// Unresolved facets are prioritised by missing rubric coverage. Other due items
/// follow, then fresh items and optional top-up items. A decomposed original never
/// re-enters the smart-review pool.
/// </summary>
public class ReviewGenerator : IReviewGenerator
{
    private readonly IReviewScheduler _scheduler;
    private readonly Random _random = new();

    public ReviewGenerator(IReviewScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public List<Question> GenerateReview(Project project, int count)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (count <= 0) throw new ArgumentException("数量必须大于0", nameof(count));

        var ranked = Rank(project, DateTime.Now);
        if (ranked.Count <= count)
            return ResetStatus(ranked.Select(r => r.Question).ToList());

        var due = ranked.Where(r => r.IsWeakFacet || r.Recall is { } p && p < _scheduler.DesiredRetention);
        var fresh = ranked.Where(r => !r.IsWeakFacet && r.Recall is null).OrderBy(_ => _random.Next());
        var rest = ranked.Where(r => !r.IsWeakFacet && r.Recall is { } p && p >= _scheduler.DesiredRetention);

        var result = due.Concat(fresh).Concat(rest).Take(count).Select(r => r.Question).ToList();
        return ResetStatus(result);
    }

    public List<Question> GenerateParameterizationReview(Project project, ReviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var ranked = Rank(project, DateTime.Now);
        var reviewed = ranked.Where(r => r.IsWeakFacet || r.Recall is not null).ToList();
        var fresh = ranked.Where(r => !r.IsWeakFacet && r.Recall is null).OrderBy(_ => _random.Next()).ToList();

        var newCount = (int)(options.TotalCount * options.NewQuestionRatio);
        var reviewCount = Math.Max(0, options.TotalCount - newCount);

        IEnumerable<Ranked> reviewPart = options.PrioritizeWeakPoints
            ? reviewed // already ordered by weak status and expected coverage deficit
            : reviewed.OrderBy(_ => _random.Next());

        var result = reviewPart.Take(reviewCount)
            .Concat(fresh.Take(newCount))
            .Select(r => r.Question)
            .ToList();

        // Top up from whatever is left when one of the pools is short.
        if (result.Count < options.TotalCount)
        {
            var chosen = new HashSet<Question>(result, ReferenceEqualityComparer.Instance);
            var remaining = ranked.Select(r => r.Question).Where(q => !chosen.Contains(q));
            result.AddRange(remaining.Take(options.TotalCount - result.Count));
        }

        return ResetStatus(result);
    }

    /// <summary>Eligible items ranked by weak status and missing coverage; fresh items last.</summary>
    public List<Ranked> Rank(Project project, DateTime now)
    {
        var ranked = new List<Ranked>();
        foreach (var question in ReviewQuestionCatalog.Roots(project))
        {
            if (question.ReviewFacets.Count == 0)
            {
                var recall = _scheduler.PredictRecall(project, question, now);
                var weak = question.LastAiAssessment is { IsCorrect: false } &&
                    (question.RequiresAiGrading || project.TemplateType == Core.Enums.ProjectTemplateType.RuleBasedReview);
                if (weak && question.Memory is { } memory && now < memory.LastReview + CfsrsPolicy.RelearningDelay) continue;
                if (!weak && (question.RequiresAiGrading || project.TemplateType == Core.Enums.ProjectTemplateType.RuleBasedReview) &&
                    recall >= _scheduler.DesiredRetention) continue;
                ranked.Add(new Ranked(question, recall, weak, recall is null ? 0 : 1 -
                    (weak ? question.LastAiAssessment!.ScoreRatio : 1) * recall.Value));
                continue;
            }
            // A decomposed parent is never reintroduced, including during top-up.
            foreach (var facet in question.ReviewFacets)
            {
                var recall = _scheduler.PredictRecall(project, facet.Question, now);
                if (CfsrsPolicy.IsAvailable(facet, now, recall, _scheduler.DesiredRetention))
                    ranked.Add(new Ranked(facet.Question, recall, facet.NeedsReview,
                        facet.Weight / question.ReviewFacets.Sum(f => f.Weight) * (1 - facet.LastScoreRatio * (recall ?? 0)), question.ReviewIdentity));
            }
        }

        var ordered = ranked
            .OrderBy(r => r.IsWeakFacet ? 0 : r.Recall is null ? 2 : 1)
            .ThenByDescending(r => r.CoverageDeficit)
            .ThenBy(r => r.Recall ?? 1.0)
            .ThenBy(_ => _random.Next())
            .ToList();
        // Give other source questions a turn before another sibling from the same question.
        // This also prevents a heavily split question from occupying the entire short session.
        var turns = new Dictionary<Guid, int>();
        return ordered.Select(row =>
        {
            var parent = row.ParentIdentity ?? row.Question.ReviewIdentity;
            var turn = turns.GetValueOrDefault(parent);
            turns[parent] = turn + 1;
            return (Row: row, Turn: turn);
        }).OrderBy(item => item.Turn).Select(item => item.Row).ToList();
    }

    public readonly record struct Ranked(Question Question, double? Recall, bool IsWeakFacet = false, double CoverageDeficit = 0, Guid? ParentIdentity = null);

    private static List<Question> ResetStatus(List<Question> questions)
    {
        questions.ForEach(question => question.Status = null);
        return questions;
    }
}
