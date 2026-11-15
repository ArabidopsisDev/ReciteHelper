using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.ValueObjects;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed class GalGameCreationService : IGalGameCreationService
{
    private const string Instructions = "You are a writer who excels at creating moving and touching screenplays.";
    private readonly IProjectFileService _projectFileService;
    private readonly IGalGameService _galGameService;
    private readonly IPromptProvider _promptProvider;
    private readonly IAiChatService _aiChatService;

    public GalGameCreationService(
        IProjectFileService projectFileService,
        IGalGameService galGameService,
        IPromptProvider promptProvider,
        IAiChatService aiChatService)
    {
        _projectFileService = projectFileService;
        _galGameService = galGameService;
        _promptProvider = promptProvider;
        _aiChatService = aiChatService;
    }

    public async Task CreateAsync(string projectPath, string deepSeekKey)
    {
        var project = await _projectFileService.OpenProjectAsync(projectPath)
            ?? throw new InvalidOperationException("无法读取项目文件。");

        var chapterQuestions = new Dictionary<string, StringBuilder>();
        var chapterNames = new StringBuilder();

        foreach (var chapter in project.Chapters ?? [])
        {
            if (chapter.Name is null)
                continue;

            var singleChapter = new StringBuilder();

            foreach (var question in chapter.Questions ?? [])
                singleChapter.AppendLine($"问题：{question.Text} 答案：{question.CorrectAnswer}");

            chapterNames.AppendLine(chapter.Name);
            chapterQuestions.Add(chapter.Name, singleChapter);
        }

        var clusterPrompt = await _promptProvider.GetPromptAsync("ReClustering.txt");
        var clusterResponse = await _aiChatService.RunAsync(deepSeekKey, $"{clusterPrompt}\n{chapterNames}", Instructions);
        var clusterJson = CleanJson(clusterResponse);
        var clusterResult = JsonSerializer.Deserialize<List<ChapterCluster>>(clusterJson)
            ?? throw new InvalidOperationException("游戏章节聚类结果无法解析。");

        chapterNames.Clear();
        clusterResult.ForEach(cluster => chapterNames.Append($"{cluster.UnifiedName}/"));

        var outlinePrompt = await _promptProvider.GetPromptAsync("GenerateOutline.txt");
        var outlineResponse = await _aiChatService.RunAsync(deepSeekKey, $"{outlinePrompt}\n{chapterNames}", Instructions);
        var chapterList = JsonSerializer.Deserialize<List<GameChapterDto>>(CleanJson(outlineResponse))
            ?? throw new InvalidOperationException("游戏章节大纲无法解析。");

        var galPrompt = await _promptProvider.GetPromptAsync("GenerateGal.txt");
        var combined = chapterList.Zip(clusterResult, (first, second) => (first, second));
        var storyLines = new ConcurrentBag<object>();

        await Parallel.ForEachAsync(combined, async (it, _) =>
        {
            var chapter = it.first;
            var cluster = it.second;
            var builder = new StringBuilder();

            foreach (var item in cluster.Chapters ?? [])
            {
                if (chapterQuestions.TryGetValue(item, out var questions))
                    builder.AppendLine(questions.ToString());
            }

            var currentPrompt = galPrompt;
            currentPrompt += $"{chapter.GameChapterOutline}\n" +
                             "This is the content the user needs to review (but don't explicitly label the learning points in the story; let the user feel like they are learning naturally)." +
                             $"{builder}";

            var galResponse = await _aiChatService.RunAsync(deepSeekKey, currentPrompt, Instructions);
            var galCode = galResponse.Replace("`", "").Replace("csharp", "").Trim();

            var mainStoryLine = await _galGameService.CompileStoryAsync(galCode);
            storyLines.Add(mainStoryLine);
        });

        await _galGameService.SaveStoryLinesAsync(project, storyLines);
    }

    private static string CleanJson(string raw)
    {
        return raw.Replace("`", "").Replace("json", "").Trim();
    }
}
