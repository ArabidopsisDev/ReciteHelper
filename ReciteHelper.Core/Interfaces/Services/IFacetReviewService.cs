using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Interfaces.Services;

public interface IFacetReviewService
{
    FacetReviewUpdate? RecordAssessment(Project project, Question question, AiAnswerAssessment assessment, DateTime reviewedAt);
}
