using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.Services;
using ReciteHelper.Infrastructure.Services;
using ReciteHelper.Infrastructure.Configuration;

const string output = @"D:\Temp\ReciteHelperTask\cfsrs-experiment.json";
if (args.Contains("--live") && args.Contains("--subquestions"))
{
    var pointPrompts = new[] { "说明栈的访问次序和入栈、出栈位置。", "说明队列的访问次序、入队位置和出队位置。", "说明数组的存储方式及访问特点。", "说明单链表的存储方式和访问特点。" };
    var pointReferences = new[] { "栈后进先出，入栈和出栈均在栈顶。", "队列先进先出，在队尾入队、队头出队。", "数组连续存储，可按下标直接访问元素。", "单链表结点通过指针链接，不要求连续存储，访问指定位置通常需从头沿链接遍历。" };
    var pointQuestion = new Question { Text = "分别说明栈、队列、数组、单链表的课程核心特点。", RequiresAiGrading = true,
        CorrectAnswer = string.Join("\n", pointReferences), SourceExcerpts = pointReferences.ToList(),
        AnswerParts = pointPrompts.Select((prompt, i) => new QuestionPart { Key = "part" + (i + 1), Prompt = prompt, ReferenceAnswer = pointReferences[i], Weight = 0.25 }).ToList() };
    var pointAnswer = EssayAnswerLayout.Join(["后进先出，入栈出栈都在栈顶。", "先进先出，从队尾入队。", "不会。", "单链表的结点必须连续存放，所以可以按下标直接访问任意结点，无需沿指针遍历。"]);
    var pointWatch = Stopwatch.StartNew();
    var pointAssessment = await new AiAnswerGrader(new LiveChat("subquestion-model-2026-11-17")).GradeAsync(pointQuestion, pointAnswer);
    var pointOutput = new { elapsedMs = pointWatch.ElapsedMilliseconds, expected = new[] { "AC", "PT", "WA", "FL" },
        actual = pointAssessment.Facets.Select(f => AiGradingPolicy.GetVerdict(f.RawScoreRatio!.Value, f.HasFallacy)).ToArray(),
        assessment = pointAssessment };
    await File.WriteAllTextAsync(@"D:\Temp\ReciteHelperTask\subquestion-live-2026-11-17.json", JsonSerializer.Serialize(pointOutput,
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine(JsonSerializer.Serialize(new { pointOutput.elapsedMs, pointOutput.expected, pointOutput.actual }));
    return;
}
if (args.Contains("--generate"))
{
    var config = new ConfigService(@"D:\Temp\ReciteHelperTask\experiment-config.xml");
    IAiChatService chat = args.Contains("--production")
        ? new AiChatService(new HostedModelService(config), config, new UnusedPi()) : new LiveChat();
    var service = new RuleReviewService(chat);
    var sourceText = "栈采用后进先出规则，入栈和出栈在栈顶进行。\n队列采用先进先出规则，从队尾入队，从队头出队。\n数组使用连续的存储位置，支持按下标访问，插入删除通常需要移动后续元素。\n单链表用结点和指针连接，访问指定位置需从头遍历，插入删除可通过修改链接完成。";
    var watch = Stopwatch.StartNew();
    var category = args.Contains("--single") ? new RuleReviewCategory { Name = "小麦叶锈病", Prompt = "仅考查小麦叶锈病的病症，按讲义中的典型症状作答，不要求扩展。",
        Materials = [new("课程笔记.txt", "发病部产生圆形或近圆形的橘红色夏孢子堆，多发生于叶片正面，后期在叶片背面形成黑褐色冬孢子堆。")] }
        : new RuleReviewCategory { Name = "线性数据结构", Prompt = "考查栈、队列、数组、单链表的基本特点和主要操作。每种结构一道简答题，按本科课程核心要点回答。",
            Materials = [new("课程笔记.txt", sourceText)] };
    var preview = await service.PreviewAsync(category);
    var previewMs = watch.ElapsedMilliseconds;
    var chapter = await service.CompleteAsync(preview);
    var data = new { previewMs, totalMs = watch.ElapsedMilliseconds, plannedCount = preview.QuestionCount,
        questionCount = chapter.Questions!.Count, coverageNote = preview.CoverageNote, questions = chapter.Questions };
    await File.WriteAllTextAsync(args.Contains("--single") ? @"D:\Temp\ReciteHelperTask\cfsrs-single-generation.json" : @"D:\Temp\ReciteHelperTask\cfsrs-generation-experiment.json", JsonSerializer.Serialize(data,
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"Generation: preview={previewMs}ms total={watch.ElapsedMilliseconds}ms questions={chapter.Questions.Count}");
    return;
}
if (args.Contains("--offline") || !args.Contains("--live"))
{
    var preview = new RuleReviewPreview("速度实验", "每个对象一题", Enumerable.Range(0, 38).Select(i => $"范围{i}").ToList(),
        [new() { Text = "范围0", CorrectAnswer = "答案" }, new() { Text = "范围1", CorrectAnswer = "答案" }]);
    var chat = new DelayedChat();
    var watch = Stopwatch.StartNew();
    await new RuleReviewService(chat).CompleteAsync(preview);
    watch.Stop();
    var before = Stopwatch.StartNew();
    for (var batch = 0; batch < 6; batch++) await Task.Delay(200);
    before.Stop();
    double[][] weights = [[0.7, 0.2, 0.1], [0.5, 0.4, 0.1], [0.2, 0.1, 0.7], [0.05, 0.05, 0.9]];
    var parents = Enumerable.Range(0, 4).Select(n => new Question { Text = $"原题{n}", ReviewFacets = Enumerable.Range(0, 3)
        .Select(i => new ReviewFacet { Name = $"切面{n}-{i}", Weight = weights[n][i], NeedsReview = i < 2, LastScoreRatio = i < 2 ? 0 : 1,
            ReviewAvailableAt = DateTime.Now.AddMinutes(-1), Question = new() { Text = $"切面{n}-{i}", ReviewFacetId = Guid.NewGuid(),
                Memory = i < 2 ? null : MemoryRecord.From(FsrsAlgorithm.InitialState(ReviewGrade.Good, SchedulerParameters.Default), DateTime.Now, 1, 0) } }).ToList() }).ToList();
    var project = new Project { Chapters = parents.Select(q => new Chapter { Questions = [q] }).ToList() };
    var ranked = new ReviewGenerator(new FsrsReviewScheduler()).Rank(project, DateTime.Now);
    var result = new { batchDelayMs = 200, batchCount = 6, sequentialMs = before.ElapsedMilliseconds,
        parallelMs = watch.ElapsedMilliseconds, maxConcurrency = chat.MaxActive,
        firstFourParentCountBeforeInterleaving = ranked.OrderByDescending(r => r.CoverageDeficit).Take(4).Select(r => r.ParentIdentity).Distinct().Count(),
        firstFourParentCount = ranked.Take(4).Select(r => r.ParentIdentity).Distinct().Count(),
        ordering = ranked.Select(r => r.Question.Text), threshold = AiGradingPolicy.MasteryThreshold,
        relearningDelayMinutes = CfsrsPolicy.RelearningDelay.TotalMinutes };
    await File.WriteAllTextAsync(@"D:\Temp\ReciteHelperTask\cfsrs-offline-experiment.json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(result));
    return;
}

const string source = "发病部产生圆形或近圆形的橘红色夏孢子堆，多发生于叶片正面，后期在叶片背面形成黑褐色冬孢子堆。";
if (args.Contains("--generated-answer"))
{
    using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(@"D:\Projects\ReciteHelper\docs\experiments\cfsrs-generation-2026-11-16.json"));
    var question = JsonSerializer.Deserialize<Question>(fixture.RootElement.GetProperty("questions")[0].GetRawText())!;
    var parts = EssayAnswerLayout.Parts(question);
    var answer = EssayAnswerLayout.Join(["后进先出。", ""]);
    var assessment = await new AiAnswerGrader(new LiveChat()).GradeAsync(question, answer);
    var project = new Project { TemplateType = ReciteHelper.Core.Enums.ProjectTemplateType.RuleBasedReview,
        Chapters = [new() { Questions = [question] }] };
    var now = DateTime.Now;
    var update = new FacetReviewService(new FsrsReviewScheduler()).RecordAssessment(project, question, assessment, now);
    var availableLater = new ReviewGenerator(new FsrsReviewScheduler()).Rank(project, now.AddMinutes(11));
    var result = new { inputFields = parts.Count, assessment, weakComponents = question.ReviewFacets.Count(f => f.NeedsReview),
        immediatelyAvailable = new ReviewGenerator(new FsrsReviewScheduler()).Rank(project, now).Count,
        laterQuestion = availableLater.Select(row => row.Question.Text), mastery = update?.MasteryAfter };
    await File.WriteAllTextAsync(@"D:\Temp\ReciteHelperTask\cfsrs-generated-answer.json", JsonSerializer.Serialize(result,
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"Generated-question workflow: fields={parts.Count}, score={assessment.ScoreRatio:P0}, pass={assessment.IsCorrect}, weak={result.weakComponents}, readyNow={result.immediatelyAvailable}, readyLater={availableLater.Count}");
    return;
}
Question Symptoms(bool provided = true) => new() { Text = "简述小麦叶锈病的病症。", RequiresAiGrading = true,
    CorrectAnswer = source + " 可进一步补充孢子堆尺寸和破裂方式。", SourceExcerpts = provided ? [source] : [],
    AnswerParts = [new() { Key = "symptoms", Prompt = "简述小麦叶锈病的病症。", ReferenceAnswer = source, Weight = 1 }] };
var multi = new Question { Text = "分别说明栈和队列的基本存取规则。", RequiresAiGrading = true,
    CorrectAnswer = "栈后进先出；队列先进先出。", SourceExcerpts = ["栈采用后进先出规则。队列采用先进先出规则。"], AnswerParts = [
        new() { Key = "stack", Prompt = "栈的存取规则是什么？", ReferenceAnswer = "后进先出。", Weight = 0.5 },
        new() { Key = "queue", Prompt = "队列的存取规则是什么？", ReferenceAnswer = "先进先出。", Weight = 0.5 }] };
var samples = new[] {
    (Name: "用户提供的核心答案", Question: Symptoms(), Answer: source, Pass: true),
    (Name: "同义简述", Question: Symptoms(), Answer: "叶正面多见橙红色、圆形或近圆形的夏孢子堆，后期叶背出现黑褐色冬孢子堆。", Pass: true),
    (Name: "只写一个特征", Question: Symptoms(), Answer: "叶片有橘红色的斑。", Pass: false),
    (Name: "颠倒颜色和时期", Question: Symptoms(), Answer: "先在叶正面形成黑褐色冬孢子堆，后期叶背出现橘红色夏孢子堆。", Pass: false),
    (Name: "空答案", Question: Symptoms(), Answer: "", Pass: false),
    (Name: "未提供资料的核心答案", Question: Symptoms(false), Answer: source, Pass: true),
    (Name: "两问只答第一问", Question: multi, Answer: EssayAnswerLayout.Join(["后放进去的先取出来，也就是后进先出。", ""]), Pass: false)
};
var results = new List<object>();
var grader = new AiAnswerGrader(new LiveChat());
foreach (var sample in samples)
{
    var watch = Stopwatch.StartNew();
    try
    {
        var assessment = await grader.GradeAsync(sample.Question, sample.Answer);
        results.Add(new { sample.Name, sample.Answer, ExpectedPass = sample.Pass, Passed = assessment.IsCorrect,
            MatchesExpectation = assessment.IsCorrect == sample.Pass, Milliseconds = watch.ElapsedMilliseconds, Assessment = assessment });
        Console.WriteLine($"{sample.Name}: raw={assessment.RawScoreRatio:P0}, awarded={assessment.ScoreRatio:P0}, pass={assessment.IsCorrect}, expected={sample.Pass}, ms={watch.ElapsedMilliseconds}");
    }
    catch (Exception ex)
    {
        results.Add(new { sample.Name, Error = ex.Message, Milliseconds = watch.ElapsedMilliseconds });
        Console.WriteLine($"{sample.Name}: ERROR {ex.Message}");
    }
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}

sealed class LiveChat(string outputPrefix = "cfsrs-model") : IAiChatService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private int _calls;
    public async Task<string> RunAsync(string key, string prompt, string? instructions = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));
        request.Content = JsonContent.Create(new { model = "deepseek-chat", messages = new[] {
            new { role = "system", content = instructions ?? "" }, new { role = "user", content = prompt } },
            temperature = 0, max_tokens = 4096, response_format = new { type = instructions?.Contains("输出 JSON 数组") is true ? "text" : "json_object" } });
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
        await File.WriteAllTextAsync($@"D:\Temp\ReciteHelperTask\{outputPrefix}-{Interlocked.Increment(ref _calls)}.json", content);
        return content;
    }
}

sealed class UnusedPi : IPiModelService
{
    public Task<IReadOnlyList<PiOAuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PiChatModel>> GetModelsAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task LoginAsync(string provider, Func<PiAuthPrompt, CancellationToken, Task<string>> prompt, Action<PiAuthEvent> notify, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task LogoutAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string> RunChatAsync(string provider, string model, string prompt, string? instructions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

sealed class DelayedChat : IAiChatService
{
    private int _active;
    public int MaxActive { get; private set; }
    public async Task<string> RunAsync(string key, string prompt, string? instructions = null)
    {
        var active = Interlocked.Increment(ref _active); MaxActive = Math.Max(MaxActive, active);
        try
        {
            await Task.Delay(200);
            using var json = JsonDocument.Parse(prompt);
            return JsonSerializer.Serialize(json.RootElement.GetProperty("questionPlan").EnumerateArray()
                .Select(p => new Question { Text = p.GetString(), CorrectAnswer = "答案" }).ToList());
        }
        finally { Interlocked.Decrement(ref _active); }
    }
}
