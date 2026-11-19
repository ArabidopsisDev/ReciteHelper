using System.Text.Json;
using System.Text.Encodings.Web;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Services;

namespace ReciteHelper.Infrastructure.Services;

public sealed class RuleReviewService(IAiChatService chat) : IRuleReviewService
{
    private static readonly JsonSerializerOptions PromptJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public async Task<RuleReviewPreview> PreviewAsync(RuleReviewCategory category, CancellationToken cancellationToken = default)
    {
        Validate(category.Name, category.Prompt);
        cancellationToken.ThrowIfCancellationRequested();
        var name = category.Name.Trim();
        var scope = category.Prompt.Trim();
        var existing = category.ExistingQuestionTexts.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()).ToList();
        var materials = RuleReviewMaterialSelector.Select(category.Materials, name + " " + scope);
        var input = JsonSerializer.Serialize(new { category = name, requirements = scope, previewCount = 2,
            existingQuestions = existing, materials = MaterialContext(materials) }, PromptJson);
        var response = await chat.RunAsync(string.Empty, input, """
            根据用户的聚类大类及 requirements 安排简答题。先列出完整出题计划，再编写计划中的前两道题供用户预览。
            面向正在复习本科课程的大学生。题目数量由考查对象和主要内容决定，同一对象密切相关的内容合为一道题。
            考查课程主干和典型特征，避免冷僻细节、细枝末节和变换措辞重复出题。题目应能用几句话回答。
            questionPlan 中每项写明该题的具体考查对象和内容。各项分工明确，避免重复提问。
            范围只有一个自然问题时只安排一道题，不为凑两道而细拆或重复；有足够内容时预览前两道。
            existingQuestions 是该大类已有的题目。追加出题时只安排本次 requirements 要求的新题，避免重复或改写已有题目。
            例如植物病理学：用户列出所有病原体名称，并指定形态、寄主、传播途径、致病机制或防治措施；计划应覆盖这些具体要求。
            materials 非空时，只按资料中明确给出的知识出题和编写答案，不用资料外的专业知识增加要求。
            按考查内容首次出现在资料中的位置排列 questionPlan 和预览题，资料依照 materials 的顺序读取。
            从资料提取与题目直接相关的原文到 source_excerpts，每条必须是资料中的连续原文。资料不足以覆盖的要求在 coverageNote 中说明，勿编造。
            将本题实际引用的文件 sourceId 复制到 source_material_ids 数组。
            materials 为空时，按本科课程常见要求出题，参考答案只列足以拿满分的核心要点，不追求专家级穷尽。
            questions 仅返回计划的前两道简答题（全计划仅一题时返回一题），参考答案简洁。多问用 answer_parts 明确列出每问、参考答案及分值占比；单问也只列一个部分。
            每问填写 answer_nature。症状、术语和主要特征等用 "Conceptual"，防治、分析、论证或应用等用 "Expressive"，同一问兼具两种要求时用 "Conceptual, Expressive"。
            表达问的参考答案给出一组说得通的本科课程示例，避免把其它合理做法或答题思路排除在外。
            每问对应病症、循环、防治等自然内容范围，禁止将一句话拆成多个微小知识碎片。weight 之和为1。
            用户指定的分问数量优先。例如指定三问合成一题，questions 中写一题，answer_parts 中写三项。
            每问中的识别表现、颜色部位、侵染阶段、越冬越夏等是该问内部的评分要点，写进 reference_answer，不新增小问或输入框。
            禁止把一个自然小问再次拆成七八问。只有用户另行明确要求时才细分。
            text 写一个完整的总题干，交代考查对象和范围，例如“说明甘薯茎线虫病的症状、病害循环与防治措施。”这三个方面分别写入 answer_parts。
            每项 prompt 只写对应的一问。text 和 prompt 均不写编号，界面会在总题干下逐项编号。
            “病症与病害循环”是两问，而同一种病症的颜色、形状和发生部位共同构成一问，不再细拆。
            例如小麦叶锈病的病症：答出圆形或近圆形橘红色夏孢子堆、主要在叶正面、后期叶背黑褐色冬孢子堆即可满分，未要求的大小尺寸等不列为必答点。
            输出 JSON：{"questionPlan":["第1题的对象与考查内容","第2题的对象与考查内容","其余题目的对象与考查内容"],
            "coverageNote":"资料不足之处，无则空字符串",
            "questions":[{"text":"题干","correct_answer":"满分所需核心要点","source_excerpts":["资料原文"],"source_material_ids":[],
            "answer_parts":[{"key":"symptoms","prompt":"说明具体对象的病症。","reference_answer":"本科课程核心要点","weight":1,"answer_nature":"Conceptual"}]}]}。
            无资料时 source_excerpts 为空数组。计划每项只用一句话，答案不写长篇解析，以减少等待。
            输出完整 JSON，省略其他说明。
            """).WaitAsync(cancellationToken);
        var result = AiJson.Read<PreviewResponse>(response);
        ValidatePlan(result.QuestionPlan);
        PrepareQuestions(result.Questions, Math.Min(2, result.QuestionPlan.Count), existing, category.Materials, materials);
        var ordered = OrderByMaterials(result.Questions, category.Materials);
        return new(name, scope, result.QuestionPlan.AsReadOnly(), ordered.AsReadOnly())
        { ExistingQuestionTexts = existing.AsReadOnly(), IsAppending = category.IsAppending,
            Materials = category.Materials.ToList(), CoverageNote = result.CoverageNote };
    }

    public async Task<Chapter> CompleteAsync(RuleReviewPreview acceptedPreview, CancellationToken cancellationToken = default)
    {
        Validate(acceptedPreview.Name, acceptedPreview.Prompt);
        ValidatePlan(acceptedPreview.QuestionPlan);
        ValidateQuestions(acceptedPreview.Questions, Math.Min(2, acceptedPreview.QuestionCount));
        ValidateUnique(acceptedPreview.Questions, acceptedPreview.ExistingQuestionTexts);
        var questions = acceptedPreview.Questions.ToList();
        var batches = acceptedPreview.QuestionPlan.Skip(questions.Count).Chunk(6).ToList();
        var materials = RuleReviewMaterialSelector.Select(acceptedPreview.Materials, acceptedPreview.Name + " " + acceptedPreview.Prompt);
        using var concurrency = new SemaphoreSlim(2);
        var generated = await Task.WhenAll(batches.Select(async plan =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try { return await GenerateAsync(acceptedPreview.Name, acceptedPreview.Prompt, plan,
                acceptedPreview.ExistingQuestionTexts.Concat(questions.Select(q => q.Text!)).ToList(), materials, acceptedPreview.Materials, cancellationToken); }
            finally { concurrency.Release(); }
        }));
        questions.AddRange(generated.SelectMany(batch => batch));
        ValidateUnique(questions, acceptedPreview.ExistingQuestionTexts);
        return new Chapter { Name = acceptedPreview.Name, GenerationPrompt = acceptedPreview.Prompt,
            Questions = OrderByMaterials(questions, acceptedPreview.Materials),
            ReviewMaterials = acceptedPreview.Materials.ToList(), KnowledgePoints = [] };
    }

    private async Task<List<Question>> GenerateAsync(string name, string scope, IReadOnlyList<string> plan, IReadOnlyList<string> existing,
        IReadOnlyList<ReviewMaterial> materials, IReadOnlyList<ReviewMaterial> originals, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var input = JsonSerializer.Serialize(new { category = name, requirements = scope, questionPlan = plan, count = plan.Count,
            existingQuestions = existing, materials = MaterialContext(materials) }, PromptJson);
        var response = await chat.RunAsync(string.Empty, input, """
            为用户指定的聚类大类编写简答题。输入 JSON 中的 requirements 描述考查对象、知识范围和提问要求。
            按 questionPlan 的顺序逐项编写题目，每项对应一道题，共 count 道。每题写明具体对象和需要回答的内容，避免重复 existingQuestions。
            例如植物病理学：用户会列明病原体名称，并指定形态、寄主、传播途径、致病机制或防治措施；按这些具体要求提问。
            面向本科课程复习，考查主要概念与典型特征，避免钻细节、冷僻知识及无关拓展。参考答案只列满分所需的核心要点，使用简洁短句。
            materials 非空时严格依据资料，source_excerpts 摘录资料中的连续原文；不得补入资料外的必答点。无资料时采用本科课程通常要求，source_excerpts 返回空数组。
            摘录能定位本题考查内容的连续原文，按资料顺序列出。
            把实际引用的文件 sourceId 复制到 source_material_ids 数组。
            多问用 answer_parts 明确列出各问。单问列一个部分；每问是自然的考查范围，不能拆成词句碎片。weight 之和为1。
            每问填写 answer_nature。症状、术语和主要特征等用 "Conceptual"，防治、分析、论证或应用等用 "Expressive"，同一问兼具两种要求时用 "Conceptual, Expressive"。
            表达问的参考答案给出一组本科课程的合理示例，避免将其写成唯一作答方式。
            严格保留用户指定的自然分问数量和范围。例如症状、发病循环、病害特征只对应三个 answer_parts 项，不能扩成七八项。
            颜色和部位、各侵染阶段、各种防治手段等仅是对应问的内部评分要点，统一写入该问 reference_answer，禁止再次拆成小问。
            text 用一条完整的总题干写出考查对象和各问范围，例如“说明甘薯茎线虫病的症状、病害循环与防治措施。”
            三个小问分别写在 answer_parts 的 prompt 中，界面会自动编号。text 和 prompt 不含编号。
            病症、病害循环是两问；病症中的颜色、形状及发生部位合为一问，不能为了增加数量拆成细节题。
            小麦叶锈病病症答出夏孢子堆形状、橘红色、主要在叶正面及后期叶背黑褐色冬孢子堆即可满分，不额外要求尺寸。
            输出 JSON 数组，每项为 {"text":"总题干","correct_answer":"核心参考答案",
            "source_excerpts":["资料连续原文"],"source_material_ids":[],"answer_parts":[{"key":"稳定标识","prompt":"独立的一问，写明对象","reference_answer":"该问核心要点","weight":1,"answer_nature":"Conceptual"}]}。
            输出完整 JSON，省略其他说明。
            """).WaitAsync(token);
        var questions = AiJson.Read<List<Question>>(response);
        PrepareQuestions(questions, plan.Count, existing, originals, materials);
        return questions;
    }

    private static void PrepareQuestions(IReadOnlyList<Question> questions, int count, IReadOnlyList<string> existing,
        IReadOnlyList<ReviewMaterial> materials, IReadOnlyList<ReviewMaterial> supplied)
    {
        ValidateQuestions(questions, count);
        ValidateUnique(questions, existing);
        var documentIds = supplied.Select(material => material.SourceId).ToHashSet(StringComparer.Ordinal);
        var sources = new RuleReviewSources(materials.Where(material => documentIds.Contains(material.SourceId)));
        var normalized = materials.Select(material => SourceEvidenceOrder.Normalize(material.Content)).ToArray();
        foreach (var question in questions)
        {
            question.Type = QuestionType.ShortAnswer;
            question.RequiresAiGrading = true;
            question.AnswerParts = EssayAnswerLayout.Parts(question).ToList();
            question.Text = EssayAnswerLayout.Stem(question, question.AnswerParts);
            question.AnswerNature = question.AnswerParts.Aggregate(question.AnswerNature, (nature, part) => nature | part.AnswerNature);
            if (question.AnswerParts.Count > 0 && (question.AnswerParts.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Prompt) ||
                string.IsNullOrWhiteSpace(p.ReferenceAnswer) || !double.IsFinite(p.Weight) || p.Weight <= 0) ||
                question.AnswerParts.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != question.AnswerParts.Count))
                throw new InvalidDataException("模型返回的分问或评分要点不完整，请重新生成。");
            if (question.AnswerParts.Count > 0)
            {
                var total = question.AnswerParts.Sum(p => p.Weight);
                foreach (var part in question.AnswerParts) part.Weight /= total;
            }
            if (materials.Count > 0 && (question.SourceExcerpts.Count == 0 || question.SourceExcerpts.Any(quote => string.IsNullOrWhiteSpace(quote) ||
                !normalized.Any(source => SourceEvidenceOrder.Find(source, quote) >= 0))))
                throw new InvalidDataException("题目缺少可核对的资料原文，请修改范围或重新生成。");
            question.SourceMaterialIds = sources.LinkExcerpts(question, question.SourceMaterialIds?.ToHashSet(StringComparer.Ordinal));
        }
        ValidateUnique(questions, existing);
    }

    private static object MaterialContext(IEnumerable<ReviewMaterial> materials)
        => materials.Select(material => new { material.Name, material.Content, sourceId = material.SourceId });

    private static List<Question> OrderByMaterials(List<Question> questions, IReadOnlyList<ReviewMaterial> materials)
    {
        if (materials.Count == 0) return questions;
        var sources = materials.Select(material => SourceEvidenceOrder.Normalize(material.Content)).ToArray();
        return questions.OrderBy(question =>
        {
            for (var index = 0; index < sources.Length; index++)
            {
                var position = question.SourceExcerpts.Select(quote => SourceEvidenceOrder.Find(sources[index], quote))
                    .Where(offset => offset >= 0).DefaultIfEmpty(-1).Min();
                if (position >= 0) return (index, position);
            }
            return (int.MaxValue, int.MaxValue);
        }).ToList();
    }

    private static void ValidateUnique(IReadOnlyList<Question> questions, IReadOnlyList<string> existing)
    {
        var seen = existing.Select(text => text.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var question in questions)
            if (!seen.Add(question.Text!.Trim())) throw new InvalidDataException("模型返回了重复题目，请重试出题。");
    }

    private static void Validate(string name, string prompt)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("请填写聚类大类名称，并说明具体考查对象和提问范围。");
    }

    private static void ValidatePlan(IReadOnlyList<string>? plan)
    {
        if (plan is null || plan.Count < 1 || plan.Any(string.IsNullOrWhiteSpace) ||
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
        public string CoverageNote { get; set; } = string.Empty;
    }
}
