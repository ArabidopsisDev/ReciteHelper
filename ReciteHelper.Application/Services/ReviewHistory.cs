using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Scheduling;

namespace ReciteHelper.Application.Services;

public static class ReviewHistory
{
    public static void Clear(Project project, IEnumerable<Question> questions)
    {
        foreach (var question in questions)
        {
            var source = ReviewQuestionCatalog.FindFacet(project, question);
            question.Status = null;
            question.Memory = null;
            question.ReviewTag.Clear();
            question.LastAiAssessment = null;
            question.EFValue = 2.5;
            question.ReviewFacets.Clear();
            question.ArchivedReviewFacets.Clear();
            question.FacetsCreatedAt = null;

            if (source is { } facetSource)
            {
                facetSource.Facet.LastScoreRatio = 0;
                facetSource.Facet.NeedsReview = false;
                facetSource.Facet.ReviewAvailableAt = null;
                facetSource.Parent.Status = null;
                facetSource.Parent.LastAiAssessment = null;
            }
        }
    }
}
