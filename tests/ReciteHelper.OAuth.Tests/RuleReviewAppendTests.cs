using System.Text.Json;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class RuleReviewAppendTests
{
    [Fact]
    public async Task ExistingCategoryKeepsItsQuestionsAndLearningRecordsWhenAppending()
    {
        var project = ProjectWithHistory();
        var chapter = project.Chapters![0];
        var oldQuestion = chapter.Questions![0];
        var before = JsonSerializer.Serialize(oldQuestion);
        await new RuleReviewProjectEditor(new FilesStub()).AppendAsync(project, chapter, Batch("细菌", "新增范围", "新题1", "新题2"));
        Assert.Single(project.Chapters);
        Assert.Equal(3, chapter.Questions!.Count);
        Assert.Same(oldQuestion, chapter.Questions[0]);
        Assert.Equal(before, JsonSerializer.Serialize(oldQuestion));
        Assert.Equal("原有要求", chapter.GenerationPrompt);
        Assert.Equal("新增范围", Assert.Single(chapter.AdditionalGenerationPrompts));
    }

    [Fact]
    public async Task NewCategoryAndAdditionalQuestionsAreAvailableAfterReopening()
    {
        var project = ProjectWithHistory();
        var root = Path.Combine(Path.GetTempPath(), "RuleAppendTests", Guid.NewGuid().ToString("N"));
        project.StoragePath = root;
        try
        {
            var files = new ProjectFileService();
            var editor = new RuleReviewProjectEditor(files);
            await editor.AppendAsync(project, project.Chapters![0], Batch("细菌", "新增范围", "新题1", "新题2"));
            await editor.AppendAsync(project, null, Batch("真菌", "真菌范围", "真菌1", "真菌2"));
            var loaded = (await files.OpenProjectAsync(Path.Combine(root, project.ProjectName!, project.ProjectName + ".rhproj")))!;
            Assert.Equal(2, loaded.Chapters!.Count);
            Assert.Equal(3, loaded.Chapters[0].Questions!.Count);
            Assert.Equal(2, loaded.Chapters[1].Number);
            Assert.Equal(2, loaded.Chapters[1].Questions!.Count);
            Assert.Equal(8, loaded.Chapters[0].Questions![0].Memory!.Stability);
            Assert.Single(loaded.Chapters[0].Questions![0].ReviewTag);
            Assert.Single(loaded.Chapters[0].Questions![0].ReviewFacets);
            Assert.Equal("课程要点", Assert.Single(loaded.Chapters[0].ReviewMaterials).Content);
            Assert.Equal(2, loaded.Chapters[0].Questions![1].AnswerParts.Count);
            Assert.Equal("课程要点", Assert.Single(loaded.Chapters[0].Questions![1].SourceExcerpts));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AdditionalGenerationKeepsEachQuestionsOwnFileAcrossReload()
    {
        const string excerpt = "课程复习应说明传播途径。";
        var project = ProjectWithHistory();
        var chapter = project.Chapters![0];
        var original = chapter.Questions![0];
        var first = new ReviewMaterial("讲义.txt", "原材料只讲甲的传播途径。" + excerpt);
        var second = new ReviewMaterial("讲义.txt", "新材料只讲乙的传播途径。" + excerpt);
        chapter.ReviewMaterials = [first];
        original.SourceExcerpts = [excerpt];

        var chat = new Chat(JsonSerializer.Serialize(new
        {
            questionPlan = new[] { "乙的传播途径" },
            questions = new[] { new { text = "说明乙的传播途径。", correct_answer = excerpt, source_excerpts = new[] { excerpt },
                source_material_ids = new[] { second.SourceId } } }
        }));
        var service = new RuleReviewService(chat);
        var preview = await service.PreviewAsync(new() { Name = chapter.Name!, Prompt = "说明乙的传播途径。", Materials = [second] });
        var generated = await service.CompleteAsync(preview);
        await new RuleReviewProjectEditor(new FilesStub()).AppendAsync(project, chapter, generated);
        var restored = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project))!;
        var questions = restored.Chapters![0].Questions!;
        var help = new QuestionHelpService(null!, null!, null!);

        var oldPassage = Assert.Single(await help.FindMatchesAsync(restored, questions[0]));
        var newPassage = Assert.Single(await help.FindMatchesAsync(restored, questions[1]));
        Assert.Contains("只讲甲", oldPassage.Content);
        Assert.Contains("只讲乙", newPassage.Content);
        Assert.Equal(first.SourceId, Assert.Single(questions[0].SourceMaterialIds!));
        Assert.Equal(second.SourceId, Assert.Single(questions[1].SourceMaterialIds!));
    }

    [Fact]
    public async Task AdditionalGenerationCarriesExistingQuestionsThroughPreviewAndCompletion()
    {
        var chat = new Chat(Preview(), Questions("补充1", "补充2"));
        var service = new RuleReviewService(chat);
        var preview = await service.PreviewAsync(new() { Name = "细菌", Prompt = "新增寄主与传播细节",
            ExistingQuestionTexts = ["原题"], IsAppending = true });
        var chapter = await service.CompleteAsync(preview);
        Assert.True(preview.IsAppending);
        Assert.Equal(4, chapter.Questions!.Count);
        using var first = JsonDocument.Parse(chat.Inputs[0]);
        using var second = JsonDocument.Parse(chat.Inputs[1]);
        Assert.Equal("原题", first.RootElement.GetProperty("existingQuestions")[0].GetString());
        Assert.Equal(3, second.RootElement.GetProperty("existingQuestions").GetArrayLength());
        Assert.Equal("原题", second.RootElement.GetProperty("existingQuestions")[0].GetString());
    }

    private static Project ProjectWithHistory() => new() { ProjectName = "现有项目", TemplateType = ProjectTemplateType.RuleBasedReview,
        Chapters = [new() { Name = "细菌", Number = 1, GenerationPrompt = "原有要求", Questions = [new() { Text = "原题", CorrectAnswer = "原答案",
            RequiresAiGrading = true, Memory = MemoryRecord.From(new MemoryState(8, 4), DateTime.Now, 2, 1),
            ReviewTag = [new() { Grade = 3, Time = DateTime.Now }],
            ReviewFacets = [new() { Key = "old", Name = "旧切面", Weight = 1, Question = new() { ReviewFacetId = Guid.NewGuid() } }] }] }] };
    private static Chapter Batch(string name, string prompt, params string[] questions) => new() { Name = name, GenerationPrompt = prompt,
        ReviewMaterials = [new("课程笔记", "课程要点")],
        Questions = questions.Select(text => new Question { Text = text, CorrectAnswer = "参考答案", RequiresAiGrading = true,
            SourceExcerpts = ["课程要点"], AnswerParts = [new() { Key = "a", Prompt = "第一问", ReferenceAnswer = "甲", Weight = 0.5 },
                new() { Key = "b", Prompt = "第二问", ReferenceAnswer = "乙", Weight = 0.5 }] }).ToList() };
    private static string Questions(params string[] texts) => JsonSerializer.Serialize(texts.Select(text => new { text, correct_answer = "完整参考答案" }));
    private static string Preview() => "{\"questionPlan\":[\"范围1\",\"范围2\",\"范围3\",\"范围4\"],\"questions\":" + Questions("预览1", "预览2") + "}";

    private sealed class Chat(params string[] responses) : IAiChatService
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> Inputs { get; } = [];
        public Task<string> RunAsync(string key, string prompt, string? instructions = null) { Inputs.Add(prompt); return Task.FromResult(_responses.Dequeue()); }
    }
    private sealed class FilesStub : IProjectFileService
    {
        public Task SaveProjectAsync(Project project) => Task.CompletedTask;
        public Task<Project?> OpenProjectAsync(string path) => throw new NotSupportedException();
        public bool ProjectExists(string path) => false;
        public Task<ImportedProject> ImportProjectArchiveAsync(string archive, string destination) => throw new NotSupportedException();
        public Task<string> ExportProjectArchiveAsync(Project project, string destination, string? version) => throw new NotSupportedException();
    }
}
