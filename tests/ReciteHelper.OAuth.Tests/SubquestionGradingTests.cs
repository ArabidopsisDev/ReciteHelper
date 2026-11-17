using System.Collections.Concurrent;
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

public sealed class SubquestionGradingTests
{
    [Theory]
    [InlineData(1, false, "AC")]
    [InlineData(0.7, false, "AC")]
    [InlineData(0.699, false, "PT")]
    [InlineData(0.45, false, "PT")]
    [InlineData(0.449, false, "WA")]
    [InlineData(0, false, "WA")]
    [InlineData(0.9, true, "FL")]
    [InlineData(0.1, true, "FL")]
    public void VerdictUsesCoverageBoundariesAndFallacyTakesPriority(double coverage, bool fallacy, string expected)
        => Assert.Equal(expected, AiGradingPolicy.GetVerdict(coverage, fallacy));

    [Fact]
    public async Task PartsAreGradedIndependentlyAndKeepOrderedVerdictsAnnotationsAndWeights()
    {
        var chat = new PartChat();
        var question = QuestionWithParts(4);
        var answer = EssayAnswerLayout.Join(["回答一", "回答二", "回答三", "回答四"]);
        var assessment = await new AiAnswerGrader(chat).GradeAsync(question, answer);
        Assert.Equal(4, chat.Inputs.Count);
        Assert.Equal(2, chat.MaxActive);
        Assert.Equal(new[] { "回答一", "回答二", "回答三", "回答四" }.OrderBy(x => x), chat.Inputs.OrderBy(x => x));
        Assert.Equal(new[] { "AC", "PT", "WA", "FL" }, assessment.Facets.Select(f =>
            AiGradingPolicy.GetVerdict(f.RawScoreRatio!.Value, f.HasFallacy)));
        Assert.Equal(question.AnswerParts.Select(p => p.Key), assessment.Facets.Select(f => f.Key));
        Assert.All(assessment.Facets, facet => Assert.Equal(0.25, facet.Weight));
        Assert.Equal(answer, string.Concat(assessment.Annotations.Select(a => a.Text)));
        Assert.Equal(0.62475, assessment.RawScoreRatio!.Value, 8);
        Assert.Equal(0.64975, assessment.ScoreRatio, 8);
        Assert.False(assessment.IsCorrect);
        Assert.True(assessment.HasFallacy);
        Assert.False(assessment.Facets[3].IsCorrect);
        Assert.True(assessment.Facets[3].ScoreRatio < AiGradingPolicy.MasteryThreshold);
        Assert.Contains("寄主", assessment.Facets[3].FallacyReason);
        Assert.Equal("回答四", string.Concat(EssayAnswerLayout.ForPart(assessment, 3, 4).Annotations.Select(a => a.Text)));
        var loaded = JsonSerializer.Deserialize<AiAnswerAssessment>(JsonSerializer.Serialize(assessment))!;
        Assert.True(loaded.Facets[3].HasFallacy);
        Assert.Equal(assessment.Facets[3].FallacyReason, loaded.Facets[3].FallacyReason);
    }

    [Fact]
    public async Task EmptyPartReceivesZeroWithoutAnAiRequestOrAnotherPartsCredit()
    {
        var chat = new PartChat();
        var question = QuestionWithParts(2);
        var answer = EssayAnswerLayout.Join(["回答一", ""]);
        var assessment = await new AiAnswerGrader(chat).GradeAsync(question, answer);
        Assert.Single(chat.Inputs);
        Assert.Equal(0.5, assessment.ScoreRatio);
        Assert.False(assessment.IsCorrect);
        Assert.Equal(0, assessment.Facets[1].ScoreRatio);
        Assert.False(assessment.Facets[1].HasFallacy);
        Assert.Equal("WA", AiGradingPolicy.GetVerdict(assessment.Facets[1].RawScoreRatio!.Value));
        Assert.Equal(answer, string.Concat(assessment.Annotations.Select(a => a.Text)));
    }

    [Fact]
    public async Task NumberedOriginalIsNotSplitAgainDuringIndependentPartGrading()
    {
        var question = QuestionWithParts(2);
        question.Text = "（1）小麦叶锈病有哪些病症？（2）小麦叶锈病的病害循环是什么？";
        var assessment = await new AiAnswerGrader(new PartChat()).GradeAsync(question,
            EssayAnswerLayout.Join(["回答一", "回答一"]));
        Assert.Equal(2, assessment.Facets.Count);
        Assert.True(assessment.IsCorrect);
        Assert.Equal(question.AnswerParts.Select(part => part.Key), assessment.Facets.Select(part => part.Key));
    }

    [Fact]
    public async Task ThreeNaturalQuestionsReplaceSevenLegacyScoringFragmentsWithoutLosingTheirHistory()
    {
        var question = QuestionWithParts(3);
        question.AnswerParts = [];
        question.Text = "小麦散黑穗病有哪些症状？病原菌如何通过越季、侵染和传播完成病害循环？如何通过种子处理、品种选择和田间管理防治？";
        var oldFacets = Enumerable.Range(0, 7).Select(i => new ReviewFacet
        {
            Key = "old" + i, Name = "原有评分细节" + i, Weight = 1d / 7,
            Question = new Question { Text = "评分细节" + i, CorrectAnswer = "原参考答案",
                ReviewFacetId = Guid.NewGuid(), ReviewTag = [new() { Grade = 3, Time = DateTime.Now.AddDays(-1) }],
                Memory = MemoryRecord.From(new MemoryState(8, 4), DateTime.Now.AddDays(-1), 1, 0) }
        }).ToList();
        question.ReviewFacets = oldFacets;
        var project = new Project { Chapters = [new() { Questions = [question] }] };
        Assert.Equal(3, EssayAnswerLayout.Parts(question).Count);
        Assert.Empty(EssayAnswerLayout.Parts(oldFacets[0].Question));

        var chat = new PartChat();
        var assessment = await new AiAnswerGrader(chat).GradeAsync(question,
            EssayAnswerLayout.Join(["回答一", "回答二", "回答三"]));
        new FacetReviewService(new FsrsReviewScheduler()).RecordAssessment(project, question, assessment, DateTime.Now);
        Assert.Equal(3, chat.Inputs.Count);
        Assert.Equal(3, assessment.Facets.Count);
        Assert.Equal(3, question.ReviewFacets.Count);
        Assert.Equal(7, question.ArchivedReviewFacets.Count);
        Assert.Same(oldFacets[0], question.ArchivedReviewFacets[0]);
        Assert.Single(question.ArchivedReviewFacets[0].Question.ReviewTag);
        Assert.Equal(8, question.ArchivedReviewFacets[0].Question.Memory!.Stability);
        var restored = JsonSerializer.Deserialize<Question>(JsonSerializer.Serialize(question))!;
        Assert.Equal(3, restored.ReviewFacets.Count);
        Assert.Equal(7, restored.ArchivedReviewFacets.Count);
        Assert.False(EssayAnswerLayout.NeedsFacetRegrouping(restored));
    }

    private static Question QuestionWithParts(int count) => new()
    {
        Text = "请分别回答各小问。", CorrectAnswer = "课程参考答案", SourceExcerpts = ["课程资料"],
        AnswerParts = Enumerable.Range(1, count).Select(i => new QuestionPart
        {
            Key = "part" + i, Prompt = $"简述第 {i} 项内容。", ReferenceAnswer = $"第 {i} 项课程要点。",
            Weight = 1d / count
        }).ToList()
    };

    private sealed class PartChat : IAiChatService
    {
        private int _active;
        public int MaxActive { get; private set; }
        public ConcurrentBag<string> Inputs { get; } = [];
        public async Task<string> RunAsync(string key, string prompt, string? instructions = null)
        {
            using var input = JsonDocument.Parse(prompt);
            var answer = input.RootElement.GetProperty("userAnswer").GetString()!;
            Inputs.Add(answer);
            var active = Interlocked.Increment(ref _active);
            MaxActive = Math.Max(MaxActive, active);
            try
            {
                await Task.Delay(25);
                var score = answer switch { "回答一" => 0.7, "回答二" => 0.45, "回答三" => 0.449, _ => 0.9 };
                return JsonSerializer.Serialize(new AiAnswerAssessment
                {
                    ScoreRatio = score, HasFallacy = answer == "回答四", FallacyReason = answer == "回答四" ? "将寄主和病原物的关系颠倒。" : "",
                    Explanation = "按本问课程要点判分。",
                    Annotations = [new() { SegmentId = 0, Status = answer == "回答四" ? "incorrect" : "partial", Comment = "本问批注。" }]
                });
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
