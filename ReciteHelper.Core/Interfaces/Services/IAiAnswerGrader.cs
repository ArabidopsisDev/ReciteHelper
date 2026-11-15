using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Interfaces.Services;

public interface IAiAnswerGrader
{
    Task<AiAnswerAssessment> GradeAsync(Question question, string userAnswer);
}
