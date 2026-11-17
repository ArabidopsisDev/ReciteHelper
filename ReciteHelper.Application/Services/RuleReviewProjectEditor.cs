using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Application.Services;

/// <summary>Appends an accepted batch while retaining existing objects and their learning records.</summary>
public sealed class RuleReviewProjectEditor(IProjectFileService files)
{
    public async Task<Chapter> AppendAsync(Project project, Chapter? target, Chapter generated)
    {
        var previousChapters = project.Chapters;
        var previousQuestions = target?.Questions;
        var previousPrompts = target?.AdditionalGenerationPrompts;
        var previousMaterials = target?.ReviewMaterials;
        var saved = target ?? generated;
        if (target is null)
        {
            generated.Number = (project.Chapters?.Select(chapter => chapter.Number).DefaultIfEmpty(0).Max() ?? 0) + 1;
            project.Chapters = [.. project.Chapters ?? [], generated];
        }
        else
        {
            target.Questions = [.. target.Questions ?? [], .. generated.Questions!];
            target.AdditionalGenerationPrompts = [.. target.AdditionalGenerationPrompts, generated.GenerationPrompt!];
            target.ReviewMaterials = generated.ReviewMaterials.ToList();
        }
        try { await files.SaveProjectAsync(project); }
        catch
        {
            project.Chapters = previousChapters;
            if (target is not null)
            {
                target.Questions = previousQuestions;
                target.AdditionalGenerationPrompts = previousPrompts!;
                target.ReviewMaterials = previousMaterials!;
            }
            throw;
        }
        return saved;
    }
}
