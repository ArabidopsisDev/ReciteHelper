using System.Text.Json;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class ProjectSourceOrderTests
{
    private const string First = "资料先介绍栈的存取规则";
    private const string Second = "接着讲解队列的使用方式";
    private const string Third = "随后回到栈的实际用途";
    private const string Fourth = "最后说明队列的场景";

    [Fact]
    public async Task CreationKeepsSourceOrderAfterAChunkRetryAndInterleavedClusters()
    {
        var directory = Directory.CreateTempSubdirectory("ReciteHelperSourceOrder");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "资料.meg");
            var firstChunk = (First + "。" + Second + "。" + Third + "。").PadRight(500);
            var source = MergeFile.Create([firstChunk + Fourth], FileClusterType.Sequential);
            await File.WriteAllTextAsync(sourcePath, JsonSerializer.Serialize(source));
            var chat = new OutOfOrderChat();
            var service = new ProjectCreationService(new PromptNames(), new ProjectFileService(), new NoEmbeddings(), chat);

            var result = await service.CreateAsync(new("测试项目", directory.FullName, [sourcePath], "", MissingStrategy.Replay));
            var persisted = await new ProjectFileService().OpenProjectAsync(result.ProjectPath);

            Assert.Equal(2, chat.FirstChunkCalls);
            Assert.Equal(new[] { First, Second, Third, Fourth },
                persisted!.Chapters!.SelectMany(chapter => chapter.Questions!).Select(question => Assert.Single(question.SourceExcerpts)));
            Assert.Equal(new[] { 1, 2, 3, 4 }, persisted.Chapters!.Select(chapter => chapter.Number));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class PromptNames : IPromptProvider
    {
        public Task<string> GetPromptAsync(string promptName) => Task.FromResult(promptName);
    }

    private sealed class NoEmbeddings : IKnowledgeBaseService
    {
        public Task<FileVectorStore> Build(string projectPath, string content) => throw new NotSupportedException();

        public Task<IReadOnlyList<KnowledgeBaseMatch>> SearchAsync(
            FileVectorStore store, string query, int topK, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<float[]>> EmbedTextsAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class OutOfOrderChat : IAiChatService
    {
        public int FirstChunkCalls { get; private set; }

        public Task<string> RunAsync(string deepSeekKey, string prompt, string? instructions = null)
        {
            if (prompt.StartsWith("GenerateChapter.txt", StringComparison.Ordinal))
                return Task.FromResult("""
                    [{"names":["队列应用"],"number":1,"uname":"队列"},{"names":["栈的应用"],"number":2,"uname":"栈"}]
                    """);

            if (prompt.Contains(Fourth, StringComparison.Ordinal))
                return Task.FromResult(JsonSerializer.Serialize(new[] { Section("队列应用", Fourth) }));

            FirstChunkCalls++;
            if (FirstChunkCalls == 1) throw new IOException("模拟首次请求中断");
            return Task.FromResult(JsonSerializer.Serialize(new[] { Section("栈的应用", Third, First), Section("队列应用", Second) }));
        }

        private static Chapter Section(string name, params string[] excerpts) => new()
        {
            Name = name,
            Questions = excerpts.Select(excerpt => new Question
            {
                Text = $"请说明{excerpt}。",
                CorrectAnswer = "课程参考答案",
                SourceExcerpts = [excerpt]
            }).ToList()
        };
    }
}
