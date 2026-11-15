using System.Text.Json;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Infrastructure.Services;

public sealed class RuleReviewService(IAiChatService chat) : IRuleReviewService
{
    public async Task<RuleReviewPreview> PreviewAsync(RuleReviewCategory category, CancellationToken cancellationToken = default)
    {
        Validate(category.Name, category.Prompt);
        cancellationToken.ThrowIfCancellationRequested();
        var name = category.Name.Trim();
        var scope = category.Prompt.Trim();
        var input = JsonSerializer.Serialize(new { category = name, requirements = scope, previewCount = 2 });
        var response = await chat.RunAsync(string.Empty, input, """
            根据用户的聚类大类及 requirements 安排简答题。先列出完整出题计划，再编写计划中的前两道题供用户预览。
            题目数量由你根据考查对象、内容范围和详细程度决定。逐项覆盖用户列出的全部对象及考查内容，将密切相关的内容组织成一道题。
            questionPlan 中每项写明该题的具体考查对象和内容，至少安排两道题。各项应有明确分工，避免重复提问。
            例如植物病理学：用户列出所有病原体名称，并指定形态、寄主、传播途径、致病机制或防治措施；计划应覆盖这些具体要求。
            questions 仅返回计划中的前两道简答题，每题提供详细参考答案，分条说明得分要点。
            题干直接写问题，编号和分数由界面添加。
            输出 JSON：{"questionPlan":["第1题的对象与考查内容","第2题的对象与考查内容","其余题目的对象与考查内容"],
            "questions":[{"text":"题干","correct_answer":"参考答案及得分要点"},{"text":"题干","correct_answer":"参考答案及得分要点"}]}。
            输出完整 JSON，省略其他说明。
            """).WaitAsync(cancellationToken);
        var result = AiJson.Read<PreviewResponse>(response);
        ValidatePlan(result.QuestionPlan);
        PrepareQuestions(result.Questions, 2, []);
        return new(name, scope, result.QuestionPlan.AsReadOnly(), result.Questions.AsReadOnly());
    }

    public async Task<Chapter> CompleteAsync(RuleReviewPreview acceptedPreview, CancellationToken cancellationToken = default)
    {
        Validate(acceptedPreview.Name, acceptedPreview.Prompt);
        ValidatePlan(acceptedPreview.QuestionPlan);
        ValidateQuestions(acceptedPreview.Questions, 2);
        var questions = acceptedPreview.Questions.ToList();
        while (questions.Count < acceptedPreview.QuestionCount)
        {
            var count = Math.Min(8, acceptedPreview.QuestionCount - questions.Count);
            var plan = acceptedPreview.QuestionPlan.Skip(questions.Count).Take(count).ToList();
            questions.AddRange(await GenerateAsync(acceptedPreview.Name, acceptedPreview.Prompt, plan, questions, cancellationToken));
        }
        return new Chapter { Name = acceptedPreview.Name, GenerationPrompt = acceptedPreview.Prompt, Questions = questions, KnowledgePoints = [] };
    }

    private async Task<List<Question>> GenerateAsync(string name, string scope, IReadOnlyList<string> plan, IReadOnlyList<Question> existing, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var input = JsonSerializer.Serialize(new { category = name, requirements = scope, questionPlan = plan, count = plan.Count, existingQuestions = existing.Select(q => q.Text) });
        var response = await chat.RunAsync(string.Empty, input, """
            为用户指定的聚类大类编写简答题。输入 JSON 中的 requirements 描述考查对象、知识范围和提问要求。
            按 questionPlan 的顺序逐项编写题目，每项对应一道题，共 count 道。每题写明具体对象和需要回答的内容，避免重复 existingQuestions。
            例如植物病理学：用户会列明病原体名称，并指定形态、寄主、传播途径、致病机制或防治措施；按这些具体要求提问。
            每题提供详细参考答案，分条说明得分要点。题干直接写问题，编号和分数由界面添加。
            题型统一为简答题。输出 JSON 数组，每项格式为 {"text":"题干","correct_answer":"参考答案及得分要点"}。
            输出完整 JSON，省略其他说明。
            """).WaitAsync(token);
        var questions = AiJson.Read<List<Question>>(response);
        PrepareQuestions(questions, plan.Count, existing);
        return questions;
    }

    private static void PrepareQuestions(IReadOnlyList<Question> questions, int count, IReadOnlyList<Question> existing)
    {
        ValidateQuestions(questions, count);
        var seen = existing.Select(q => q.Text!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var question in questions)
        {
            if (!seen.Add(question.Text!.Trim())) throw new InvalidDataException("模型返回了重复题目，请重试出题。");
            question.Type = QuestionType.Essay;
            question.RequiresAiGrading = true;
        }
    }

    private static void Validate(string name, string prompt)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("请填写聚类大类名称，并说明具体考查对象和提问范围。");
    }

    private static void ValidatePlan(IReadOnlyList<string>? plan)
    {
        if (plan is null || plan.Count < 2 || plan.Any(string.IsNullOrWhiteSpace) ||
            plan.Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Count)
            throw new InvalidDataException("AI 返回的出题计划有误，请重试生成预览。");
    }

    private static void ValidateQuestions(IReadOnlyList<Question>? questions, int count)
    {
        if (questions is null || questions.Count != count || questions.Any(q => q is null || string.IsNullOrWhiteSpace(q.Text) || string.IsNullOrWhiteSpace(q.CorrectAnswer)))
            throw new InvalidDataException($"模型须返回 {count} 道包含题干和参考答案的简答题，请重试出题。");
    }

    private sealed class PreviewResponse
    {
        public List<string> QuestionPlan { get; set; } = [];
        public List<Question> Questions { get; set; } = [];
    }
}
