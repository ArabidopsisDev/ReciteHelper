using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Services;

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
        var previousArchive = target?.PriorReviewMaterials;
        var linkedLegacyQuestions = new List<Question>();
        var saved = target ?? generated;
        if (target is null)
        {
            generated.Number = (project.Chapters?.Select(chapter => chapter.Number).DefaultIfEmpty(0).Max() ?? 0) + 1;
            project.Chapters = [.. project.Chapters ?? [], generated];
        }
        else
        {
            var sources = RuleReviewSources.ForChapter(target);
            foreach (var question in previousQuestions ?? [])
            {
                if (question.SourceMaterialIds is not null || question.SourceExcerpts.Count == 0) continue;
                question.SourceMaterialIds = sources.LinkExcerpts(question);
                linkedLegacyQuestions.Add(question);
            }
            target.Questions = [.. target.Questions ?? [], .. generated.Questions!];
            target.AdditionalGenerationPrompts = [.. target.AdditionalGenerationPrompts, generated.GenerationPrompt!];
            target.ReviewMaterials = generated.ReviewMaterials.ToList();
            var active = target.ReviewMaterials.Select(material => material.SourceId).ToHashSet(StringComparer.Ordinal);
            var retained = target.Questions.SelectMany(question => question.SourceMaterialIds ?? []).ToHashSet(StringComparer.Ordinal);
            var archive = (previousMaterials ?? []).Concat(previousArchive ?? [])
                .Where(material => retained.Contains(material.SourceId) && !active.Contains(material.SourceId))
                .DistinctBy(material => material.SourceId).ToList();
            target.PriorReviewMaterials = archive.Count == 0 ? null : archive;
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
                target.PriorReviewMaterials = previousArchive;
                foreach (var question in linkedLegacyQuestions)
                    question.SourceMaterialIds = null;
            }
            throw;
        }
        return saved;
    }
}
