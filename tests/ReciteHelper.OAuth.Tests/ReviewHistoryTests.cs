using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class ReviewHistoryTests
{
    [Fact]
    public async Task ClearingChapterRemovesPersistedMemoryAndRestoresFreshRootQuestions()
    {
        var root = LearnedQuestion();
        root.Text = "说明病症与循环。";
        root.CorrectAnswer = "课程参考答案";
        root.SourceExcerpts = ["课程原文"];
        root.AnswerParts = [new() { Key = "symptoms", Prompt = "病症", ReferenceAnswer = "病症要点", Weight = 0.4 }];
        root.ReviewFacets = [new() { Key = "symptoms", Weight = 1, LastScoreRatio = 1,
            Question = LearnedQuestion() }];
        root.FacetsCreatedAt = DateTime.Now;
        var directory = Path.Combine(Path.GetTempPath(), "ReciteHelperResetTests", Guid.NewGuid().ToString("N"));
        var project = new Project { ProjectName = "测试项目", StoragePath = directory,
            Chapters = [new() { Name = "病害", Questions = [root], ReviewMaterials = [new("讲义", "课程原文")] }] };
        try
        {
            ReviewHistory.Clear(project, project.Chapters[0].Questions!);
            var files = new ProjectFileService();
            await files.SaveProjectAsync(project);
            var reopened = (await files.OpenProjectAsync(Path.Combine(directory, "测试项目", "测试项目.rhproj")))!;
            var question = reopened.Chapters![0].Questions![0];
            Assert.Null(question.Status);
            Assert.Null(question.Memory);
            Assert.Null(question.LastAiAssessment);
            Assert.Empty(question.ReviewTag);
            Assert.Empty(question.ReviewFacets);
            Assert.Null(question.FacetsCreatedAt);
            Assert.Equal(2.5, question.EFValue);
            Assert.Equal("课程参考答案", question.CorrectAnswer);
            Assert.Equal("课程原文", Assert.Single(question.SourceExcerpts));
            Assert.Single(question.AnswerParts);
            Assert.Single(reopened.Chapters[0].ReviewMaterials);
            var scheduler = new FsrsReviewScheduler();
            Assert.Null(scheduler.PredictRecall(reopened, question, DateTime.Now));
            Assert.Empty(new ReviewGenerator(scheduler).GenerateReview(reopened, 20));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ClearingOneFacetRemovesItsMasteryWithoutResettingItsSibling()
    {
        var now = DateTime.Now;
        var selected = LearnedQuestion();
        selected.ReviewFacetId = Guid.NewGuid();
        var sibling = LearnedQuestion();
        var root = LearnedQuestion();
        root.ReviewFacets = [
            new() { Key = "symptoms", Weight = 0.4, LastScoreRatio = 1, NeedsReview = true,
                ReviewAvailableAt = now.AddMinutes(10), Question = selected },
            new() { Key = "cycle", Weight = 0.6, LastScoreRatio = 1, Question = sibling }];
        var project = new Project { Chapters = [new() { Questions = [root] }] };
        var siblingMemory = sibling.Memory;

        ReviewHistory.Clear(project, [selected]);

        Assert.Null(selected.Memory);
        Assert.Null(selected.Status);
        Assert.Empty(selected.ReviewTag);
        Assert.Null(selected.LastAiAssessment);
        Assert.Equal(0, root.ReviewFacets[0].LastScoreRatio);
        Assert.False(root.ReviewFacets[0].NeedsReview);
        Assert.Null(root.ReviewFacets[0].ReviewAvailableAt);
        Assert.Null(root.Status);
        Assert.Null(root.LastAiAssessment);
        Assert.Same(siblingMemory, sibling.Memory);
        Assert.Single(sibling.ReviewTag);
        Assert.Equal(0.6, FacetMemoryProjection.Mastery(root, now, SchedulerParameters.Default), 8);
    }

    private static Question LearnedQuestion() => new()
    {
        Status = true,
        RequiresAiGrading = true,
        EFValue = 3,
        Memory = MemoryRecord.From(new MemoryState(8, 4), DateTime.Now, 1, 0),
        ReviewTag = [new() { Grade = 3, Time = DateTime.Now, IsCorrect = true }],
        LastAiAssessment = new AiAnswerAssessment { ScoreRatio = 1, UserAnswer = "原答案" }
    };
}
