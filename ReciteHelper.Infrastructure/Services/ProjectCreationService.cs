using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Infrastructure.Utilities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReciteHelper.Infrastructure.Services;

public sealed partial class ProjectCreationService : IProjectCreationService
{
    private const int ChunkSize = 500;
    private const int StructuredChapterChunkSize = 800;
    private const int ChapterSimilaritySampleSize = 2400;
    private const float NearDuplicateChapterSimilarity = 0.75f;
    private const int MinPreferredChapterCount = 6;
    private const int MaxPreferredChapterCount = 12;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPromptProvider _promptProvider;
    private readonly IProjectFileService _projectFileService;
    private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly IAiChatService _aiChatService;

    public ProjectCreationService(
        IPromptProvider promptProvider,
        IProjectFileService projectFileService,
        IKnowledgeBaseService knowledgeBaseService,
        IAiChatService aiChatService)
    {
        _promptProvider = promptProvider;
        _projectFileService = projectFileService;
        _knowledgeBaseService = knowledgeBaseService;
        _aiChatService = aiChatService;
    }

    public async Task<CreateProjectResult> CreateAsync(
        CreateProjectRequest request,
        IProgress<ProjectCreationProgress>? progress = null)
    {
        Report(progress, 1, 1, 0, 0, 1, 1, "正在读取题库文件...", ProjectCreationStage.ReadingText);

        var projectDir = Path.Combine(request.StoragePath, request.ProjectName);
        Directory.CreateDirectory(projectDir);

        var copiedQuestionBanks = CopyQuestionBanks(request.QuestionBankPaths, projectDir);
        var project = new Project
        {
            ProjectName = request.ProjectName,
            QuestionBankPath = string.Join(';', copiedQuestionBanks) + ";",
            Chapters = [],
            StoragePath = request.StoragePath
        };
        var knowledgeBaseSourceText = string.Empty;

        var firstExtension = Path.GetExtension(copiedQuestionBanks.FirstOrDefault() ?? string.Empty);
        if (firstExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            knowledgeBaseSourceText = await ReadLearningSourceTextAsync(copiedQuestionBanks, progress);
            Report(progress, 1, 1, 0, 0, 1, 1, "题库文本读取完成。", ProjectCreationStage.ReadingText);
            project.Chapters = await ProcessTextAsync(
                knowledgeBaseSourceText,
                request.DeepSeekKey,
                request.MissingStrategy,
                progress,
                existingChapterNames: [],
                allowNewChapters: true);
        }
        else if (firstExtension.Equals(".meg", StringComparison.OrdinalIgnoreCase))
        {
            var mergeFile = (MergeFile)ExtractText.FromAutomatic(copiedQuestionBanks[0]);
            if (mergeFile is null)
                throw new InvalidOperationException("题库文件已损坏！");

            knowledgeBaseSourceText = string.Join(Environment.NewLine, mergeFile.Contents);
            if (mergeFile.ClusterType == FileClusterType.Sequential)
            {
                var result = new List<Chapter>();
                var round = 1;
                foreach (var item in mergeFile.Contents)
                {
                    Report(progress, 1, 1, 0, 0, round, mergeFile.Contents.Count, "正在读取合并题库片段...", ProjectCreationStage.ReadingText);
                    var cluster = await ProcessTextAsync(
                        item,
                        request.DeepSeekKey,
                        request.MissingStrategy,
                        progress,
                        existingChapterNames: [],
                        allowNewChapters: true);
                    if (cluster is not null)
                        result.AddRange(cluster);
                    round++;
                }

                project.Chapters = result;
            }
            else
            {
                project.Chapters = await ProcessTextAsync(knowledgeBaseSourceText, request.DeepSeekKey,
                    request.MissingStrategy, progress, existingChapterNames: [], allowNewChapters: true);
            }
        }
        else
        {
            throw new NotSupportedException("创建项目仅支持 PDF 或 MEG 学习资料；HTML/MHTML 试卷请在项目内使用“导入试卷”。");
        }

        if (project.Chapters is null || !project.Chapters.Any(chapter => chapter.Questions is { Count: > 0 }))
            throw new InvalidOperationException("项目创建失败：未能从题库生成任何题目，请检查资料文本提取结果或稍后重试。");

        var sourceChapters = project.Chapters;
        project.Chapters = [];
        AppendSourceChapters(project.Chapters, sourceChapters);
        await BuildKnowledgeBaseAsync(project, projectDir, knowledgeBaseSourceText, progress);
        Report(progress, 1, 1, 1, 1, 1, 1, "项目创建完成。", ProjectCreationStage.Completed);

        await _projectFileService.SaveProjectAsync(project);

        return new CreateProjectResult(
            project,
            Path.Combine(projectDir, $"{request.ProjectName}.rhproj"));
    }

    public async Task AppendSourcesAsync(
        Project project,
        IReadOnlyList<string> sourcePaths,
        string deepSeekKey,
        MissingStrategy missingStrategy,
        IProgress<ProjectCreationProgress>? progress = null)
    {
        if (project.Chapters is null)
            project.Chapters = [];
        if (string.IsNullOrWhiteSpace(project.StoragePath) || string.IsNullOrWhiteSpace(project.ProjectName))
            throw new InvalidOperationException("项目路径信息不完整，无法追加题目来源。");
        if (sourcePaths.Count == 0)
            throw new InvalidOperationException("请选择要追加的 PDF 或 MEG 学习资料。");

        Report(progress, 0, sourcePaths.Count, 0, 0, 0, sourcePaths.Count, "正在复制并读取新的学习资料。", ProjectCreationStage.ReadingText);
        var projectDir = Path.Combine(project.StoragePath, project.ProjectName);
        Directory.CreateDirectory(projectDir);
        var copiedSources = CopyQuestionBanks(sourcePaths, projectDir);
        var sourceText = await ReadLearningSourceTextAsync(copiedSources, progress);
        if (string.IsNullOrWhiteSpace(sourceText))
            throw new InvalidOperationException("追加失败：未能从学习资料中读取到文本。");

        var existingNames = project.Chapters
            .Select(chapter => chapter.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        var generated = await ProcessTextAsync(
            sourceText,
            deepSeekKey,
            missingStrategy,
            progress,
            existingNames,
            allowNewChapters: true);

        if (generated is null || !generated.Any(chapter => chapter.Questions is { Count: > 0 }))
            throw new InvalidOperationException("没有可追加的题目。");
        AppendSourceChapters(project.Chapters, generated);
        project.QuestionBankPath = MergeQuestionBankPaths(project.QuestionBankPath, copiedSources);

        await BuildKnowledgeBaseAsync(project, projectDir, BuildProjectKnowledgeCorpus(project, sourceText), progress);
        Report(progress, 1, 1, 1, 1, 1, 1, "新的题目来源已经导入完成。", ProjectCreationStage.Completed);
        await _projectFileService.SaveProjectAsync(project);
    }

    public async Task ImportQuestionsAsync(
        Project project,
        IReadOnlyList<Question> questions,
        string deepSeekKey,
        IProgress<ProjectCreationProgress>? progress = null)
    {
        if (project.Chapters is null || project.Chapters.Count == 0)
            throw new InvalidOperationException("项目中没有可归入的已有章节。");
        if (questions.Count == 0)
            throw new InvalidOperationException("没有选择需要导入的错题。");

        Report(progress, 0, questions.Count, 0, questions.Count, 0, 1, "正在提取待导入错题。", ProjectCreationStage.KnowledgeExtraction);

        var candidates = questions
            .Select((question, index) => new Chapter
            {
                Name = $"错题{index + 1:D3}：{CreateKnowledgePointName(question.Text ?? "未命名题目")}",
                Number = index + 1,
                Questions = [CloneQuestion(question)],
                KnowledgePoints = []
            })
            .ToList();
        var existingNames = project.Chapters
            .Select(chapter => chapter.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        Report(progress, null, null, 0, candidates.Count, 1, 1, "正在把错题归入已有章节。", ProjectCreationStage.TextClustering);
        var clustered = await ClusterGeneratedChaptersAsync(
            [candidates],
            deepSeekKey,
            progress,
            existingNames,
            allowNewChapters: false);

        MergeIntoProject(project, clustered, appendToExistingOnly: true);
        var projectDir = Path.Combine(project.StoragePath!, project.ProjectName!);
        await BuildKnowledgeBaseAsync(project, projectDir, BuildProjectKnowledgeCorpus(project, string.Empty), progress);
        Report(progress, 1, 1, 1, 1, 1, 1, "错题已经导入题库。", ProjectCreationStage.Completed);
        await _projectFileService.SaveProjectAsync(project);
    }

    private static async Task<string> ReadLearningSourceTextAsync(
        IReadOnlyList<string> copiedSources,
        IProgress<ProjectCreationProgress>? progress)
    {
        var sourceText = new StringBuilder();
        for (var index = 0; index < copiedSources.Count; index++)
        {
            var source = copiedSources[index];
            Report(
                progress,
                index + 1,
                copiedSources.Count,
                0,
                0,
                index + 1,
                copiedSources.Count,
                $"正在读取学习资料 {index + 1}/{copiedSources.Count}。",
                ProjectCreationStage.ReadingText);

            var extension = Path.GetExtension(source);
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                sourceText.AppendLine(ExtractText.FromAutomatic(source));
                continue;
            }

            if (extension.Equals(".meg", StringComparison.OrdinalIgnoreCase))
            {
                var mergeFile = (MergeFile)ExtractText.FromAutomatic(source);
                if (mergeFile is not null)
                    sourceText.AppendLine(string.Join(Environment.NewLine, mergeFile.Contents));
                continue;
            }

            throw new NotSupportedException("追加题目来源仅支持 PDF 或 MEG 学习资料；HTML/MHTML 试卷请使用“导入试卷”。");
        }

        await Task.CompletedTask;
        return sourceText.ToString();
    }

    private static void AppendSourceChapters(List<Chapter> chapters, IEnumerable<Chapter> additions)
    {
        var names = chapters.Select(chapter => chapter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in additions)
        {
            var name = chapter.Name ?? "复习资料";
            var uniqueName = name;
            for (var occurrence = 2; !names.Add(uniqueName); occurrence++)
                uniqueName = $"{name}（续 {occurrence}）";
            chapter.Name = uniqueName;
            chapter.Number = chapters.Count + 1;
            chapters.Add(chapter);
        }
    }

    private static void MergeIntoProject(Project project, IReadOnlyList<Chapter>? importedChapters, bool appendToExistingOnly)
    {
        if (importedChapters is null || importedChapters.Count == 0)
            throw new InvalidOperationException("没有可导入的题目。");

        project.Chapters ??= [];
        foreach (var imported in importedChapters)
        {
            if (imported.Questions is null || imported.Questions.Count == 0)
                continue;

            var target = project.Chapters.FirstOrDefault(chapter =>
                string.Equals(chapter.Name, imported.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                if (appendToExistingOnly)
                    target = project.Chapters.FirstOrDefault();
                else
                {
                    target = new Chapter
                    {
                        Name = string.IsNullOrWhiteSpace(imported.Name) ? "杂项题目" : imported.Name,
                        Number = project.Chapters.Count + 1,
                        Questions = [],
                        KnowledgePoints = []
                    };
                    project.Chapters.Add(target);
                }
            }

            if (target is null)
                continue;

            target.Questions ??= [];
            target.KnowledgePoints ??= [];
            target.Questions.AddRange(imported.Questions);
            if (imported.KnowledgePoints is not null)
                target.KnowledgePoints.AddRange(imported.KnowledgePoints);
        }

        for (var index = 0; index < project.Chapters.Count; index++)
            project.Chapters[index].Number = index + 1;
    }

    private static string MergeQuestionBankPaths(string? existing, IReadOnlyList<string> additions)
    {
        var paths = (existing ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(additions)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(';', paths) + ";";
    }

    private static string BuildProjectKnowledgeCorpus(Project project, string additionalSourceText)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(additionalSourceText))
            builder.AppendLine(additionalSourceText);

        foreach (var chapter in project.Chapters ?? [])
        {
            builder.AppendLine($"章节：{chapter.Name}");
            foreach (var point in chapter.KnowledgePoints ?? [])
            {
                builder.AppendLine(point.Name);
                builder.AppendLine(point.ContentMarkdown);
            }

            foreach (var question in chapter.Questions ?? [])
            {
                builder.AppendLine($"题目：{question.Text}");
                builder.AppendLine($"答案：{question.GetCorrectAnswerText()}");
            }
        }

        return builder.ToString();
    }

    private static Question CloneQuestion(Question question)
    {
        var json = JsonSerializer.Serialize(question, JsonOptions);
        var cloned = JsonSerializer.Deserialize<Question>(json, JsonOptions) ?? new Question();
        cloned.Status = null;
        return cloned;
    }

    private async Task BuildKnowledgeBaseAsync(
        Project project,
        string projectDir,
        string sourceText,
        IProgress<ProjectCreationProgress>? progress)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            project.MarkKnowledgeBaseBuildFailed("题库文本为空，未构建知识库。");
            return;
        }

        Report(progress, 0, 1, 0, 1, null, null, "正在生成知识库向量...", ProjectCreationStage.VectorGeneration);

        const string knowledgeBaseFileName = "knowledge-base.json";
        var knowledgeBasePath = Path.Combine(projectDir, knowledgeBaseFileName);

        try
        {
            var store = await _knowledgeBaseService.Build(knowledgeBasePath, sourceText);
            project.AttachKnowledgeBase(knowledgeBaseFileName, store);
            Report(progress, 1, 1, 1, 1, null, null, "知识库向量生成完成。", ProjectCreationStage.VectorGeneration);
        }
        catch (Exception ex)
        {
            if (File.Exists(knowledgeBasePath))
                File.Delete(knowledgeBasePath);

            project.MarkKnowledgeBaseBuildFailed(ex.Message);
            Debug.WriteLine($"Failed to build knowledge base: {ex}");
            Report(progress, 1, 1, 1, 1, null, null, "知识库构建失败，项目将继续创建。", ProjectCreationStage.VectorGeneration);
        }
    }

    private static List<string> CopyQuestionBanks(IEnumerable<string> questionBankPaths, string projectDir)
    {
        var copied = new List<string>();

        foreach (var bankPath in questionBankPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var destination = Path.Combine(projectDir, Path.GetFileName(bankPath));
            File.Copy(bankPath, destination, true);
            copied.Add(destination);
        }

        return copied;
    }

    private async Task<List<Chapter>?> ProcessTextAsync(
        string text,
        string deepSeekKey,
        MissingStrategy missingStrategy,
        IProgress<ProjectCreationProgress>? progress,
        IReadOnlyList<string> existingChapterNames,
        bool allowNewChapters)
    {
        if (allowNewChapters && ContainsExplicitChapterMarkers(text))
        {
            Report(
                progress,
                0,
                1,
                0,
                1,
                null,
                null,
                "检测到资料中存在明确章节，正在按源文件章节切分。",
                ProjectCreationStage.KnowledgeExtraction,
                usesSourceChapters: true);
            return await ProcessStructuredChapterTextAsync(text, deepSeekKey, missingStrategy, progress);
        }

        var scanTotal = (int)Math.Ceiling(text.Length / (double)ChunkSize);
        Report(progress, 0, scanTotal, 0, scanTotal, null, null, "你的资料已经被切割完成，正在分块提取知识点和相关题目。", ProjectCreationStage.KnowledgeExtraction);

        return await ClusterQuestionsAsync(text, deepSeekKey, missingStrategy, progress, existingChapterNames, allowNewChapters);
    }

    private async Task<string> RunChatAsync(string deepSeekKey, string prompt, string? instructions = null)
    {
        return await _aiChatService.RunAsync(deepSeekKey, prompt, instructions);
    }

    private static List<Chunk> BuildChunks(string text)
    {
        var totalChunks = (int)Math.Ceiling(text.Length / (double)ChunkSize);
        var chunks = new List<Chunk>();

        for (var i = 0; i < totalChunks; i++)
        {
            var startIndex = i * ChunkSize;
            var length = Math.Min(ChunkSize, text.Length - startIndex);
            chunks.Add(Chunk.Create(text.Substring(startIndex, length), false, i));
        }

        return chunks;
    }

    private async Task<List<Chapter>> ProcessStructuredChapterTextAsync(
        string text,
        string deepSeekKey,
        MissingStrategy missingStrategy,
        IProgress<ProjectCreationProgress>? progress)
    {
        var localSplit = SplitSourceChaptersLocally(text);
        var sourceChapters = localSplit.IsCompleteContinuous
            ? await MergeDuplicateSourceChaptersBySemanticAsync(localSplit.Chapters)
            : localSplit.Chapters;
        if (sourceChapters.Count == 0)
            return await ClusterQuestionsAsync(text, deepSeekKey, missingStrategy, progress, [], allowNewChapters: true);

        Report(
            progress,
            0,
            sourceChapters.Count,
            1,
            sourceChapters.Count,
            null,
            null,
            $"已识别 {sourceChapters.Count} 个源文件章节，正在按章节生成题目。",
            ProjectCreationStage.TextClustering,
            usesSourceChapters: true);

        var result = new List<Chapter>();
        for (var chapterIndex = 0; chapterIndex < sourceChapters.Count; chapterIndex++)
        {
            var sourceChapter = sourceChapters[chapterIndex];
            var chunks = BuildStructuredChapterChunks(sourceChapter.Content);
            var generated = await GenerateQuestionsForStructuredChapterAsync(
                sourceChapter.Name,
                chunks,
                deepSeekKey,
                progress,
                chapterIndex,
                sourceChapters.Count);

            if (generated.Questions is { Count: > 0 } || generated.KnowledgePoints is { Count: > 0 })
            {
                generated.Number = result.Count + 1;
                result.Add(generated);
            }
        }

        return RenumberChapters(result);
    }

    private static SourceChapterSplit SplitSourceChaptersLocally(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SourceChapterSplit.Empty;

        var matches = ExplicitChapterHeadingRegex().Matches(text);
        if (matches.Count == 0)
            return SourceChapterSplit.Empty;

        var markers = matches
            .Cast<Match>()
            .Select(match => new SourceChapterMarker(
                match.Groups["heading"].Index,
                CleanChapterName(match.Groups["heading"].Value),
                NormalizeChapterOrdinal(match.Groups["ordinal"].Value),
                ParseChapterNumber(match.Groups["ordinal"].Value)))
            .Where(marker => marker.Index >= 0 &&
                             !string.IsNullOrWhiteSpace(marker.Heading) &&
                             !string.IsNullOrWhiteSpace(marker.Ordinal) &&
                             marker.Number is > 0)
            .OrderBy(marker => marker.Index)
            .ToList();
        if (markers.Count == 0)
            return SourceChapterSplit.Empty;

        var chapters = new List<SourceChapter>();
        var preface = text[..markers[0].Index].Trim();
        if (preface.Length >= StructuredChapterChunkSize / 2)
            chapters.Add(new SourceChapter(InferPrefaceChapterName(preface), preface, 0));

        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index];
            var nextIndex = index + 1 < markers.Count ? markers[index + 1].Index : text.Length;
            var name = CleanChapterName(marker.Heading);
            var content = text[marker.Index..nextIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(content))
                chapters.Add(new SourceChapter(name, content, marker.Number));
        }

        return new SourceChapterSplit(chapters, IsCompleteContinuousChapterSequence(markers));
    }

    private async Task<List<SourceChapter>> MergeDuplicateSourceChaptersBySemanticAsync(
        IReadOnlyList<SourceChapter> chapters)
    {
        if (chapters.Count == 0)
            return [];

        var result = new List<SourceChapter>();
        foreach (var chapter in chapters)
        {
            if (result.Count == 0 || !IsSameSourceChapter(result[^1], chapter))
            {
                result.Add(chapter);
                continue;
            }

            var current = result[^1];
            var similarity = await CalculateSourceChapterSemanticSimilarityAsync(current.Content, chapter.Content);
            if (similarity >= NearDuplicateChapterSimilarity)
                continue;

            result[^1] = current with
            {
                Content = $"{current.Content}{Environment.NewLine}{Environment.NewLine}{chapter.Content}"
            };
        }

        return result;
    }

    private async Task<float> CalculateSourceChapterSemanticSimilarityAsync(string left, string right)
    {
        try
        {
            var vectors = await _knowledgeBaseService.EmbedTextsAsync(
                [
                    CreateChapterSimilaritySample(left),
                    CreateChapterSimilaritySample(right)
                ]);
            return CalculateCosineSimilarity(vectors[0], vectors[1]);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to calculate chapter semantic similarity: {ex.Message}");
            return 0;
        }
    }

    private static bool IsSameSourceChapter(SourceChapter left, SourceChapter right)
    {
        if (left.Number.HasValue && right.Number.HasValue)
            return left.Number.Value == right.Number.Value;

        return string.Equals(
            CleanChapterName(left.Name),
            CleanChapterName(right.Name),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompleteContinuousChapterSequence(IReadOnlyList<SourceChapterMarker> markers)
    {
        var numbers = markers
            .Select(marker => marker.Number)
            .Where(number => number is > 0)
            .Select(number => number!.Value)
            .Distinct()
            .Order()
            .ToList();
        if (numbers.Count == 0 || numbers[0] != 1)
            return false;

        for (var index = 0; index < numbers.Count; index++)
        {
            if (numbers[index] != index + 1)
                return false;
        }

        return true;
    }

    private static int? ParseChapterNumber(string value)
    {
        var normalized = NormalizeChapterOrdinal(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (int.TryParse(normalized, out var numeric))
            return numeric;

        return ChineseNumberToInt(normalized);
    }

    private static int? ChineseNumberToInt(string text)
    {
        text = text.Replace("〇", "零", StringComparison.Ordinal)
            .Replace("两", "二", StringComparison.Ordinal);

        var map = new Dictionary<char, int>
        {
            ['零'] = 0,
            ['一'] = 1,
            ['二'] = 2,
            ['三'] = 3,
            ['四'] = 4,
            ['五'] = 5,
            ['六'] = 6,
            ['七'] = 7,
            ['八'] = 8,
            ['九'] = 9
        };

        var result = 0;
        var section = 0;
        var number = 0;
        foreach (var character in text)
        {
            if (map.TryGetValue(character, out var mapped))
            {
                number = mapped;
                continue;
            }

            switch (character)
            {
                case '十':
                    section += (number == 0 ? 1 : number) * 10;
                    number = 0;
                    break;
                case '百':
                    section += (number == 0 ? 1 : number) * 100;
                    number = 0;
                    break;
                case '千':
                    section += (number == 0 ? 1 : number) * 1000;
                    number = 0;
                    break;
                case '万':
                    result += (section + number) * 10000;
                    section = 0;
                    number = 0;
                    break;
                default:
                    return null;
            }
        }

        var total = result + section + number;
        return total > 0 ? total : null;
    }

    private static string CreateChapterSimilaritySample(string text)
    {
        var normalized = WhitespaceRegex().Replace(text ?? string.Empty, " ").Trim();
        if (normalized.Length <= ChapterSimilaritySampleSize)
            return normalized;

        var half = ChapterSimilaritySampleSize / 2;
        return $"{normalized[..half]} {normalized[^half..]}";
    }

    private static float CalculateCosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
            return 0;

        float dot = 0;
        float leftMagnitude = 0;
        float rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
            return 0;

        return dot / (float)(Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static string NormalizeChapterOrdinal(string value)
    {
        return WhitespaceRegex().Replace(value ?? string.Empty, string.Empty).Trim();
    }

    private static string InferPrefaceChapterName(string preface)
    {
        var firstMeaningfulLine = preface
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Contains("绪论", StringComparison.Ordinal) ||
                                    line.Contains("导论", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(firstMeaningfulLine))
            return CleanChapterName(firstMeaningfulLine.Length <= 40 ? firstMeaningfulLine : firstMeaningfulLine[..40]);

        return "绪论";
    }

    private async Task<Chapter> GenerateQuestionsForStructuredChapterAsync(
        string chapterName,
        IReadOnlyList<Chunk> chunks,
        string deepSeekKey,
        IProgress<ProjectCreationProgress>? progress,
        int chapterIndex,
        int chapterTotal)
    {
        var result = new Chapter
        {
            Name = chapterName,
            Number = chapterIndex + 1,
            Questions = [],
            KnowledgePoints = []
        };
        if (chunks.Count == 0)
            return result;

        var prompt = await _promptProvider.GetPromptAsync("GenerateQuestion.txt");
        var progressValue = 0;
        var generatedChunks = new ConcurrentBag<GeneratedChunk>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 8
        };

        await Parallel.ForEachAsync(chunks, parallelOptions, async (chunk, _) =>
        {
            try
            {
                var structuredPrompt = $$"""
                {{prompt}}

                当前资料章节：{{chapterName}}
                必须把本次生成的所有题目和知识点放入同一个章节，章节 name 必须严格等于：{{chapterName}}
                不要新建其它章节，不要聚类，不要改写章节名。

                <chapter_chunk>
                {{chunk.Content}}
                </chapter_chunk>
                """;
                var response = await RunChatAsync(deepSeekKey, structuredPrompt);
                var json = ExtractJsonContent(response);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var generated = JsonSerializer.Deserialize<List<Chapter>>(json, JsonOptions);
                if (generated is null)
                    return;

                generatedChunks.Add(LocateGeneratedQuestions(chunk, generated));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate structured chapter chunk: {ex.Message}");
            }
            finally
            {
                var current = Interlocked.Increment(ref progressValue);
                Report(
                    progress,
                    current,
                    chunks.Count,
                    chapterIndex + 1,
                    chapterTotal,
                    null,
                    null,
                    $"正在从“{chapterName}”生成题目 {current}/{chunks.Count}。",
                    ProjectCreationStage.TextClustering,
                    usesSourceChapters: true);
            }
        });

        foreach (var generated in generatedChunks.OrderBy(item => item.Index))
        {
            result.Questions!.AddRange(generated.QuestionsInOrder);
            foreach (var chapter in generated.Chapters)
            {
                if (chapter.KnowledgePoints is not null)
                    result.KnowledgePoints!.AddRange(chapter.KnowledgePoints);
            }
        }

        return result;
    }

    private static List<Chunk> BuildStructuredChapterChunks(string text)
    {
        var totalChunks = (int)Math.Ceiling(text.Length / (double)StructuredChapterChunkSize);
        var chunks = new List<Chunk>();
        for (var index = 0; index < totalChunks; index++)
        {
            var startIndex = index * StructuredChapterChunkSize;
            var length = Math.Min(StructuredChapterChunkSize, text.Length - startIndex);
            chunks.Add(Chunk.Create(text.Substring(startIndex, length), false, index));
        }

        return chunks;
    }

    private async Task<List<GeneratedChunk>> SendChunksAsync(
        List<Chunk> chunks,
        string deepSeekKey,
        IProgress<ProjectCreationProgress>? progress)
    {
        var generatedChunks = new ConcurrentBag<GeneratedChunk>();
        var progressValue = 0;
        var prompt = await _promptProvider.GetPromptAsync("GenerateQuestion.txt");

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 16
        };

        await Parallel.ForEachAsync(chunks, parallelOptions, async (chunk, _) =>
        {
            try
            {
                var result = await RunChatAsync(deepSeekKey, $"{prompt}\n{chunk.Content}");
                var jsonContent = ExtractJsonContent(result);
                if (string.IsNullOrWhiteSpace(jsonContent))
                    return;

                var chapter = JsonSerializer.Deserialize<List<Chapter>>(jsonContent, JsonOptions);
                if (chapter is null)
                    return;
                generatedChunks.Add(LocateGeneratedQuestions(chunk, chapter));

                var current = Interlocked.Increment(ref progressValue);
                Report(progress, current, chunks.Count, current, chunks.Count, null, null, $"已完成 {current}/{chunks.Count} 个文本块。", ProjectCreationStage.KnowledgeExtraction);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate chunk: {ex.Message}");
            }
        });

        return generatedChunks.ToList();
    }

    private static GeneratedChunk LocateGeneratedQuestions(Chunk chunk, List<Chapter> chapters)
    {
        NormalizeGeneratedQuestions(chapters);
        var source = SourceEvidenceOrder.Normalize(chunk.Content);
        var located = new List<(Question Question, int Position)>();
        foreach (var chapter in chapters)
        {
            var verified = new List<Question>();
            foreach (var question in chapter.Questions ?? [])
            {
                var evidence = (question.SourceExcerpts ?? [])
                    .Where(excerpt => !string.IsNullOrWhiteSpace(excerpt))
                    .Select(excerpt => (Text: excerpt, Position: SourceEvidenceOrder.Find(source, excerpt)))
                    .Where(excerpt => excerpt.Position >= 0)
                    .OrderBy(excerpt => excerpt.Position)
                    .ToList();
                if (evidence.Count == 0)
                {
                    var answer = question.GetCorrectAnswerText();
                    var position = answer.Length >= 6 ? SourceEvidenceOrder.Find(source, answer) : -1;
                    if (position < 0) continue;
                    evidence.Add((answer, position));
                }

                question.SourceExcerpts = evidence.Select(excerpt => excerpt.Text).ToList();
                located.Add((question, evidence[0].Position));
                verified.Add(question);
            }
            chapter.Questions = verified;
        }

        return new GeneratedChunk(chunk.Index, chapters,
            located.OrderBy(item => item.Position).Select(item => item.Question).ToList());
    }

    private static string ExtractJsonContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var trimmed = content.Trim();
        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var jsonStart = trimmed.IndexOf('\n', fenceStart);
            if (jsonStart >= 0)
            {
                var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                trimmed = fenceEnd > jsonStart
                    ? trimmed[(jsonStart + 1)..fenceEnd].Trim()
                    : trimmed[(jsonStart + 1)..].Trim();
            }
        }

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
            return trimmed[arrayStart..(arrayEnd + 1)].Trim();

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
            return trimmed[objectStart..(objectEnd + 1)].Trim();

        return trimmed;
    }

    private static bool ContainsExplicitChapterMarkers(string text)
    {
        return ExplicitChapterHeadingRegex().Matches(text ?? string.Empty)
            .Cast<Match>()
            .Select(match => NormalizeChapterOrdinal(match.Groups["ordinal"].Value))
            .Where(ordinal => !string.IsNullOrWhiteSpace(ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= 2;
    }

    private static void NormalizeGeneratedQuestions(List<Chapter> chapters)
    {
        foreach (var chapter in chapters)
        {
            if (chapter.Questions is null || chapter.Questions.Count == 0)
                continue;

            RepairSplitChoiceOptions(chapter.Questions);
            NormalizeMalformedChoiceQuestions(chapter.Questions);
            NormalizeGeneratedQuestionTypes(chapter.Questions);
            RemoveDeclarativeShortAnswerQuestions(chapter);
        }
    }

    private static void RemoveDeclarativeShortAnswerQuestions(Chapter chapter)
    {
        if (chapter.Questions is null || chapter.Questions.Count == 0)
            return;

        chapter.KnowledgePoints ??= [];
        var validQuestions = new List<Question>();

        foreach (var question in chapter.Questions)
        {
            question.Text = question.Text?.Trim();
            question.CorrectAnswer = question.CorrectAnswer?.Trim();

            if (string.IsNullOrWhiteSpace(question.Text))
                continue;

            if (question.IsSingleChoice || IsValidGeneratedQuestion(question))
            {
                validQuestions.Add(question);
                continue;
            }

            if (TryConvertDeclarativeSentenceToBlank(question))
            {
                validQuestions.Add(question);
                continue;
            }

            chapter.KnowledgePoints.Add(KnowledgePoint.Create(
                CreateKnowledgePointName(question.Text),
                question.Text));
        }

        chapter.Questions = validQuestions;
    }

    private static bool IsValidGeneratedQuestion(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.CorrectAnswer) && question.GetCorrectAnswers().Count == 0)
            return false;

        return question.Type switch
        {
            QuestionType.FillBlank =>
                BlankRegex().Matches(question.Text ?? string.Empty).Count is var blankCount &&
                blankCount > 0 &&
                blankCount == question.GetCorrectAnswers().Count,
            QuestionType.TermDefinition => (question.Text ?? string.Empty).StartsWith("名词解释", StringComparison.Ordinal),
            _ when question.IsEssay => IsValidShortAnswerStem(question.Text ?? string.Empty),
            _ => false
        };
    }

    private static bool IsValidShortAnswerStem(string text)
    {
        if (BlankRegex().IsMatch(text) || text.Contains('?') || text.Contains('？'))
            return true;

        return ShortAnswerPromptRegex().IsMatch(text);
    }

    private static bool TryConvertDeclarativeSentenceToBlank(Question question)
    {
        var text = question.Text?.Trim();
        var answer = question.CorrectAnswer?.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(answer))
            return false;

        if (string.Equals(text, answer, StringComparison.OrdinalIgnoreCase))
            return false;

        var index = text.IndexOf(answer, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var remainingLength = text.Length - answer.Length;
        if (answer.Length < 2 || remainingLength < 4)
            return false;

        question.Text = text.Remove(index, answer.Length).Insert(index, "________");
        question.Type = QuestionType.FillBlank;
        question.CorrectAnswers = [answer];
        return IsValidShortAnswerStem(question.Text);
    }

    private static void NormalizeGeneratedQuestionTypes(List<Question> questions)
    {
        questions.RemoveAll(question => question.Type == QuestionType.TrueFalse);

        foreach (var question in questions)
        {
            question.CorrectAnswer = question.CorrectAnswer?.Trim();
            question.CorrectAnswers = question.CorrectAnswers
                .Where(answer => !string.IsNullOrWhiteSpace(answer))
                .Select(answer => answer.Trim())
                .ToList();

            if (question.Type == QuestionType.FillBlank && question.CorrectAnswers.Count == 0 &&
                !string.IsNullOrWhiteSpace(question.CorrectAnswer))
            {
                question.CorrectAnswers = [question.CorrectAnswer];
            }

            if (question.Type == QuestionType.TermDefinition &&
                !string.IsNullOrWhiteSpace(question.Text) &&
                !question.Text.StartsWith("名词解释", StringComparison.Ordinal))
            {
                question.Text = $"名词解释：{question.Text.Trim().TrimEnd('。', '？', '?')}";
            }

            if (question.Type != QuestionType.SingleChoice)
            {
                question.Options = [];
                question.CorrectOptionIds = [];
            }

            if (question.Type != QuestionType.FillBlank)
                question.CorrectAnswers = [];
        }
    }

    private static string CreateKnowledgePointName(string text)
    {
        var normalized = WhitespaceRegex().Replace(text, " ").Trim();
        return normalized.Length <= 32 ? normalized : normalized[..32];
    }

    private static void RepairSplitChoiceOptions(List<Question> questions)
    {
        for (var i = 0; i <= questions.Count - 5; i++)
        {
            var stem = questions[i];
            if (stem.Options.Count > 0 || LooksLikeOptionOnlyQuestion(stem, null, out _))
                continue;

            var parsedOptions = new List<QuestionOption>();
            var expectedIds = new[] { "A", "B", "C", "D" };
            var matched = true;

            for (var offset = 0; offset < expectedIds.Length; offset++)
            {
                var optionQuestion = questions[i + offset + 1];
                if (!LooksLikeOptionOnlyQuestion(optionQuestion, expectedIds[offset], out var optionText))
                {
                    matched = false;
                    break;
                }

                parsedOptions.Add(new QuestionOption
                {
                    Id = expectedIds[offset],
                    Text = optionText
                });
            }

            if (!matched)
                continue;

            var correctOptionIds = ResolveCorrectOptionIds(stem, parsedOptions);
            if (correctOptionIds.Count == 0)
                continue;

            stem.Type = QuestionType.SingleChoice;
            stem.Options = parsedOptions;
            stem.CorrectOptionIds = correctOptionIds;
            stem.CorrectAnswer = correctOptionIds[0];

            questions.RemoveRange(i + 1, 4);
        }
    }

    private static void NormalizeMalformedChoiceQuestions(List<Question> questions)
    {
        foreach (var question in questions.Where(question => question.Type == QuestionType.SingleChoice))
        {
            question.Options = question.Options
                .Where(option => !string.IsNullOrWhiteSpace(option.Id) && !string.IsNullOrWhiteSpace(option.Text))
                .GroupBy(option => QuestionOption.NormalizeId(option.Id))
                .Select(group => new QuestionOption
                {
                    Id = group.Key,
                    Text = group.First().Text.Trim()
                })
                .ToList();

            question.CorrectOptionIds = ResolveCorrectOptionIds(question, question.Options);

            if (question.Options.Count == 0 || question.CorrectOptionIds.Count == 0)
            {
                question.Type = QuestionType.Essay;
                question.Options = [];
                question.CorrectOptionIds = [];
            }
        }
    }

    private static List<string> ResolveCorrectOptionIds(Question question, List<QuestionOption> options)
    {
        var ids = question.GetCorrectOptionIds()
            .Where(id => options.Any(option => QuestionOption.NormalizeId(option.Id) == id))
            .ToList();

        if (ids.Count > 0)
            return ids;

        var correctAnswer = question.CorrectAnswer?.Trim();
        if (string.IsNullOrWhiteSpace(correctAnswer))
            return [];

        var matchingOption = options.FirstOrDefault(option =>
            string.Equals(option.Text.Trim(), correctAnswer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.DisplayText.Trim(), correctAnswer, StringComparison.OrdinalIgnoreCase));

        return matchingOption is null ? [] : [QuestionOption.NormalizeId(matchingOption.Id)];
    }

    private static bool LooksLikeOptionOnlyQuestion(Question question, string? expectedId, out string optionText)
    {
        optionText = string.Empty;
        if (!string.IsNullOrWhiteSpace(question.CorrectAnswer) || question.Options.Count > 0)
            return false;

        var match = OptionOnlyRegex().Match(question.Text ?? string.Empty);
        if (!match.Success)
            return false;

        var optionId = QuestionOption.NormalizeId(match.Groups["id"].Value);
        if (!string.IsNullOrWhiteSpace(expectedId) && optionId != expectedId)
            return false;

        optionText = match.Groups["text"].Value.Trim();
        return !string.IsNullOrWhiteSpace(optionText);
    }

    [GeneratedRegex(@"^\s*\(?\s*(?<id>[A-Da-d])\s*\)?\s*[\.、:：\)]\s*(?<text>.+?)\s*$")]
    private static partial Regex OptionOnlyRegex();

    [GeneratedRegex(@"_{2,}|＿{2,}|-{3,}")]
    private static partial Regex BlankRegex();

    [GeneratedRegex(@"^\s*(名词解释|简述|说明|分析|比较|阐述|试述|论述|列举|举例|概括|描述|解释|指出|写出|回答|判断|计算|请|问)|(为什么|为何|如何|怎样|哪些|哪种|哪个|什么|是否|能否)")]
    private static partial Regex ShortAnswerPromptRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(杂项题目|其它内容|其他内容|综合|未分类|未命名|通用章节|主题单元\s*\d+|chapter\s*\d+|section\s*\d+|unit\s*\d+|第\s*\d+\s*章|第[一二三四五六七八九十]+\s*章)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericChapterNameRegex();

    [GeneratedRegex(@"当前资料章节[:：]\s*(?<name>[^\r\n]+)")]
    private static partial Regex EmbeddedHeadingRegex();

    [GeneratedRegex(@"[\u4e00-\u9fff]{2,8}|[A-Za-z][A-Za-z0-9\-]{2,}")]
    private static partial Regex KeywordRegex();

    [GeneratedRegex(@"^(问题|答案|题目|章节|知识|内容|资料|学习|复习|概念|定义|特点|作用|原因|过程|分类|包括|主要|相关|进行|分析|说明|简述|论述|the|and|for|with)$", RegexOptions.IgnoreCase)]
    private static partial Regex CommonTopicWordRegex();

    [GeneratedRegex(@"(?m)(?:^|(?<=[\r\n。！？；;：:])\s*)(?<heading>第\s*(?<ordinal>(?:[一二三四五六七八九十百千万〇零两\d]\s*){1,8})章[^\r\n]{0,60})")]
    private static partial Regex ExplicitChapterHeadingRegex();

    private sealed record SourceChapter(string Name, string Content, int? Number = null);

    private sealed record SourceChapterMarker(int Index, string Heading, string Ordinal, int? Number);

    private sealed record SourceChapterSplit(List<SourceChapter> Chapters, bool IsCompleteContinuous)
    {
        public static SourceChapterSplit Empty { get; } = new([], false);
    }

    private sealed record GeneratedChunk(int Index, List<Chapter> Chapters, List<Question> QuestionsInOrder);

    private async Task<List<GeneratedChunk>> MergeChunksAsync(
        List<Chunk> chunks,
        string deepSeekKey,
        MissingStrategy missingStrategy,
        IProgress<ProjectCreationProgress>? progress)
    {
        var result = new List<GeneratedChunk>();

        while (true)
        {
            var generated = await SendChunksAsync(chunks, deepSeekKey, progress);
            var completed = generated.Select(chunk => chunk.Index).ToHashSet();
            var failed = chunks.Where(chunk => !completed.Contains(chunk.Index)).ToList();
            result.AddRange(generated);
            if (failed.Count == 0 || missingStrategy == MissingStrategy.Ignore)
                break;

            chunks = [.. failed];
        }

        return result.OrderBy(chunk => chunk.Index).ToList();
    }

    private async Task<List<Chapter>> ClusterQuestionsAsync(
        string text,
        string deepSeekKey,
        MissingStrategy missingStrategy,
        IProgress<ProjectCreationProgress>? progress,
        IReadOnlyList<string> existingChapterNames,
        bool allowNewChapters)
    {
        var chunks = BuildChunks(text);
        var generated = await MergeChunksAsync(chunks, deepSeekKey, missingStrategy, progress);
        if (generated.All(chunk => chunk.Chapters.Count == 0))
            throw new InvalidOperationException("未能生成可解析的章节题目数据。");

        var clustered = await ClusterGeneratedChaptersAsync(generated.Select(chunk => chunk.Chapters).ToList(),
            deepSeekKey, progress, existingChapterNames, allowNewChapters);
        return LimitChapterCount(FollowSourceOrder(clustered, generated.SelectMany(chunk => chunk.QuestionsInOrder)));
    }

    private static List<Chapter> FollowSourceOrder(IReadOnlyList<Chapter> clustered, IEnumerable<Question> questions)
    {
        var chapterForQuestion = new Dictionary<Question, Chapter>(ReferenceEqualityComparer.Instance);
        foreach (var chapter in clustered)
        {
            foreach (var question in chapter.Questions ?? [])
                chapterForQuestion.TryAdd(question, chapter);
        }

        var ordered = new List<Chapter>();
        var placed = new HashSet<Chapter>(ReferenceEqualityComparer.Instance);
        Chapter? previous = null;
        foreach (var question in questions)
        {
            var chapter = chapterForQuestion[question];
            if (!ReferenceEquals(previous, chapter))
            {
                ordered.Add(new Chapter
                {
                    Name = chapter.Name,
                    Questions = [],
                    KnowledgePoints = placed.Add(chapter) ? chapter.KnowledgePoints : []
                });
                previous = chapter;
            }
            ordered[^1].Questions!.Add(question);
        }

        ordered.AddRange(clustered.Where(chapter => !placed.Contains(chapter) && chapter.KnowledgePoints is { Count: > 0 })
            .Select(chapter => new Chapter { Name = chapter.Name, Questions = [], KnowledgePoints = chapter.KnowledgePoints }));
        return ordered;
    }

    private async Task<List<Chapter>> ClusterGeneratedChaptersAsync(
        List<List<Chapter>> allChapter,
        string deepSeekKey,
        IProgress<ProjectCreationProgress>? progress,
        IReadOnlyList<string> existingChapterNames,
        bool allowNewChapters)
    {
        NormalizeGeneratedChapterNames(allChapter);

        var chapters = new List<Chapter>();
        var chapterNames = new List<string>();
        foreach (var chapter in allChapter)
        {
            foreach (var seg in chapter)
            {
                if (seg.Name is not null)
                    chapterNames.Add(seg.Name);
            }
        }

        Report(progress, null, null, 0, chapterNames.Count, null, null, "正在合并相似章节并整理题目结构。", ProjectCreationStage.TextClustering);
        if (chapterNames.Count == 0)
            return FlattenGeneratedChapters(allChapter);

        var prompt = await BuildChapterClusterPromptAsync(chapterNames, existingChapterNames, allowNewChapters);
        var clusterResult = await RunChatAsync(deepSeekKey, prompt);
        var jsonContent = ExtractJsonContent(clusterResult);
        var cluster = string.IsNullOrWhiteSpace(jsonContent)
            ? []
            : JsonSerializer.Deserialize<List<ChapterCluster>>(jsonContent, JsonOptions) ?? [];

        if (cluster.Count == 0)
            return allowNewChapters
                ? FlattenGeneratedChapters(allChapter)
                : AssignAllToBestExistingChapter(allChapter, existingChapterNames);

        if (IsBadMiscellaneousCluster(cluster, chapterNames.Count))
            return allowNewChapters
                ? FlattenGeneratedChapters(allChapter)
                : AssignAllToBestExistingChapter(allChapter, existingChapterNames);

        for (var clusterIndex = 0; clusterIndex < cluster.Count; clusterIndex++)
        {
            var single = cluster[clusterIndex];
            Report(progress, null, null, clusterIndex + 1, cluster.Count, null, null, "正在写入聚类后的章节。", ProjectCreationStage.TextClustering);
            foreach (var individual in allChapter)
            {
                foreach (var seg in individual)
                {
                    if (single.Chapters is null || seg.Name is null || !single.Chapters.Contains(seg.Name))
                        continue;

                    var currentName = ResolveClusterTargetName(single.UnifiedName, existingChapterNames, allowNewChapters);
                    if (!chapters.Select(c => c.Name).Contains(currentName))
                    {
                        chapters.Add(new Chapter
                        {
                            Name = currentName,
                            Number = single.Number,
                            Questions = [],
                            KnowledgePoints = []
                        });
                    }

                    var current = chapters.Find(c => c.Name == currentName)!;

                    if (seg.Questions is not null)
                        current.Questions!.AddRange(seg.Questions);

                    if (seg.KnowledgePoints is not null)
                        current.KnowledgePoints!.AddRange(seg.KnowledgePoints);
                }
            }
        }

        var assignedNames = cluster
            .SelectMany(item => item.Chapters ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet();
        var leftovers = allChapter
            .SelectMany(group => group)
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter.Name) && !assignedNames.Contains(chapter.Name))
            .ToList();
        foreach (var leftover in leftovers)
        {
            var targetName = allowNewChapters
                ? leftover.Name!
                : FindBestExistingChapterName(leftover.Name, existingChapterNames);
            var current = chapters.FirstOrDefault(chapter => chapter.Name == targetName);
            if (current is null)
            {
                current = new Chapter
                {
                    Name = targetName,
                    Number = chapters.Count + 1,
                    Questions = [],
                    KnowledgePoints = []
                };
                chapters.Add(current);
            }

            if (leftover.Questions is not null)
                current.Questions!.AddRange(leftover.Questions);
            if (leftover.KnowledgePoints is not null)
                current.KnowledgePoints!.AddRange(leftover.KnowledgePoints);
        }

        return chapters;
    }

    private async Task<string> BuildChapterClusterPromptAsync(
        IReadOnlyList<string> chapterNames,
        IReadOnlyList<string> existingChapterNames,
        bool allowNewChapters)
    {
        var basePrompt = await _promptProvider.GetPromptAsync("GenerateChapter.txt");
        var builder = new StringBuilder(basePrompt);
        builder.AppendLine();
        builder.AppendLine("Additional clustering rules:");
        builder.AppendLine("- Merge aggressively. The final number of chapters should usually be 6-12.");
        builder.AppendLine("- Never return more than 12 clusters unless the source explicitly has more than 12 major textbook units.");
        builder.AppendLine("- Names that differ only by wording, examples, section numbers, or granularity must be merged.");
        builder.AppendLine("- Do not create a separate chapter for one isolated concept if it fits any broader chapter.");
        builder.AppendLine("- Candidate names may come from source-file headings. Preserve meaningful source headings whenever possible.");
        builder.AppendLine("- Do not assign many chapters to 杂项题目/其它内容. If more than two candidates are hard to classify, infer real teaching-unit names from their topics instead.");
        if (existingChapterNames.Count > 0)
        {
            builder.AppendLine("- Existing project chapters are listed below. Prefer reusing them exactly.");
            if (!allowNewChapters)
                builder.AppendLine("- You must use only an existing project chapter as uname. Do not invent new uname values.");
            builder.AppendLine("<existing_chapters>");
            foreach (var name in existingChapterNames)
                builder.AppendLine(name);
            builder.AppendLine("</existing_chapters>");
        }
        builder.AppendLine("<candidate_chapters>");
        foreach (var name in chapterNames)
            builder.AppendLine(name);
        builder.AppendLine("</candidate_chapters>");
        return builder.ToString();
    }

    private static string ResolveClusterTargetName(
        string? proposedName,
        IReadOnlyList<string> existingChapterNames,
        bool allowNewChapters)
    {
        if (allowNewChapters || existingChapterNames.Count == 0)
            return IsGenericChapterName(proposedName) ? "综合复习" : proposedName!.Trim();

        var exact = existingChapterNames.FirstOrDefault(name => string.Equals(name, proposedName, StringComparison.OrdinalIgnoreCase));
        return exact ?? FindBestExistingChapterName(proposedName, existingChapterNames);
    }

    private static void NormalizeGeneratedChapterNames(List<List<Chapter>> allChapter)
    {
        var unnamedIndex = 1;
        foreach (var chapter in allChapter.SelectMany(group => group))
        {
            chapter.Name = CleanChapterName(chapter.Name);
            if (!IsGenericChapterName(chapter.Name))
                continue;

            chapter.Name = InferChapterName(chapter) ?? $"主题单元 {unnamedIndex++}";
        }
    }

    private static bool IsBadMiscellaneousCluster(IReadOnlyList<ChapterCluster> cluster, int candidateCount)
    {
        if (candidateCount <= 1)
            return false;

        var miscAssigned = cluster
            .Where(item => IsGenericChapterName(item.UnifiedName))
            .Sum(item => item.Chapters?.Count ?? 0);

        return miscAssigned >= Math.Max(3, candidateCount / 2);
    }

    private static string? InferChapterName(Chapter chapter)
    {
        var heading = TryReadHeadingFromContent(chapter);
        if (!string.IsNullOrWhiteSpace(heading))
            return heading;

        var text = string.Join(' ',
            (chapter.KnowledgePoints ?? []).Select(point => $"{point.Name} {point.ContentMarkdown}")
                .Concat((chapter.Questions ?? []).Select(question => $"{question.Text} {question.GetCorrectAnswerText()}")));
        var keywords = KeywordRegex().Matches(text)
            .Select(match => match.Value.Trim())
            .Where(word => word.Length >= 2)
            .Where(word => !CommonTopicWordRegex().IsMatch(word))
            .GroupBy(word => word)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key.Length)
            .Take(2)
            .Select(group => group.Key)
            .ToList();

        return keywords.Count == 0 ? null : string.Join("与", keywords);
    }

    private static string? TryReadHeadingFromContent(Chapter chapter)
    {
        foreach (var value in (chapter.KnowledgePoints ?? []).Select(point => point.Name)
                     .Concat((chapter.Questions ?? []).Select(question => question.Text)))
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var match = EmbeddedHeadingRegex().Match(value);
            if (match.Success)
                return CleanChapterName(match.Groups["name"].Value);
        }

        return null;
    }

    private static string CleanChapterName(string? name)
    {
        var cleaned = WhitespaceRegex().Replace(name ?? string.Empty, " ").Trim();
        cleaned = cleaned.Trim(' ', '：', ':', '-', '—');
        return cleaned;
    }

    private static bool IsGenericChapterName(string? name)
    {
        var cleaned = CleanChapterName(name);
        if (string.IsNullOrWhiteSpace(cleaned))
            return true;

        return GenericChapterNameRegex().IsMatch(cleaned);
    }

    private static List<Chapter> LimitChapterCount(List<Chapter> chapters)
    {
        var nonEmpty = chapters
            .Where(chapter => chapter.Questions is { Count: > 0 } || chapter.KnowledgePoints is { Count: > 0 })
            .ToList();
        if (nonEmpty.Count <= MaxPreferredChapterCount)
            return RenumberChapters(nonEmpty);

        var targetCount = MaxPreferredChapterCount;
        var limited = new List<Chapter>(targetCount);
        for (var groupIndex = 0; groupIndex < targetCount; groupIndex++)
        {
            var start = groupIndex * nonEmpty.Count / targetCount;
            var end = (groupIndex + 1) * nonEmpty.Count / targetCount;
            var group = nonEmpty.Skip(start).Take(Math.Max(1, end - start)).ToList();
            limited.Add(MergeChapterGroup(group, groupIndex + 1));
        }

        return RenumberChapters(limited);
    }

    private static Chapter MergeChapterGroup(IReadOnlyList<Chapter> group, int number)
    {
        var merged = new Chapter
        {
            Name = CreateMergedChapterName(group, number),
            Number = number,
            Questions = [],
            KnowledgePoints = []
        };

        foreach (var chapter in group)
        {
            if (chapter.Questions is not null)
                merged.Questions.AddRange(chapter.Questions);
            if (chapter.KnowledgePoints is not null)
                merged.KnowledgePoints.AddRange(chapter.KnowledgePoints);
        }

        return merged;
    }

    private static string CreateMergedChapterName(IReadOnlyList<Chapter> group, int number)
    {
        var meaningfulNames = group
            .Select(chapter => CleanChapterName(chapter.Name))
            .Where(name => !IsGenericChapterName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        if (meaningfulNames.Count == 1)
            return meaningfulNames[0];
        if (meaningfulNames.Count > 1)
            return $"{meaningfulNames[0]}与{meaningfulNames[1]}";

        var inferred = InferChapterName(MergeChapterGroupWithoutName(group));
        return string.IsNullOrWhiteSpace(inferred) ? $"综合单元 {number}" : inferred;
    }

    private static Chapter MergeChapterGroupWithoutName(IReadOnlyList<Chapter> group)
    {
        var merged = new Chapter
        {
            Name = string.Empty,
            Questions = [],
            KnowledgePoints = []
        };

        foreach (var chapter in group)
        {
            if (chapter.Questions is not null)
                merged.Questions.AddRange(chapter.Questions);
            if (chapter.KnowledgePoints is not null)
                merged.KnowledgePoints.AddRange(chapter.KnowledgePoints);
        }

        return merged;
    }

    private static List<Chapter> RenumberChapters(List<Chapter> chapters)
    {
        for (var index = 0; index < chapters.Count; index++)
            chapters[index].Number = index + 1;

        return chapters;
    }

    private static List<Chapter> AssignAllToBestExistingChapter(
        List<List<Chapter>> allChapter,
        IReadOnlyList<string> existingChapterNames)
    {
        var result = new List<Chapter>();
        foreach (var segment in allChapter.SelectMany(group => group))
        {
            var targetName = FindBestExistingChapterName(segment.Name, existingChapterNames);
            var current = result.FirstOrDefault(chapter => chapter.Name == targetName);
            if (current is null)
            {
                current = new Chapter
                {
                    Name = targetName,
                    Number = result.Count + 1,
                    Questions = [],
                    KnowledgePoints = []
                };
                result.Add(current);
            }

            if (segment.Questions is not null)
                current.Questions!.AddRange(segment.Questions);
            if (segment.KnowledgePoints is not null)
                current.KnowledgePoints!.AddRange(segment.KnowledgePoints);
        }

        return result;
    }

    private static string FindBestExistingChapterName(string? candidate, IReadOnlyList<string> existingChapterNames)
    {
        if (existingChapterNames.Count == 0)
            return "杂项题目";

        var normalizedCandidate = candidate ?? string.Empty;
        return existingChapterNames
            .OrderByDescending(name => TokenOverlap(normalizedCandidate, name))
            .ThenBy(name => Math.Abs(name.Length - normalizedCandidate.Length))
            .First();
    }

    private static double TokenOverlap(string left, string right)
    {
        var leftTokens = Regex.Matches(left, @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightTokens = Regex.Matches(right, @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0;

        return leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count() /
               (double)leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
    }

    private static List<Chapter> FlattenGeneratedChapters(List<List<Chapter>> generatedChapters)
    {
        var chapters = new List<Chapter>();

        foreach (var chapterGroup in generatedChapters)
        {
            foreach (var segment in chapterGroup)
            {
                if (segment.Questions is null && segment.KnowledgePoints is null)
                    continue;

                var chapterName = string.IsNullOrWhiteSpace(segment.Name) ? "杂项题目" : segment.Name;
                var current = chapters.FirstOrDefault(chapter => chapter.Name == chapterName);
                if (current is null)
                {
                    current = new Chapter
                    {
                        Name = chapterName,
                        Number = chapters.Count + 1,
                        Questions = [],
                        KnowledgePoints = []
                    };
                    chapters.Add(current);
                }

                if (segment.Questions is not null)
                    current.Questions!.AddRange(segment.Questions);

                if (segment.KnowledgePoints is not null)
                    current.KnowledgePoints!.AddRange(segment.KnowledgePoints);
            }
        }

        return chapters;
    }

    private static void Report(
        IProgress<ProjectCreationProgress>? progress,
        int? scanCurrent,
        int? scanTotal,
        int? clusterCurrent,
        int? clusterTotal,
        int? roundCurrent,
        int? roundTotal,
        string? label = null,
        ProjectCreationStage stage = ProjectCreationStage.KnowledgeExtraction,
        bool usesSourceChapters = false)
    {
        progress?.Report(new ProjectCreationProgress(
            scanCurrent ?? 0,
            scanTotal ?? 0,
            clusterCurrent ?? 0,
            clusterTotal ?? 0,
            roundCurrent ?? 0,
            roundTotal ?? 0,
            label,
            stage,
            usesSourceChapters));
    }
}
