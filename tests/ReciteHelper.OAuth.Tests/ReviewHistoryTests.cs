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
    public void ClearingAnswersKeepsReviewAndExamHistoryAndMemory()
    {
        var question = LearnedQuestion();
        var facet = LearnedQuestion();
        question.SubmittedAnswer = "原答案";
        question.LastAiAssessment!.Annotations.Add(new() { Text = "原答案" });
        question.ReviewFacets = [new() { Question = facet, NeedsReview = true, LastScoreRatio = 0.5 }];
        question.ExamHistory = [new() { Verdict = "PC" }];
        var memory = question.Memory;
        var project = new Project { Chapters = [new() { Questions = [question] }] };
        var mistake = ReviewMistakes.Record(project, question, DateTime.Now, false, null, "原答案");

        ReviewHistory.Clear(project, [question], answersOnly: true);

        Assert.True(question.AnswerCleared);
        Assert.True(facet.AnswerCleared);
        Assert.Null(question.SubmittedAnswer);
        Assert.Empty(question.LastAiAssessment.UserAnswer);
        Assert.Empty(question.LastAiAssessment.Annotations);
        Assert.Equal(1, question.LastAiAssessment.ScoreRatio);
        Assert.True(question.Status);
        Assert.Equal(3, question.EFValue);
        Assert.Same(memory, question.Memory);
        Assert.Single(question.ReviewTag);
        Assert.Equal("PC", Assert.Single(question.ExamHistory).Verdict);
        Assert.True(Assert.Single(question.ReviewFacets).NeedsReview);
        var keptMistake = Assert.Single(project.ReviewMistakes);
        Assert.Empty(keptMistake.Answer!);
        Assert.Same(mistake!.Corrections, keptMistake.Corrections);
    }

    [Fact]
    public async Task ClearingMistakesKeepsReviewDataAndHidesEarlierPracticeAndImportedExamEntries()
    {
        var oldTime = DateTime.Now.AddMinutes(-5);
        var question = LearnedQuestion();
        question.ReviewTag[0].Time = oldTime;
        var assessment = question.LastAiAssessment!;
        assessment.UserAnswer = "正确的内容。写错的内容。";
        assessment.Annotations = [new() { Text = "正确的内容。", Status = "correct" },
            new() { Text = "写错的内容。", Status = "incorrect" }];
        assessment.Corrections = ["应当更正的内容。"];
        var directory = Path.Combine(Path.GetTempPath(), "ReciteHelperResetTests", Guid.NewGuid().ToString("N"));
        var project = new Project { ProjectName = "测试项目", StoragePath = directory,
            Chapters = [new() { Questions = [question] }] };
        var files = new ProjectFileService();
        var exams = new JsonExamSetRepository();
        try
        {
            var legacy = Assert.Single(ReviewMistakes.Read(project));
            var original = legacy.Answer!;
            Assert.Equal(new[] { "correct", "incorrect" }, original.Select(span => span.Status));
            Assert.Equal(assessment.UserAnswer, string.Concat(original.Select(span => span.Text)));
            var stored = ReviewMistakes.Record(project, question, oldTime, false, assessment, assessment.UserAnswer)!;
            Assert.Equal(original, stored.Answer);
            var exam = new ExamSet { Title = "期末试卷", Questions = [new() { Question = question, Score = 4 }],
                ReviewMistakes = [stored with { IsExam = true }] };
            await exams.SaveAsync(project, exam);

            await ReviewMistakes.ClearAsync(project, files);

            var reopened = (await files.OpenProjectAsync(Path.Combine(directory, "测试项目", "测试项目.rhproj")))!;
            Assert.Empty(ReviewMistakes.Read(reopened));
            Assert.Empty(Assert.Single(await exams.LoadAllAsync(reopened)).ReviewMistakes);
            var loaded = reopened.Chapters![0].Questions![0];
            Assert.Equal(assessment.UserAnswer, loaded.LastAiAssessment!.UserAnswer);
            Assert.Single(loaded.ReviewTag);
            Assert.NotNull(loaded.Memory);
            Assert.Null(ReviewMistakes.Record(reopened, loaded, oldTime, false, assessment, assessment.UserAnswer));
            var next = reopened.ReviewMistakesClearedAt!.Value.AddSeconds(1);
            ReviewMistakes.Record(reopened, loaded, next, false, assessment, assessment.UserAnswer);
            Assert.Equal(next, Assert.Single(ReviewMistakes.Read(reopened)).Time);
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
