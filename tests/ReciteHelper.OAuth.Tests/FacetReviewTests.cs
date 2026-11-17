using System.Text.Json;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class FacetReviewTests
{
    [Fact]
    public async Task PartialAnswerProducesOnlyTheWeakSliceInBothReviewModes()
    {
        var (project, parent) = CreateProject();
        parent.SourceExcerpts = ["课程资料中的病症与循环要点"];
        var scheduler = new FsrsReviewScheduler();
        var quiz = Quiz(scheduler, _ => Partial());
        var result = await quiz.ProcessAnswerAsync(project, parent, "病症正确，循环遗漏。", DateTime.Now.AddSeconds(-20));
        Assert.Equal(2, parent.ReviewFacets.Count);
        Assert.Equal(0.4, result.FacetUpdate!.MasteryAfter, 8);
        var cycle = parent.ReviewFacets.Single(f => f.Key == "cycle");
        Assert.Equal(parent.SourceExcerpts, cycle.Question.SourceExcerpts);
        var generator = new ReviewGenerator(scheduler);
        Assert.Empty(generator.GenerateReview(project, 100));
        Assert.Equal(10, (cycle.ReviewAvailableAt!.Value - cycle.Question.Memory!.LastReview).TotalMinutes, 6);
        cycle.ReviewAvailableAt = DateTime.Now.AddSeconds(-1);
        var regular = generator.GenerateReview(project, 100);
        Assert.Same(cycle.Question, Assert.Single(regular));
        var parameterized = generator.GenerateParameterizationReview(project, new ReviewOptions { TotalCount = 100, NewQuestionRatio = 0.5, PrioritizeWeakPoints = true });
        Assert.Same(cycle.Question, Assert.Single(parameterized));
        Assert.DoesNotContain(parent, regular);
        Assert.DoesNotContain(parent, parameterized);
        Assert.Contains("病害循环", cycle.Question.Text);
        Assert.DoesNotContain("病症与", cycle.Question.Text);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.6)]
    [InlineData(0.9)]
    public async Task FocusedSuccessChangesOnlyItsWeightedShare(double weight)
    {
        var (project, parent) = CreateProject();
        var scheduler = new FsrsReviewScheduler();
        var quiz = Quiz(scheduler, q => q.IsReviewFacet ? Complete() : Partial(weight));
        await quiz.ProcessAnswerAsync(project, parent, "原始作答", DateTime.Now);
        var symptom = parent.ReviewFacets.Single(f => f.Key == "symptoms");
        var cycle = parent.ReviewFacets.Single(f => f.Key == "cycle");
        symptom.Question.Memory!.LastReview = DateTime.Now.AddDays(-3);
        var sibling = JsonSerializer.Serialize(symptom.Question);
        var parentMemory = JsonSerializer.Serialize(parent.Memory);
        var parentAttempts = parent.ReviewTag.Count;
        var parentLastAssessment = parent.LastAiAssessment;
        var result = await quiz.ProcessAnswerAsync(project, cycle.Question, "完整循环", DateTime.Now);
        Assert.Equal(weight, result.FacetUpdate!.MasteryAfter - result.FacetUpdate.MasteryBefore, 8);
        Assert.Equal(sibling, JsonSerializer.Serialize(symptom.Question));
        Assert.Equal(parentMemory, JsonSerializer.Serialize(parent.Memory));
        Assert.Equal(parentAttempts, parent.ReviewTag.Count);
        Assert.Same(parentLastAssessment, parent.LastAiAssessment);
        Assert.False(parent.Status); // Last full attempt remains a failed attempt.
        Assert.False(cycle.NeedsReview);
        Assert.Equal(result.FacetUpdate.MasteryAfter,
            FacetMemoryProjection.Mastery(parent, DateTime.Now, SchedulerParameters.Default), 8);
        Assert.Equal(ReviewGrade.Good, result.Outcome.Grade);
        Assert.True(result.FacetUpdate.ParentNextIntervalDays >= 0);
    }

    [Fact]
    public async Task PartialSliceRemainsAnAgainAndDoesNotBecomeAHardPass()
    {
        var (project, parent) = CreateProject();
        var scheduler = new FsrsReviewScheduler();
        var quiz = Quiz(scheduler, q => q.IsReviewFacet ? new() { ScoreRatio = 0.5 } : Partial());
        await quiz.ProcessAnswerAsync(project, parent, "原始作答", DateTime.Now);
        var cycle = parent.ReviewFacets.Single(f => f.Key == "cycle");
        var result = await quiz.ProcessAnswerAsync(project, cycle.Question, "只答对一半循环", DateTime.Now);
        Assert.Equal(ReviewGrade.Again, result.Outcome.Grade);
        Assert.True(cycle.NeedsReview);
        Assert.Equal(0.7, result.FacetUpdate!.MasteryAfter, 8);
    }

    [Fact]
    public void AllClustersContributeWeakSlicesAndParentsNeverTopUpThePool()
    {
        var (project, first) = CreateProject();
        var second = new Question { Text = "另一病害的病症与循环。", RequiresAiGrading = true };
        project.Chapters!.Add(new() { Name = "真菌", Questions = [second] });
        var scheduler = new FsrsReviewScheduler();
        var service = new FacetReviewService(scheduler);
        service.RecordAssessment(project, first, Partial(), DateTime.Now.AddMinutes(-11));
        service.RecordAssessment(project, second, Partial(), DateTime.Now.AddMinutes(-11));
        var result = new ReviewGenerator(scheduler).GenerateReview(project, 100);
        Assert.Equal(2, result.Count);
        Assert.All(result, q => Assert.True(q.IsReviewFacet));
        Assert.Contains(result, q => ReferenceEquals(q, first.ReviewFacets.Single(f => f.NeedsReview).Question));
        Assert.Contains(result, q => ReferenceEquals(q, second.ReviewFacets.Single(f => f.NeedsReview).Question));
    }

    [Fact]
    public void LargerMissingCoverageTakesPriorityAcrossClusters()
    {
        var (project, first) = CreateProject();
        var second = new Question { Text = "另一病害的病症与循环。", RequiresAiGrading = true };
        project.Chapters!.Add(new() { Name = "另一聚类", Questions = [second] });
        var scheduler = new FsrsReviewScheduler();
        var service = new FacetReviewService(scheduler);
        service.RecordAssessment(project, first, Partial(0.2), DateTime.Now.AddMinutes(-11));
        service.RecordAssessment(project, second, Partial(0.8), DateTime.Now.AddMinutes(-11));
        var selected = Assert.Single(new ReviewGenerator(scheduler).GenerateReview(project, 1));
        Assert.Same(second.ReviewFacets.Single(f => f.Key == "cycle").Question, selected);
    }

    [Fact]
    public async Task ResolvedSlicesWaitUntilDueWhileAParentStaysExcluded()
    {
        var (project, parent) = CreateProject();
        var scheduler = new FsrsReviewScheduler();
        var quiz = Quiz(scheduler, q => q.IsReviewFacet ? Complete() : Partial());
        await quiz.ProcessAnswerAsync(project, parent, "原始作答", DateTime.Now);
        var cycle = parent.ReviewFacets.Single(f => f.Key == "cycle");
        await quiz.ProcessAnswerAsync(project, cycle.Question, "完整循环", DateTime.Now);
        var generator = new ReviewGenerator(scheduler);
        Assert.Empty(generator.GenerateReview(project, 100));
        var future = generator.Rank(project, DateTime.Now.AddDays(100));
        Assert.Equal(2, future.Count);
        Assert.All(future, row => Assert.True(row.Question.IsReviewFacet));
    }

    [Fact]
    public void FullReassessmentReusesFrozenRubricAndUpdatesAllObservedParts()
    {
        var (project, parent) = CreateProject();
        var service = new FacetReviewService(new FsrsReviewScheduler());
        service.RecordAssessment(project, parent, Partial(), DateTime.Now.AddDays(-2));
        var ids = parent.ReviewFacets.Select(f => f.Question.ReviewFacetId).ToList();
        var complete = Partial(cycleScore: 1);
        complete.Facets.Reverse();
        service.RecordAssessment(project, parent, complete, DateTime.Now);
        Assert.Equal(ids, parent.ReviewFacets.Select(f => f.Question.ReviewFacetId));
        Assert.All(parent.ReviewFacets, f => { Assert.False(f.NeedsReview); Assert.Equal(2, f.Question.Memory!.Reps); });
        Assert.Equal(1, FacetMemoryProjection.Mastery(parent, DateTime.Now, SchedulerParameters.Default), 8);
    }

    [Fact]
    public void MalformedRubricCannotMutateScheduling()
    {
        Action<AiAnswerAssessment>[] corruptions = [
            a => a.Facets[0].Weight = -1,
            a => a.Facets[0].Weight = double.NaN,
            a => a.Facets[1].Key = a.Facets[0].Key,
            a => a.Facets[1].Question = a.Facets[0].Question,
            a => a.Facets[0].Question = "小麦叶斑病的病症与病害循环。",
            a => a.ScoreRatio = 0.9,
            a => a.Facets[0].ScoreRatio = 0.5,
            a => a.Facets[0].ReferenceAnswer = "" ];
        foreach (var corrupt in corruptions)
        {
            var (project, parent) = CreateProject();
            var assessment = Partial();
            corrupt(assessment);
            Assert.Throws<InvalidDataException>(() => new FacetReviewService(new FsrsReviewScheduler())
                .RecordAssessment(project, parent, assessment, DateTime.Now));
            Assert.Empty(parent.ReviewFacets);
            Assert.Null(parent.Memory);
            Assert.Empty(parent.ReviewTag);
        }
    }

    [Fact]
    public void ChangedRubricWeightsAreRejectedWithoutChangingExistingMemories()
    {
        var (project, parent) = CreateProject();
        var service = new FacetReviewService(new FsrsReviewScheduler());
        service.RecordAssessment(project, parent, Partial(), DateTime.Now);
        var before = JsonSerializer.Serialize(parent);
        Assert.Throws<InvalidDataException>(() => service.RecordAssessment(project, parent, Partial(0.8), DateTime.Now));
        Assert.Equal(before, JsonSerializer.Serialize(parent));
    }

    [Fact]
    public async Task PartialAiResponseMustContainACompleteRubric()
    {
        var raw = JsonSerializer.Serialize(new AiAnswerAssessment { ScoreRatio = 0.4,
            Annotations = [new() { Text = "原文", Status = "partial" }] });
        await Assert.ThrowsAsync<InvalidDataException>(() => new AiAnswerGrader(new Chat(raw))
            .GradeAsync(new Question { Text = "小麦叶斑病的病症与病害循环。" }, "原文"));
    }

    [Fact]
    public async Task GradingASliceDoesNotCreateFurtherSlices()
    {
        var assessment = Partial();
        assessment.Annotations = [new() { Text = "原文", Status = "partial" }];
        await Assert.ThrowsAsync<InvalidDataException>(() => new AiAnswerGrader(new Chat(JsonSerializer.Serialize(assessment)))
            .GradeAsync(new Question { ReviewFacetId = Guid.NewGuid() }, "原文"));
    }

    [Fact]
    public void CombinedCurveHasTheCorrectInverseAndAccountsForCoverage()
    {
        var (project, parent) = CreateProject();
        var now = DateTime.Now;
        new FacetReviewService(new FsrsReviewScheduler()).RecordAssessment(project, parent, Partial(), now);
        foreach (var facet in parent.ReviewFacets) facet.Question.Memory!.Stability = 10;
        Assert.Equal(10, FacetMemoryProjection.IntervalForRetention(parent, now, 0.9, SchedulerParameters.Default), 7);
        Assert.Equal(7, FacetMemoryProjection.IntervalForRetention(parent, now.AddDays(3), 0.9, SchedulerParameters.Default), 7);
        var weak = parent.ReviewFacets.Single(f => f.Key == "cycle");
        var strong = parent.ReviewFacets.Single(f => f.Key == "symptoms");
        weak.Question.Memory!.Stability = 1;
        weak.Weight = 0.2; strong.Weight = 0.8;
        var smallWeakPart = FacetMemoryProjection.IntervalForRetention(parent, now, 0.9, SchedulerParameters.Default);
        weak.Weight = 0.8; strong.Weight = 0.2;
        var largeWeakPart = FacetMemoryProjection.IntervalForRetention(parent, now, 0.9, SchedulerParameters.Default);
        Assert.True(largeWeakPart < smallWeakPart);
        Assert.InRange(largeWeakPart, 1, 10);
    }

    [Fact]
    public void WholeAttemptIsOneWeightedTrainingExampleAndARealSliceAttemptIsOneMore()
    {
        var (project, parent) = CreateProject();
        var scheduler = new FsrsReviewScheduler();
        var service = new FacetReviewService(scheduler);
        service.RecordAssessment(project, parent, Partial(), DateTime.Now.AddDays(-4));
        service.RecordAssessment(project, parent, Partial(), DateTime.Now.AddDays(-2));
        parent.ReviewTag = [new() { Time = DateTime.Now.AddDays(-4), Grade = 1 }, new() { Time = DateTime.Now, Grade = 3 }];
        var sequences = FsrsOptimizer.ExtractSequences(project);
        Assert.Equal(2, sequences.Count); // The composite is excluded from fitting.
        Assert.Equal(1, FsrsOptimizer.CountExamples(sequences));
        var cycle = parent.ReviewFacets.Single(f => f.Key == "cycle").Question;
        cycle.ReviewTag.Add(new() { Time = DateTime.Now, Grade = 3, OptimizerWeight = 1 });
        Assert.Equal(2, FsrsOptimizer.CountExamples(FsrsOptimizer.ExtractSequences(project)));
    }

    [Fact]
    public void WeightedOptimizerLossAndGradientMatchTheWeightedObservations()
    {
        var optimizer = new FsrsOptimizer(gamma: 0);
        var success = new ReviewSequence([(0, ReviewGrade.Good), (4, ReviewGrade.Good)]);
        var failure = new ReviewSequence([(0, ReviewGrade.Good), (4, ReviewGrade.Again)]);
        var weighted = optimizer.Evaluate([success with { ObservationWeights = [1, 0.4] }, failure with { ObservationWeights = [1, 0.6] }], SchedulerParameters.Default.Weights);
        var first = optimizer.Evaluate([success], SchedulerParameters.Default.Weights);
        var second = optimizer.Evaluate([failure], SchedulerParameters.Default.Weights);
        Assert.Equal(0.4 * first.Loss + 0.6 * second.Loss, weighted.Loss, 10);
        for (var index = 0; index < weighted.Gradient.Length; index++)
            Assert.Equal(0.4 * first.Gradient[index] + 0.6 * second.Gradient[index], weighted.Gradient[index], 9);
    }

    [Fact]
    public void TrainingRetainsWholeQuestionHistoryFromBeforeDecomposition()
    {
        var (project, parent) = CreateProject();
        var now = DateTime.Now;
        parent.ReviewTag = [new() { Time = now.AddDays(-10), Grade = 3 }, new() { Time = now.AddDays(-7), Grade = 1 }];
        var service = new FacetReviewService(new FsrsReviewScheduler());
        service.RecordAssessment(project, parent, Partial(), now.AddDays(-4));
        service.RecordAssessment(project, parent, Partial(), now.AddDays(-2));
        parent.ReviewTag.Add(new() { Time = now.AddDays(-4), Grade = 1 });
        var sequences = FsrsOptimizer.ExtractSequences(project);
        Assert.Equal(3, sequences.Count);
        Assert.Equal(2, FsrsOptimizer.CountExamples(sequences));
    }

    [Fact]
    public void KnownWholeSuccessesWarmStartComponentsWithoutDuplicatingTrainingLabels()
    {
        var (project, parent) = CreateProject();
        var now = DateTime.Now;
        parent.ReviewTag = [new() { Time = now.AddDays(-10), Grade = 3, IsCorrect = true },
            new() { Time = now.AddDays(-5), Grade = 3, IsCorrect = true },
            new() { Time = now.AddDays(-2), Grade = 1, IsCorrect = false }];
        new FacetReviewService(new FsrsReviewScheduler()).RecordAssessment(project, parent, Partial(), now);
        var symptom = parent.ReviewFacets.Single(f => f.Key == "symptoms").Question;
        Assert.Equal(3, symptom.Memory!.Reps);
        Assert.True(symptom.Memory.Stability > FsrsAlgorithm.InitialState(ReviewGrade.Good, SchedulerParameters.Default).Stability);
        Assert.All(symptom.ReviewTag.Take(2), tag => Assert.Equal(0, tag.OptimizerWeight));
        Assert.DoesNotContain(symptom.ReviewTag, tag => tag.Time == now.AddDays(-2));
        // Two predictable original observations plus one weighted full diagnostic.
        Assert.Equal(3, FsrsOptimizer.CountExamples(FsrsOptimizer.ExtractSequences(project)));
    }

    [Fact]
    public void ParameterRefitRebuildsComponentHistories()
    {
        var (project, parent) = CreateProject();
        var service = new FacetReviewService(new FsrsReviewScheduler());
        service.RecordAssessment(project, parent, Partial(), DateTime.Now.AddDays(-3));
        service.RecordAssessment(project, parent, Partial(cycleScore: 1), DateTime.Now);
        var expected = parent.ReviewFacets.Select(f => f.Question.Memory!.Stability).ToList();
        foreach (var facet in parent.ReviewFacets) facet.Question.Memory!.Stability = 10000;
        ReviewPersonalizationService.RebuildMemoryRecords(project);
        Assert.Equal(expected, parent.ReviewFacets.Select(f => f.Question.Memory!.Stability));
    }

    [Fact]
    public async Task ProjectSaveAndReloadKeepsIndependentSliceStatesAndLinks()
    {
        var (project, parent) = CreateProject();
        new FacetReviewService(new FsrsReviewScheduler()).RecordAssessment(project, parent, Partial(), DateTime.Now);
        var root = Path.Combine(Path.GetTempPath(), "FacetReviewTests", Guid.NewGuid().ToString("N"));
        project.StoragePath = root;
        try
        {
            var files = new ProjectFileService();
            await files.SaveProjectAsync(project);
            var restored = (await files.OpenProjectAsync(Path.Combine(root, project.ProjectName!, project.ProjectName + ".rhproj")))!;
            var restoredParent = restored.Chapters![0].Questions![0];
            Assert.NotNull(restoredParent.ReviewFacets.Single(f => f.Key == "cycle").ReviewAvailableAt);
            restoredParent.ReviewFacets.Single(f => f.Key == "cycle").ReviewAvailableAt = DateTime.Now.AddSeconds(-1);
            Assert.Equal(parent.ReviewIdentity, restoredParent.ReviewIdentity);
            var restoredCycle = restoredParent.ReviewFacets.Single(f => f.Key == "cycle").Question;
            Assert.Same(restoredParent, ReviewQuestionCatalog.FindFacet(restored, restoredCycle)!.Value.Parent);
            Assert.Equal(parent.ReviewFacets.Select(f => f.Question.ReviewFacetId), restoredParent.ReviewFacets.Select(f => f.Question.ReviewFacetId));
            Assert.Same(restoredCycle, Assert.Single(new ReviewGenerator(new FsrsReviewScheduler()).GenerateReview(restored, 100)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AiExamGradingCreatesTheSameReviewSlices()
    {
        var (project, parent) = CreateProject();
        var service = new ExamAnswerService(new Grader(_ => Partial()), new FacetReviewService(new FsrsReviewScheduler()));
        await service.GradeForReviewAsync(project, parent, "考试原文", DateTime.Now);
        Assert.Equal(2, parent.ReviewFacets.Count);
        Assert.True(parent.ReviewFacets.Single(f => f.Key == "cycle").NeedsReview);
        Assert.Equal(1, parent.Memory!.Reps);
        Assert.Single(parent.ReviewTag);
    }

    [Fact]
    public async Task ExamCloneMapsBackToTheRightOriginalEvenWhenNumericIdsCollide()
    {
        var (project, parent) = CreateProject();
        parent.SetId(1);
        var other = new Question { Text = parent.Text, RequiresAiGrading = true };
        other.SetId(1);
        project.Chapters!.Add(new() { Name = "另一聚类", Questions = [other] });
        var settings = ReciteHelper.Core.ValueObjects.ExamSettings.Create("EX", 30, 2, 5, null);
        var clones = new ExamPaperService().Generate(project, settings);
        var clone = clones.Single(q => q.ReviewIdentity == parent.ReviewIdentity);
        Assert.NotSame(parent, clone);
        Assert.True(clone.RequiresAiGrading);
        await new ExamAnswerService(new Grader(_ => Partial()), new FacetReviewService(new FsrsReviewScheduler()))
            .GradeForReviewAsync(project, clone, "考试答案", DateTime.Now);
        Assert.Equal(2, parent.ReviewFacets.Count);
        Assert.Empty(other.ReviewFacets);
        Assert.NotNull(parent.LastAiAssessment);
    }

    private static (Project, Question) CreateProject()
    {
        var question = new Question { Text = "小麦叶斑病的病症与病害循环。", CorrectAnswer = "病症参考。病害循环参考。", RequiresAiGrading = true };
        return (new Project { TemplateType = ProjectTemplateType.RuleBasedReview, ProjectName = "切面复习测试",
            Chapters = [new() { Name = "细菌", Questions = [question] }] }, question);
    }

    private static AiAnswerAssessment Partial(double cycleWeight = 0.6, double cycleScore = 0)
        => new() { ScoreRatio = 1 - cycleWeight + cycleWeight * cycleScore, IsCorrect = cycleScore == 1,
            Facets = [new() { Key = "symptoms", Name = "病症", Weight = 1 - cycleWeight, WeightReason = "病症对应的得分要点",
                ScoreRatio = 1, IsCorrect = true, Question = "说明小麦叶斑病的病症。", ReferenceAnswer = "病症的完整参考答案。" },
                new() { Key = "cycle", Name = "病害循环", Weight = cycleWeight, WeightReason = "循环对应的得分要点",
                ScoreRatio = cycleScore, IsCorrect = cycleScore == 1, Question = "说明小麦叶斑病的病害循环。", ReferenceAnswer = "病害循环的完整参考答案。" }] };

    private static AiAnswerAssessment Complete() => new() { ScoreRatio = 1, IsCorrect = true };
    private static QuizService Quiz(IReviewScheduler scheduler, Func<Question, AiAnswerAssessment> answer)
        => new(new Configuration(), new ForbiddenJudge(), scheduler, new Grader(answer));
    private sealed class Configuration : IConfigService
    {
        public Task<ConfigOptions> LoadAsync() => Task.FromResult(new ConfigOptions());
        public Task SaveAsync(ConfigOptions config) => Task.CompletedTask;
    }
    private sealed class Grader(Func<Question, AiAnswerAssessment> answer) : IAiAnswerGrader
    {
        public Task<AiAnswerAssessment> GradeAsync(Question question, string userAnswer)
        {
            var assessment = answer(question); assessment.UserAnswer = userAnswer;
            assessment.Annotations = [new() { Text = userAnswer, Status = assessment.IsCorrect ? "correct" : "partial" }];
            return Task.FromResult(assessment);
        }
    }
    private sealed class Chat(string response) : IAiChatService
    {
        public Task<string> RunAsync(string key, string prompt, string? instructions = null) => Task.FromResult(response);
    }
    private sealed class ForbiddenJudge : IAnswerJudge
    {
        public Task<bool> JudgeAsync(string? answer, string? reference) => throw new InvalidOperationException();
        public Task<double> CalculateSimilarityAsync(string answer, string reference) => throw new InvalidOperationException();
    }
}
