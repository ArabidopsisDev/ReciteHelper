using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Scheduling;

public static class ReviewQuestionCatalog
{
    public static IEnumerable<Question> Roots(Project project)
        => project.Chapters?.SelectMany(chapter => chapter.Questions ?? []) ?? [];

    public static bool HasAnswerRecord(Question question)
        => question.ReviewTag.Count > 0 || question.Memory is not null ||
           question.LastAiAssessment is not null || question.Status is not null;

    public static IEnumerable<Question> All(Project project)
        => Roots(project).SelectMany(question => new[] { question }.Concat(question.ReviewFacets.Select(f => f.Question)))
            .Distinct<Question>(ReferenceEqualityComparer.Instance);

    public static IEnumerable<ReviewFacet> ActiveFacets(Question parent)
        => parent.IsArchived ? [] : parent.ReviewFacets.Where(facet => !facet.Question.IsArchived);

    public static IEnumerable<Question> Archived(Project project) => ArchiveHeads(project)
        .SelectMany(question => question.ReviewFacets.Select(facet => facet.Question).Prepend(question));

    // A root archive represents all its facets, including ones previously archived on their own.
    public static IEnumerable<Question> ArchiveHeads(Project project) => Roots(project).SelectMany(parent =>
        parent.IsArchived ? Enumerable.Repeat(parent, 1)
            : parent.ReviewFacets.Select(facet => facet.Question).Where(question => question.IsArchived));

    public static Question? FindOriginal(Project project, Question question)
        => Roots(project).FirstOrDefault(root => ReferenceEquals(root, question) ||
            question.ReviewIdentity != Guid.Empty && root.ReviewIdentity == question.ReviewIdentity);

    public static (Question Parent, ReviewFacet Facet)? FindFacet(Project project, Question question)
    {
        if (!question.IsReviewFacet) return null;
        foreach (var parent in Roots(project))
            foreach (var facet in parent.ReviewFacets)
                if (ReferenceEquals(question, facet.Question) || question.ReviewFacetId == facet.Question.ReviewFacetId)
                    return (parent, facet);
        return null;
    }
}
