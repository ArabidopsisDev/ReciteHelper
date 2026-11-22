using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public class PracticeReviewTests
{
    [Fact]
    public async Task ArchivedRootCoversItsFacetsAndRestorePreservesExplicitFacetArchives()
    {
        var root = Path.Combine(Path.GetTempPath(), "PracticeArchive", Guid.NewGuid().ToString("N"));
        var correct = new Question { IsArchived = true, Status = true, SubmittedAnswer = "原作答",
            ReviewTag = [new() { IsCorrect = true, Time = DateTime.Now }] };
        var inherited = Written("随原题归档", 0.2);
        inherited.ReviewFacetId = Guid.NewGuid();
        var explicitArchive = Written("独立归档", 0.1);
        explicitArchive.ReviewFacetId = Guid.NewGuid();
        explicitArchive.IsArchived = true;
        correct.ReviewFacets = [
            new() { Name = "随原题归档", Weight = 0.5, Question = inherited, NeedsReview = true },
            new() { Name = "独立归档", Weight = 0.5, Question = explicitArchive, NeedsReview = true }
        ];
        var wrong = new Question { Status = false };
        var project = new Project { ProjectName = "复习", StoragePath = root, Chapters = [new() { Questions = [correct, wrong] }] };
        var generator = new ReviewGenerator(new FsrsReviewScheduler());
        var files = new ProjectFileService();
        try
        {
            await files.SaveProjectAsync(project);
            var path = Path.Combine(root, "复习", "复习.rhproj");
            var saved = (await files.OpenProjectAsync(path))!;
            var archived = Assert.Single(ReviewQuestionCatalog.ArchiveHeads(saved));
            Assert.Equal(correct.ReviewIdentity, archived.ReviewIdentity);
            Assert.Equal("原作答", archived.SubmittedAnswer);
            Assert.Single(archived.ReviewTag);
            Assert.Equal(3, ReviewQuestionCatalog.Archived(saved).Count());
            Assert.Empty(ReviewQuestionCatalog.ActiveFacets(archived));
            Assert.Single(generator.GenerateReview(saved, 10));

            archived.IsArchived = false;
            var individuallyArchived = Assert.Single(ReviewQuestionCatalog.Archived(saved));
            Assert.Equal(explicitArchive.ReviewIdentity, individuallyArchived.ReviewIdentity);
            var scheduled = generator.GenerateReview(saved, 10);
            Assert.Contains(scheduled, question => question.ReviewIdentity == inherited.ReviewIdentity);
            Assert.DoesNotContain(individuallyArchived, scheduled);
            Assert.False(CfsrsPolicy.IsAvailable(archived.ReviewFacets[1], DateTime.Now, 0, 0.9));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreparationUsesPreviouslyGradedWeakTopicsAndRejectsMultipleBlanks()
    {
        var weakest = Written("蒸发", 0.1);
        var archived = Written("冻结", 0.05);
        archived.IsArchived = true;
        archived.ReviewFacets = [new() { Question = Written("冰晶", 0.01), NeedsReview = true }];
        var excluded = Written("凝结", 0.01);
        var other = Written("升华", 0.2);
        var local = new Question
        {
            Text = "水的化学式是________。",
            Type = QuestionType.FillBlank, CorrectAnswers = ["H2O"],
            ExamHistory = [new() { Time = DateTime.Now, IsCorrect = false, ScoreRatio = 0.3 }]
        };
        var unattempted = new Question { Text = "未作答的问题", CorrectAnswer = "答案" };
        var project = new Project { Chapters = [new() { Questions = [weakest, archived, excluded, other, local, unattempted] }] };
        var chat = new QuickChat();
        var service = new QuickReviewService(new FsrsReviewScheduler(), chat);
        var prompts = new List<QuickReviewPrompt>();

        await foreach (var prompt in service.PrepareAsync(project, new HashSet<Guid> { excluded.ReviewIdentity }))
            prompts.Add(prompt);

        Assert.Equal(2, prompts.Count);
        Assert.Same(local, prompts[0].Topic);
        Assert.True(prompts[0].Accepts("Ｈ２Ｏ"));
        var generated = Assert.Single(prompts, prompt => prompt.Topic == weakest);
        Assert.Equal(0, generated.Priority);
        Assert.True(generated.Accepts("汽化"));
        Assert.Contains("蒸发", chat.Prompt);
        Assert.DoesNotContain("冻结", chat.Prompt);
        Assert.DoesNotContain("冰晶", chat.Prompt);
        Assert.DoesNotContain("凝结", chat.Prompt);
        weakest.IsArchived = true;
        Assert.False(generated.IsAvailable);
    }

    private static Question Written(string text, double score) => new()
    {
        Text = text, CorrectAnswer = $"关于{text}的参考答案", Status = false,
        ReviewTag = [new() { Time = DateTime.Now, ScoreRatio = score }]
    };

    private sealed class QuickChat : IAiChatService
    {
        public string Prompt { get; private set; } = string.Empty;

        public Task<string> RunAsync(string deepSeekKey, string prompt, string? instructions = null)
        {
            Prompt = prompt;
            return Task.FromResult("""
                {"items":[
                  {"source":0,"question":"水变成水蒸气的过程称为________。","choices":[],"answer":"蒸发","aliases":["汽化"]},
                  {"source":1,"question":"水有________形态和________形态。","choices":[],"answer":"液态"}
                ]}
                """);
        }
    }
}
