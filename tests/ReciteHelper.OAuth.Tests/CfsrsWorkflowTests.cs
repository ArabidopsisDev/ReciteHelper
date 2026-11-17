using System.Text.Json;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.Services;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class CfsrsWorkflowTests
{
    [Fact]
    public async Task ThresholdAppliesPerPartAndAnOmittedPartCannotBeHiddenByTotalScore()
    {
        foreach (var (coverage, mastered) in new[] { (0.69, false), (0.7, true) })
        {
            var raw = new AiAnswerAssessment { ScoreRatio = coverage, Annotations = [new() { Text = "简述", Status = "correct" }] };
            var result = await new AiAnswerGrader(new Chat(JsonSerializer.Serialize(raw))).GradeAsync(new Question { Text = "说明病症。" }, "简述");
            Assert.Equal(mastered, result.IsCorrect);
            Assert.Equal(mastered ? 1 : coverage, result.ScoreRatio);
            Assert.Equal(coverage, result.RawScoreRatio);
        }
        var assessment = new AiAnswerAssessment { ScoreRatio = 0.8, Facets = [
            new() { Key = "a", Weight = 0.8, ScoreRatio = 1 }, new() { Key = "b", Weight = 0.2, ScoreRatio = 0 }] };
        AiGradingPolicy.Apply(assessment);
        Assert.False(assessment.IsCorrect);
        Assert.Equal(0.8, assessment.ScoreRatio);
        Assert.False(assessment.Facets[1].IsCorrect);
    }

    [Fact]
    public void MultipartAnswersAndAnnotationsKeepTheirOwnTextIncludingAnEmptyPart()
    {
        var answer = EssayAnswerLayout.Join(["圆形的橘红色夏孢子堆。", ""]);
        Assert.Equal(new[] { "圆形的橘红色夏孢子堆。", "" }, EssayAnswerLayout.Split(answer, 2));
        Assert.Equal(new[] { "圆形的橘红色夏孢子堆。", "" }, EssayAnswerLayout.Split(answer.Trim(), 2));
        var whole = new AiAnswerAssessment { UserAnswer = answer,
            Annotations = [new() { Text = answer, Status = "correct", Comment = "病症正确" }] };
        Assert.Equal("圆形的橘红色夏孢子堆。", string.Concat(EssayAnswerLayout.ForPart(whole, 0, 2).Annotations.Select(a => a.Text)));
        Assert.Empty(EssayAnswerLayout.ForPart(whole, 1, 2).Annotations);
        Assert.Equal(2, EssayAnswerLayout.Parts(new Question { Text = "简述小麦叶锈病的病症与病害循环。" }).Count);
        Assert.Equal(2, EssayAnswerLayout.Parts(new Question { Text = "简述栈的基本特点和主要操作。",
            AnswerParts = [new() { Key = "stack", Prompt = "基本特点和主要操作", ReferenceAnswer = "后进先出与栈顶操作。" }] }).Count);
        Assert.Equal(2, EssayAnswerLayout.Parts(new Question { Text = "栈的基本特点是什么？入栈和出栈操作分别在什么位置进行？",
            AnswerParts = [new() { Key = "stack", ReferenceAnswer = "后进先出与栈顶操作。" }] }).Count);
    }

    [Fact]
    public async Task SourceEvidenceIsSavedAndFabricatedQuotesAreRejected()
    {
        const string source = "圆形或近圆形橘红色夏孢子堆，主要发生在叶片正面。";
        var request = new RuleReviewCategory { Name = "病症", Prompt = "说明典型病症", Materials = [new("讲义", source)] };
        var questions = Enumerable.Range(1, 2).Select(i => new Question { Text = $"问题{i}", CorrectAnswer = source, SourceExcerpts = [source] }).ToList();
        var response = JsonSerializer.Serialize(new { questionPlan = new[] { "第一范围", "第二范围" }, questions });
        var chat = new Chat(response);
        var service = new RuleReviewService(chat);
        var chapter = await service.CompleteAsync(await service.PreviewAsync(request));
        Assert.Equal(source, Assert.Single(chapter.ReviewMaterials).Content);
        Assert.All(chapter.Questions!, q => Assert.Equal(source, Assert.Single(q.SourceExcerpts)));
        Assert.Equal(source, JsonDocument.Parse(chat.LastPrompt).RootElement.GetProperty("materials")[0].GetProperty("Content").GetString());
        questions[0].SourceExcerpts = ["资料中不存在的句子"];
        response = JsonSerializer.Serialize(new { questionPlan = new[] { "第一范围", "第二范围" }, questions });
        await Assert.ThrowsAsync<InvalidDataException>(() => new RuleReviewService(new Chat(response)).PreviewAsync(request));
    }

    [Fact]
    public void DueComponentsFromDifferentOriginalsAreInterleaved()
    {
        var now = DateTime.Now;
        var parent = new Question { ReviewFacets = [Facet("A1", 0.6), Facet("A2", 0.4)] };
        var other = new Question { ReviewFacets = [Facet("B1", 0.1), Facet("B2", 0.9)] };
        CfsrsPolicy.Observe(other.ReviewFacets[1], 1, true, now);
        other.ReviewFacets[1].Question.Memory = MemoryRecord.From(FsrsAlgorithm.InitialState(ReviewGrade.Good, SchedulerParameters.Default), now, 1, 0);
        var project = new Project { Chapters = [new() { Questions = [parent] }, new() { Questions = [other] }] };
        var ranked = new ReviewGenerator(new FsrsReviewScheduler()).Rank(project, now);
        Assert.Equal(2, ranked.Take(2).Select(r => r.ParentIdentity).Distinct().Count());
        Assert.All(ranked, r => Assert.NotSame(parent, r.Question));
        var component = parent.ReviewFacets[0];
        foreach (var facet in parent.ReviewFacets) CfsrsPolicy.Observe(facet, 0.5, false, now);
        Assert.False(CfsrsPolicy.IsAvailable(component, now.AddMinutes(9), 1, 0.9));
        Assert.True(CfsrsPolicy.IsAvailable(component, now.AddMinutes(10), 1, 0.9));
        Assert.Equal(10d / 1440, CfsrsPolicy.NextReviewInterval(parent, now, 0.9, SchedulerParameters.Default), 8);
        var atomic = new Question { RequiresAiGrading = true, LastAiAssessment = new() { ScoreRatio = 0.5 },
            Memory = MemoryRecord.From(FsrsAlgorithm.InitialState(ReviewGrade.Again, SchedulerParameters.Default), now, 1, 1) };
        project.Chapters!.Add(new() { Questions = [atomic] });
        Assert.DoesNotContain(new ReviewGenerator(new FsrsReviewScheduler()).Rank(project, now), row => ReferenceEquals(row.Question, atomic));
        Assert.Contains(new ReviewGenerator(new FsrsReviewScheduler()).Rank(project, now.AddMinutes(10)), row => ReferenceEquals(row.Question, atomic));
        atomic.LastAiAssessment.IsCorrect = true;
        Assert.DoesNotContain(new ReviewGenerator(new FsrsReviewScheduler()).Rank(project, now.AddMinutes(11)), row => ReferenceEquals(row.Question, atomic));
    }

    [Fact]
    public async Task RemainingBatchesRunWithBoundedConcurrencyAndKeepPlanOrder()
    {
        var chat = new DelayedChat();
        var preview = new RuleReviewPreview("测试", "课程主要知识", Enumerable.Range(0, 20).Select(i => $"范围{i}").ToList(),
            [new() { Text = "范围0", CorrectAnswer = "答案" }, new() { Text = "范围1", CorrectAnswer = "答案" }]);
        var completed = await new RuleReviewService(chat).CompleteAsync(preview);
        Assert.Equal(2, chat.MaxActive);
        Assert.Equal(preview.QuestionPlan, completed.Questions!.Select(q => q.Text));
    }

    private static ReviewFacet Facet(string text, double weight) => new() { Weight = weight, NeedsReview = true,
        ReviewAvailableAt = DateTime.Now.AddMinutes(-1), Question = new() { Text = text, ReviewFacetId = Guid.NewGuid() } };

    [Fact]
    public async Task AnnotationIdsPreserveTheUsersExactWords()
    {
        const string answer = "橙红色的孢子堆。";
        var assessment = new AiAnswerAssessment { ScoreRatio = 0.7,
            Annotations = [new() { SegmentId = 0, Text = "橘红色的孢子堆。", Status = "correct" }] };
        var result = await new AiAnswerGrader(new Chat(JsonSerializer.Serialize(assessment))).GradeAsync(new Question(), answer);
        Assert.Equal(answer, Assert.Single(result.Annotations).Text);
        Assert.True(result.IsCorrect);
    }

    private sealed class Chat(string response) : IAiChatService
    {
        public string LastPrompt { get; private set; } = string.Empty;
        public Task<string> RunAsync(string key, string prompt, string? instructions = null) { LastPrompt = prompt; return Task.FromResult(response); }
    }

    private sealed class DelayedChat : IAiChatService
    {
        private int _active;
        public int MaxActive { get; private set; }
        public async Task<string> RunAsync(string key, string prompt, string? instructions = null)
        {
            var active = Interlocked.Increment(ref _active);
            MaxActive = Math.Max(MaxActive, active);
            try
            {
                await Task.Delay(40);
                var plan = JsonDocument.Parse(prompt).RootElement.GetProperty("questionPlan").EnumerateArray().Select(p => p.GetString()).ToList();
                return JsonSerializer.Serialize(plan.Select(p => new Question { Text = p, CorrectAnswer = "答案" }));
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
