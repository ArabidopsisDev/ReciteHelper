using System.Text.Json;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class RuleReviewTests
{
    [Fact]
    public async Task PreviewRequestsOnlyTwoAndCompletionKeepsAcceptedQuestions()
    {
        var chat = new ChatStub(PreviewBatch("预览", 4), Batch("剩余", 2));
        var service = new RuleReviewService(chat);
        var category = new RuleReviewCategory { Name = "病原细菌", Prompt = "列出甲、乙两种病原体，分别考查寄主、传播途径和防治措施。" };
        var preview = await service.PreviewAsync(category);
        Assert.Single(chat.Prompts);
        Assert.Equal(2, preview.Questions.Count);
        Assert.Equal(2, JsonDocument.Parse(chat.Prompts[0]).RootElement.GetProperty("previewCount").GetInt32());
        Assert.Equal(4, preview.QuestionCount);
        Assert.False(JsonDocument.Parse(chat.Prompts[0]).RootElement.TryGetProperty("count", out _));
        category.Prompt = "后续修改";
        var chapter = await service.CompleteAsync(preview);
        Assert.Equal(4, chapter.Questions!.Count);
        Assert.Same(preview.Questions[0], chapter.Questions[0]);
        Assert.All(chapter.Questions, q => { Assert.True(q.RequiresAiGrading); Assert.Equal(QuestionType.ShortAnswer, q.Type); });
        Assert.Equal(preview.Prompt, chapter.GenerationPrompt);
        using var input = JsonDocument.Parse(chat.Prompts[1]);
        Assert.Equal(preview.Prompt, input.RootElement.GetProperty("requirements").GetString());
        Assert.Equal(2, input.RootElement.GetProperty("existingQuestions").GetArrayLength());
        Assert.Equal(preview.QuestionPlan[2], input.RootElement.GetProperty("questionPlan")[0].GetString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task AiPlanDeterminesQuestionCount(int plannedCount)
    {
        var chat = new ChatStub(PreviewBatch("预览", plannedCount), Batch("其余", Math.Max(0, plannedCount - 2)));
        var service = new RuleReviewService(chat);
        var preview = await service.PreviewAsync(new() { Name = "细菌", Prompt = "考查各病原体的寄主和传播途径" });
        Assert.Equal(plannedCount, preview.QuestionCount);
        var chapter = await service.CompleteAsync(preview);
        Assert.Equal(plannedCount, chapter.Questions!.Count);
        Assert.Equal(plannedCount <= 2 ? 1 : 2, chat.Prompts.Count);
    }

    [Fact]
    public async Task NumberedModelStemBecomesAnOverviewWithoutChangingTheThreeParts()
    {
        const string stem = "说明甘薯茎线虫病的症状、病害循环与防治措施。";
        const string response = """
        {
          "questionPlan": ["甘薯茎线虫病的症状、病害循环与防治措施"],
          "questions": [{
            "text": "（1）说明甘薯茎线虫病的症状。（2）说明甘薯茎线虫病的病害循环。（3）说明甘薯茎线虫病的防治措施。",
            "correct_answer": "课程评分要点",
            "answer_parts": [
              { "key": "signs", "prompt": "说明甘薯茎线虫病的症状。", "reference_answer": "症状要点", "weight": 0.3 },
              { "key": "cycle", "prompt": "说明甘薯茎线虫病的病害循环。", "reference_answer": "病害循环要点", "weight": 0.4 },
              { "key": "control", "prompt": "说明甘薯茎线虫病的防治措施。", "reference_answer": "防治措施要点", "weight": 0.3 }
            ]
          }]
        }
        """;
        var service = new RuleReviewService(new ChatStub(response));
        var preview = await service.PreviewAsync(new() { Name = "植物病理学", Prompt = "把症状、病害循环和防治措施三问合为一题。" });

        var question = Assert.Single(preview.Questions);
        Assert.Equal(stem, question.Text);
        Assert.Equal(new[] { "signs", "cycle", "control" }, question.AnswerParts.Select(part => part.Key));
        Assert.Equal(stem, Assert.Single((await service.CompleteAsync(preview)).Questions!).Text);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"重复范围\",\"重复范围\"]")]
    public async Task IncompleteOrDuplicatePlanIsRejected(string plan)
    {
        var response = "{\"questionPlan\":" + plan + ",\"questions\":" + Batch("题目", 2) + "}";
        await Assert.ThrowsAsync<InvalidDataException>(() => new RuleReviewService(new ChatStub(response))
            .PreviewAsync(new() { Name = "细菌", Prompt = "考查寄主和传播途径" }));
    }

    [Fact]
    public async Task RejectedPreviewCanBeReplacedBeforeCompletion()
    {
        var chat = new ChatStub(PreviewBatch("旧", 2), PreviewBatch("新", 2));
        var service = new RuleReviewService(chat);
        var category = new RuleReviewCategory { Name = "病原细菌", Prompt = "考查寄主" };
        var rejected = await service.PreviewAsync(category);
        category.Prompt = "考查传播途径和防治措施，并逐项提问。";
        var accepted = await service.PreviewAsync(category);
        var chapter = await service.CompleteAsync(accepted);
        Assert.Equal(2, chat.Prompts.Count);
        Assert.DoesNotContain(rejected.Questions[0], chapter.Questions!);
        Assert.Equal(category.Prompt, chapter.GenerationPrompt);
    }

    [Fact]
    public async Task FailedCompletionCanRetryWithTheSameAcceptedPreview()
    {
        var service = new RuleReviewService(new ChatStub(PreviewBatch("预览", 4), "[]", Batch("重试", 2)));
        var preview = await service.PreviewAsync(new() { Name = "细菌", Prompt = "考查所有列出的病原体的传播途径" });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CompleteAsync(preview));
        Assert.Equal(2, preview.Questions.Count);
        var completed = await service.CompleteAsync(preview);
        Assert.Equal(4, completed.Questions!.Count);
        Assert.Same(preview.Questions[0], completed.Questions[0]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"text\":\"问题\"},{\"text\":\"问题2\"}]")]
    [InlineData("[{\"text\":\"重复\",\"correct_answer\":\"答案\"},{\"text\":\"重复\",\"correct_answer\":\"答案\"}]")]
    public async Task InvalidGenerationIsRejected(string response)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => new RuleReviewService(new ChatStub(response))
            .PreviewAsync(new() { Name = "细菌", Prompt = "考查所有病原体的传播途径" }));
    }

    [Fact]
    public async Task CancellationStopsBeforeSendingARequest()
    {
        var chat = new ChatStub();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuleReviewService(chat)
            .PreviewAsync(new() { Name = "细菌", Prompt = "考查寄主" }, new CancellationToken(true)));
        Assert.Empty(chat.Prompts);
    }

    [Fact]
    public async Task AiGradingRetainsExactAnswerAndThreeAnnotationTypes()
    {
        var assessment = new AiAnswerAssessment
        {
            ScoreRatio = 0.6, Explanation = "请补充传播途径。", MissingPoints = ["防治措施"],
            Facets = [new() { Key = "known", Name = "已答对内容", Weight = 0.6, WeightReason = "对应六成得分要点",
                ScoreRatio = 1, IsCorrect = true, Question = "说明已答对部分。", ReferenceAnswer = "完整答案。" },
                new() { Key = "missing", Name = "防治措施", Weight = 0.4, WeightReason = "对应四成得分要点",
                ScoreRatio = 0, IsCorrect = false, Question = "说明防治措施。", ReferenceAnswer = "防治答案。" }],
            Annotations = [new() { Text = "正确\n", Status = "correct" }, new() { Text = "错误", Status = "incorrect" }, new() { Text = " 不全", Status = "partial" }]
        };
        var result = await new AiAnswerGrader(new ChatStub(JsonSerializer.Serialize(assessment))).GradeAsync(new Question(), "正确\n错误 不全");
        Assert.Equal("正确\n错误 不全", string.Concat(result.Annotations.Select(a => a.Text)));
        Assert.Equal("正确\n错误 不全", result.UserAnswer);
        Assert.Equal(0.6, result.ScoreRatio);
        Assert.Single(result.MissingPoints);
    }

    [Theory]
    [InlineData("改写后的文字", "correct", 0.5)]
    [InlineData("用户原文", "unknown", 0.5)]
    [InlineData("用户原文", "partial", 2)]
    public async Task InvalidAnnotationOrScoreDoesNotBecomeAResult(string text, string status, double score)
    {
        var response = JsonSerializer.Serialize(new AiAnswerAssessment { ScoreRatio = score, Annotations = [new() { Text = text, Status = status }] });
        await Assert.ThrowsAsync<InvalidDataException>(() => new AiAnswerGrader(new ChatStub(response)).GradeAsync(new Question(), "用户原文"));
    }

    [Fact]
    public async Task RuleQuizUsesOnlyAiAndUpdatesReviewHistory()
    {
        var grader = new GraderStub();
        var project = new Project { TemplateType = ProjectTemplateType.RuleBasedReview };
        var question = new Question { Text = "请说明传播途径", CorrectAnswer = "参考答案" };
        var quiz = new QuizService(new ConfigStub(), new ForbiddenLocalJudge(), new FsrsReviewScheduler(), grader);
        var result = await quiz.ProcessAnswerAsync(project, question, "我的回答", DateTime.Now.AddSeconds(-10));
        Assert.Equal(1, grader.Calls);
        Assert.Same(result.AiAssessment, question.LastAiAssessment);
        Assert.Single(question.ReviewTag);
        Assert.NotNull(question.Memory);
        Assert.Equal(0.55, result.Similarity);
        Assert.Equal(0.47, result.ReviewTag.ScoreRatio);
        Assert.False(result.IsCorrect);
    }

    [Fact]
    public async Task FailedAiGradingLeavesReviewHistoryUnchanged()
    {
        var question = new Question { RequiresAiGrading = true };
        var quiz = new QuizService(new ConfigStub(), new ForbiddenLocalJudge(), new FsrsReviewScheduler(), new GraderStub { Fail = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => quiz.ProcessAnswerAsync(new Project(), question, "答案", DateTime.Now));
        Assert.Empty(question.ReviewTag);
        Assert.Null(question.Memory);
        Assert.Null(question.LastAiAssessment);
    }

    [Fact]
    public async Task ClassicalReviewRetainsLocalGrading()
    {
        var grader = new GraderStub { Fail = true };
        var quiz = new QuizService(new ConfigStub(), new LocalJudge(), new FsrsReviewScheduler(), grader);
        var result = await quiz.ProcessAnswerAsync(new Project(), new Question { CorrectAnswer = "答案" }, "答案", DateTime.Now);
        Assert.True(result.IsCorrect);
        Assert.Null(result.AiAssessment);
        Assert.Equal(0, grader.Calls);
    }

    [Fact]
    public async Task ConfiguredModelGradesOrdinaryWrittenQuestionsInPracticeAndExam()
    {
        var config = new ConfigStub(new ConfigOptions { OpenRouterKey = "offline-test" });
        var grader = new GraderStub();
        var question = new Question { Type = QuestionType.Discussion, AnswerNature = AnswerNature.Expressive };
        var project = new Project();
        var quiz = new QuizService(config, new ForbiddenLocalJudge(), new FsrsReviewScheduler(), grader);
        var exam = new ExamAnswerService(grader, config);

        var practice = await quiz.ProcessAnswerAsync(project, question, "练习作答", DateTime.Now);
        var examAssessment = await exam.GradeForReviewAsync(project, question, "考试作答", DateTime.Now);

        Assert.Equal(2, grader.Calls);
        Assert.Equal("考试作答", examAssessment!.UserAnswer);
        Assert.Same(practice.AiAssessment, question.LastAiAssessment);
        Assert.Single(question.ReviewTag);
        Assert.Null(await exam.GradeAsync(new Question { Type = QuestionType.TrueFalse }, "正确"));
    }

    [Fact]
    public void SmartReviewDrawsFromAllCategories()
    {
        var first = new Question { Text = "细菌", Status = true };
        var second = new Question { Text = "真菌", Status = false };
        var fresh = new Question { Text = "未作答" };
        var project = new Project { TemplateType = ProjectTemplateType.RuleBasedReview,
            Chapters = [new() { Name = "细菌类", Questions = [first, fresh] }, new() { Name = "真菌类", Questions = [second] }] };
        var generator = new ReviewGenerator(new FsrsReviewScheduler());
        var review = generator.GenerateReview(project, 30);
        Assert.Equal(2, review.Count);
        Assert.Contains(first, review);
        Assert.Contains(second, review);
        Assert.DoesNotContain(fresh, generator.GenerateParameterizationReview(project, new() { NewQuestionRatio = 1 }));
        Assert.True(first.Status);
        Assert.False(second.Status);
    }

    [Fact]
    public async Task ProjectRoundTripPreservesTypePromptsAndGrading()
    {
        var root = Path.Combine(Path.GetTempPath(), "RuleProjectTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = new Project { ProjectName = "示例", StoragePath = root, TemplateType = ProjectTemplateType.RuleBasedReview,
                Chapters = [new() { Name = "细菌", GenerationPrompt = "传播途径", Questions = [new() { RequiresAiGrading = true, Text = "问题", LastAiAssessment = new() { UserAnswer = "答案" } }] }] };
            var files = new ProjectFileService();
            await files.SaveProjectAsync(project);
            var restored = await files.OpenProjectAsync(Path.Combine(root, "示例", "示例.rhproj"));
            Assert.Equal(ProjectTemplateType.RuleBasedReview, restored!.TemplateType);
            Assert.Equal("传播途径", restored.Chapters![0].GenerationPrompt);
            Assert.True(restored.Chapters[0].Questions![0].RequiresAiGrading);
            Assert.Equal("答案", restored.Chapters[0].Questions![0].LastAiAssessment!.UserAnswer);
            Assert.Equal(ProjectTemplateType.ClassicalReview, JsonSerializer.Deserialize<Project>("{}")!.TemplateType);
            Assert.Equal(4, (int)ProjectTemplateType.RuleBasedReview);
            Assert.Equal(ProjectTemplateType.RuleBasedReview, JsonSerializer.Deserialize<Project>("{\"template_type\":4}")!.TemplateType);
            Assert.False(Enum.IsDefined((ProjectTemplateType)3));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExamQuestionsUseAiAssessmentForJudgement()
    {
        var grader = new GraderStub();
        var service = new ExamAnswerService(grader);
        var question = new Question { RequiresAiGrading = true, CorrectAnswer = "完全一样" };
        var result = await service.GradeAsync(question, "完全一样");
        Assert.NotNull(result);
        Assert.Equal(1, grader.Calls);
        Assert.False(service.IsCorrect(question, "完全一样"));
    }

    private static string PreviewBatch(string prefix, int plannedCount) => JsonSerializer.Serialize(new
    {
        questionPlan = Enumerable.Range(1, plannedCount).Select(i => $"{prefix}考查范围{i}"),
        questions = JsonSerializer.Deserialize<JsonElement>(Batch(prefix, Math.Min(2, plannedCount)))
    });

    private static string Batch(string prefix, int count) => JsonSerializer.Serialize(Enumerable.Range(1, count)
        .Select(i => new { text = $"{prefix}{i}", correct_answer = $"答案{i}" }));

    private sealed class ChatStub(params string[] responses) : IAiChatService
    {
        public List<string> Prompts { get; } = [];
        public Task<string> RunAsync(string key, string prompt, string? instructions = null)
        {
            Prompts.Add(prompt);
            return Task.FromResult(responses[Prompts.Count - 1]);
        }
    }

    private sealed class GraderStub : IAiAnswerGrader
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public Task<AiAnswerAssessment> GradeAsync(Question question, string answer)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("服务暂时不可用");
            return Task.FromResult(new AiAnswerAssessment { UserAnswer = answer, ScoreRatio = 0.55, RawScoreRatio = 0.47,
                Annotations = [new() { Text = answer, Status = "partial" }] });
        }
    }

    private sealed class ConfigStub(ConfigOptions? options = null) : IConfigService
    {
        public Task<ConfigOptions> LoadAsync() => Task.FromResult(options ?? new ConfigOptions());
        public Task SaveAsync(ConfigOptions config) => Task.CompletedTask;
    }

    private sealed class ForbiddenLocalJudge : IAnswerJudge
    {
        public Task<bool> JudgeAsync(string? user, string? correct) => throw new InvalidOperationException("规则项目调用了本地评分");
        public Task<double> CalculateSimilarityAsync(string user, string correct) => throw new InvalidOperationException("规则项目调用了相似度评分");
    }

    private sealed class LocalJudge : IAnswerJudge
    {
        public Task<bool> JudgeAsync(string? user, string? correct) => Task.FromResult(user == correct);
        public Task<double> CalculateSimilarityAsync(string user, string correct) => Task.FromResult(user == correct ? 1d : 0d);
    }
}
