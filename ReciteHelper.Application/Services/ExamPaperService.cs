using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;

namespace ReciteHelper.Application.Services;

public sealed class ExamPaperService : IExamPaperService
{
    private const double ChoiceQuestionRatio = 0.30d;

    public List<Question> Generate(Project project, ExamSettings settings)
    {
        var candidates = project.Chapters?
            .Where(chapter => chapter.Questions is { Count: > 0 })
            .SelectMany(chapter => chapter.Questions!.Select(question => new QuestionCandidate(
                question,
                chapter.Name ?? string.Empty,
                GetChapterWeight(settings, chapter.Name))))
            .ToList() ?? [];

        candidates = candidates
            .Where(candidate => candidate.Question.Type != QuestionType.TrueFalse)
            .ToList();

        if (candidates.Count < settings.QuestionCount)
            return [];

        var targetChoiceCount = (int)Math.Round(
            settings.QuestionCount * ChoiceQuestionRatio,
            MidpointRounding.AwayFromZero);
        var remainingCount = settings.QuestionCount - targetChoiceCount;
        var targetFillBlankCount = (int)Math.Round(remainingCount * 0.35d, MidpointRounding.AwayFromZero);
        var targetTermDefinitionCount = (int)Math.Round(remainingCount * 0.20d, MidpointRounding.AwayFromZero);
        var targetEssayCount = remainingCount - targetFillBlankCount - targetTermDefinitionCount;

        var selected = new List<QuestionCandidate>();
        SelectType(QuestionType.SingleChoice, targetChoiceCount);
        SelectType(QuestionType.FillBlank, targetFillBlankCount);
        SelectType(QuestionType.TermDefinition, targetTermDefinitionCount);
        Select(question => question.IsEssay, targetEssayCount);

        if (selected.Count < settings.QuestionCount)
        {
            var selectedQuestions = selected.Select(candidate => candidate.Question).ToHashSet<Question>(ReferenceEqualityComparer.Instance);
            selected.AddRange(SelectCandidates(
                candidates.Where(candidate => !selectedQuestions.Contains(candidate.Question)).ToList(),
                settings.QuestionCount - selected.Count));
        }

        return selected
            .OrderBy(candidate => GetTypeOrder(candidate.Question.Type))
            .Select(candidate => CloneQuestion(candidate.Question))
            .ToList();

        void SelectType(QuestionType type, int count)
            => Select(question => question.Type == type, count);

        void Select(Func<Question, bool> matches, int count)
        {
            selected.AddRange(SelectCandidates(
                candidates.Where(candidate => matches(candidate.Question)).ToList(),
                count));
        }
    }

    private static double GetChapterWeight(ExamSettings settings, string? chapterName)
    {
        if (chapterName is not null &&
            settings.ChapterWeights?.TryGetValue(chapterName, out var weight) is true)
        {
            return Math.Max(0d, weight);
        }

        return 0d;
    }

    private static List<QuestionCandidate> SelectCandidates(
        IReadOnlyCollection<QuestionCandidate> candidates,
        int count)
    {
        if (count <= 0)
            return [];

        var chapterCandidateCounts = candidates
            .GroupBy(candidate => candidate.ChapterName)
            .ToDictionary(group => group.Key, group => group.Count());
        var positiveWeightCandidates = candidates
            .Where(candidate => candidate.ChapterWeight > 0d)
            .OrderByDescending(candidate => CreateWeightedRandomKey(
                candidate.ChapterWeight / chapterCandidateCounts[candidate.ChapterName]))
            .Take(count)
            .ToList();

        if (positiveWeightCandidates.Count >= count)
            return positiveWeightCandidates;

        var selectedQuestions = positiveWeightCandidates
            .Select(candidate => candidate.Question)
            .ToHashSet<Question>(ReferenceEqualityComparer.Instance);
        var supplements = candidates
            .Where(candidate => !selectedQuestions.Contains(candidate.Question))
            .OrderBy(_ => Random.Shared.Next())
            .Take(count - positiveWeightCandidates.Count);

        positiveWeightCandidates.AddRange(supplements);
        return positiveWeightCandidates;
    }

    private static double CreateWeightedRandomKey(double weight)
    {
        var random = Math.Max(double.Epsilon, Random.Shared.NextDouble());
        return Math.Pow(random, 1d / weight);
    }

    private static Question CloneQuestion(Question question)
    {
        return new Question
        {
            Text = question.Text,
            ReviewIdentity = question.ReviewIdentity,
            RequiresAiGrading = question.RequiresAiGrading,
            AnswerNature = question.AnswerNature,
            AnswerParts = question.AnswerParts.Select(p => new QuestionPart { Key = p.Key, Prompt = p.Prompt,
                ReferenceAnswer = p.ReferenceAnswer, Weight = p.Weight, AnswerNature = p.AnswerNature }).ToList(),
            SourceExcerpts = question.SourceExcerpts.ToList(),
            Type = question.Type,
            Options = question.Options
                .Select(option => new ReciteHelper.Core.ValueObjects.QuestionOption
                {
                    Id = option.Id,
                    Text = option.Text
                })
                .ToList(),
            CorrectOptionIds = question.CorrectOptionIds.ToList(),
            CorrectAnswers = question.CorrectAnswers.ToList(),
            CorrectAnswer = question.CorrectAnswer,
        };
    }

    private static int GetTypeOrder(QuestionType type)
    {
        return type switch
        {
            QuestionType.SingleChoice => 0,
            QuestionType.FillBlank => 1,
            QuestionType.TrueFalse => 2,
            QuestionType.TermDefinition => 3,
            QuestionType.Essay => 4,
            _ => (int)type
        };
    }

    private sealed record QuestionCandidate(
        Question Question,
        string ChapterName,
        double ChapterWeight);
}
