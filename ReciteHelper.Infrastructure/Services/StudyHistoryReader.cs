using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Scheduling;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed record StudyHistory(IReadOnlyList<StudyAttempt> Attempts, int UnavailableProjects);

public static class StudyHistoryReader
{
    public static async Task<StudyHistory> ReadAsync(IReadOnlyList<StudyProject> projects, CancellationToken cancellationToken = default)
    {
        var attempts = new List<StudyAttempt>();
        var recorded = new HashSet<(Guid Question, DateTimeOffset Time, bool IsExam)>();
        var unavailable = 0;
        foreach (var registration in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = registration.FilePath;
            try
            {
                var project = await ProjectFileService.OpenProjectFileWithoutKnowledgeBaseAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                if (project is null)
                {
                    unavailable++;
                    continue;
                }
                var name = project.ProjectName ?? Path.GetFileNameWithoutExtension(path);
                foreach (var root in ReviewQuestionCatalog.Roots(project))
                {
                    Add(root, root.ReviewIdentity, root.ReviewTag, name, isExam: false);
                    Add(root, root.ReviewIdentity, root.ExamHistory ?? [], name, isExam: true);
                    foreach (var facet in root.ReviewFacets.Concat(root.ArchivedReviewFacets))
                    {
                        var question = facet.Question;
                        // A whole answer also produces synthetic observations for its components.
                        Add(question, root.ReviewIdentity, question.ReviewTag.Where(tag =>
                            tag.ResponseSeconds is not null || tag.AnswerLength is not null), name, isExam: false);
                    }
                }
                var directory = Path.Combine(Path.GetDirectoryName(path)!, "exams");
                foreach (var exam in await JsonExamSetRepository.LoadFromDirectoryAsync(project, directory, cancellationToken)
                             .ConfigureAwait(false))
                {
                    var subject = string.IsNullOrWhiteSpace(exam.SubjectName) ? name : exam.SubjectName;
                    foreach (var item in exam.Questions)
                        Add(item.Question, item.Question.ReviewIdentity, item.Question.ExamHistory ?? [], subject, isExam: true);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                unavailable++;
            }

            void Add(Question question, Guid topic, IEnumerable<ReviewTag> tags, string subject, bool isExam)
            {
                var expectedSeconds = ExpectedSeconds(question);
                foreach (var tag in tags)
                {
                    if (tag.Time == default) continue;
                    var time = new DateTimeOffset(tag.Time);
                    if (time < registration.ImportedAt || !recorded.Add((question.ReviewIdentity, time, isExam)))
                        continue;
                    var correct = tag.IsCorrect ?? LegacyResult(tag);
                    var quality = tag.ScoreRatio ?? (tag.Similarity > 0 ? tag.Similarity : correct switch
                    {
                        true => 1d, false => 0d, null => (double?)null
                    });
                    attempts.Add(new(path, subject, question.ReviewIdentity, topic, time, isExam,
                        correct, QualityRatio(quality), PositiveDuration(tag.ResponseSeconds),
                        PositiveDuration(tag.ExamDurationSeconds), expectedSeconds)
                    {
                        HasFallacy = tag.Verdict is "FL" or "FA"
                    });
                }
            }
        }
        return new(attempts, unavailable);
    }

    private static bool? LegacyResult(ReviewTag tag) => tag.Verdict switch
    {
        "AC" => true,
        "PC" or "PT" or "WA" or "FL" or "FA" => false,
        _ when tag.Grade > 0 => ReviewGrader.IsRecall((ReviewGrade)tag.Grade),
        _ when tag.QValue > 0 => ReviewGrader.IsRecall(ReviewGrader.FromLegacyQuality(tag.QValue)),
        _ => null
    };

    private static double? QualityRatio(double? value) => value is { } score && double.IsFinite(score)
        ? Math.Clamp(score > 1 ? score / 100 : score, 0, 1) : null;

    private static double? PositiveDuration(double? value) => value is > 0 and < double.PositiveInfinity ? value : null;

    private static double ExpectedSeconds(Question question)
    {
        var parts = Math.Clamp(question.AnswerParts.Count, 1, 5);
        return question.Type switch
        {
            QuestionType.TrueFalse => 12,
            QuestionType.SingleChoice => 22,
            QuestionType.FillBlank => 20 * Math.Clamp(question.BlankCount, 1, 6),
            QuestionType.TermDefinition => 65,
            QuestionType.ShortAnswer => 90 * parts,
            _ => 125 * parts
        };
    }
}
