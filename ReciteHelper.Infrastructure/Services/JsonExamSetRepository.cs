using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Interfaces.Services;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed class JsonExamSetRepository : IExamSetRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task SaveAsync(
        Project project,
        ExamSet examSet,
        CancellationToken cancellationToken = default)
    {
        var examsDirectory = GetExamsDirectory(project);
        Directory.CreateDirectory(examsDirectory);

        var fileName = $"{SanitizeFileName(examSet.Title)}_{examSet.Id[..Math.Min(8, examSet.Id.Length)]}.rhexam.json";
        var filePath = Path.Combine(examsDirectory, fileName);
        var temporaryPath = $"{filePath}.tmp";
        examSet.ReviewMistakes.RemoveAll(entry => !project.IncludesMistakeAt(entry.Time));
        var json = JsonSerializer.Serialize(examSet, JsonOptions);

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, filePath, true);
    }

    public Task<IReadOnlyList<ExamSet>> LoadAllAsync(
        Project project,
        CancellationToken cancellationToken = default)
        => LoadFromDirectoryAsync(project, GetExamsDirectory(project), cancellationToken);

    internal static async Task<IReadOnlyList<ExamSet>> LoadFromDirectoryAsync(
        Project project, string examsDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(examsDirectory))
            return [];

        var results = new List<ExamSet>();
        foreach (var filePath in Directory.EnumerateFiles(examsDirectory, "*.rhexam.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var examSet = await JsonSerializer.DeserializeAsync<ExamSet>(stream, JsonOptions, cancellationToken);
                if (examSet is { Questions.Count: > 0 })
                {
                    examSet.ReviewMistakes.RemoveAll(entry => !project.IncludesMistakeAt(entry.Time));
                    foreach (var question in examSet.Questions)
                    {
                        if (question.Score <= 0)
                            question.Score = question.Question.DefaultExamScore;
                        if (question.Question.IsFillBlank && question.Score < question.Question.BlankCount)
                            question.Score = question.Question.BlankCount;
                    }

                    results.Add(examSet);
                }
            }
            catch (JsonException)
            {
                // A damaged set must not prevent the remaining imported papers from loading.
            }
            catch (IOException)
            {
                // A temporarily unavailable file is omitted from this catalog refresh.
            }
        }

        return results
            .OrderByDescending(examSet => examSet.ImportedAtUtc)
            .ThenBy(examSet => examSet.Title)
            .ToList();
    }

    private static string GetExamsDirectory(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.StoragePath) || string.IsNullOrWhiteSpace(project.ProjectName))
            throw new ArgumentException("Project storage path or project name is missing.");

        return Path.Combine(project.StoragePath, project.ProjectName, "exams");
    }

    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "exam";

        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
    }
}
