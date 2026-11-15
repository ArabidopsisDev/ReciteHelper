using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Interfaces.Services;

public interface IRuleReviewService
{
    Task<RuleReviewPreview> PreviewAsync(RuleReviewCategory category, CancellationToken cancellationToken = default);
    Task<Chapter> CompleteAsync(RuleReviewPreview acceptedPreview, CancellationToken cancellationToken = default);
}
