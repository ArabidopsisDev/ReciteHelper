using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Application.Services;

public static class ReviewHistory
{
    public static IReadOnlySet<Question> Clear(Project project, IEnumerable<Question> questions, bool answersOnly = false)
    {
        var selected = questions.ToHashSet<Question>(ReferenceEqualityComparer.Instance);
        var affected = new HashSet<Question>(ReferenceEqualityComparer.Instance);
        foreach (var question in selected)
        {
            var source = ReviewQuestionCatalog.FindFacet(project, question);
            if (source is { } facetSource && selected.Contains(facetSource.Parent)) continue;
            foreach (var item in DescendantsAndSelf(question))
            {
                affected.Add(item);
                item.SubmittedAnswer = null;
                if (answersOnly)
                {
                    item.AnswerCleared = true;
                    if (item.LastAiAssessment is { } assessment)
                    {
                        assessment.UserAnswer = string.Empty;
                        assessment.Annotations.Clear();
                    }
                }
                else
                    Reset(item);
            }

            if (!answersOnly && source is { } facet)
            {
                facet.Facet.LastScoreRatio = 0;
                facet.Facet.NeedsReview = false;
                facet.Facet.ReviewAvailableAt = null;
                // The last whole answer no longer represents the reset component.
                facet.Parent.Status = null;
                facet.Parent.LastAiAssessment = null;
                facet.Parent.SubmittedAnswer = null;
                affected.Add(facet.Parent);
            }
        }
        var cleared = affected.Select(question => question.ReviewIdentity).ToHashSet();
        if (answersOnly)
        {
            for (var index = 0; index < project.ReviewMistakes.Count; index++)
                if (cleared.Contains(project.ReviewMistakes[index].QuestionId))
                    project.ReviewMistakes[index] = project.ReviewMistakes[index] with { Answer = [] };
        }
        else
        {
            project.ReviewMistakes.RemoveAll(entry => cleared.Contains(entry.QuestionId));
        }
        project.NotifyReviewMistakesChanged();
        return affected;
    }

    private static IEnumerable<Question> DescendantsAndSelf(Question question)
        => question.ReviewFacets.Concat(question.ArchivedReviewFacets)
            .SelectMany(facet => DescendantsAndSelf(facet.Question)).ToArray().Append(question);

    private static void Reset(Question question)
    {
        question.Status = null;
        question.AnswerCleared = false;
        question.Memory = null;
        question.ReviewTag.Clear();
        question.ExamHistory = null;
        question.LastAiAssessment = null;
        question.EFValue = 2.5;
        question.ReviewFacets.Clear();
        question.ArchivedReviewFacets.Clear();
        question.FacetsCreatedAt = null;
    }
}
