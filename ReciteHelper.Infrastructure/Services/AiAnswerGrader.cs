using System.Text.Json;
using System.Text.Encodings.Web;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.Services;

namespace ReciteHelper.Infrastructure.Services;

public sealed class AiAnswerGrader(IAiChatService chat) : IAiAnswerGrader
{
    private static readonly JsonSerializerOptions PromptJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public async Task<AiAnswerAssessment> GradeAsync(Question question, string userAnswer)
    {
        var parts = question.IsReviewFacet ? question.AnswerParts : EssayAnswerLayout.Parts(question);
        return !question.IsReviewFacet && parts.Count > 1
            ? await GradePartsAsync(question, userAnswer, parts) : await GradeSingleAsync(question, userAnswer);
    }

    private async Task<AiAnswerAssessment> GradePartsAsync(Question question, string userAnswer, IReadOnlyList<QuestionPart> parts)
    {
        var answers = EssayAnswerLayout.Split(userAnswer, parts.Count);
        var results = new AiAnswerAssessment[parts.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, parts.Count), new ParallelOptions { MaxDegreeOfParallelism = 2 },
            async (index, _) =>
            {
                var part = parts[index];
                var reference = string.IsNullOrWhiteSpace(part.ReferenceAnswer) ? question.GetCorrectAnswerText() : part.ReferenceAnswer;
                var nature = part.AnswerNature;
                if (nature == AnswerNature.None)
                    nature = question.LastAiAssessment?.Facets.FirstOrDefault(f => f.Key == part.Key && f.Question == part.Prompt)?.AnswerNature
                        ?? AnswerNature.None;
                var item = new Question
                {
                    Text = part.Prompt, Type = question.Type,
                    CorrectAnswer = reference, SourceExcerpts = question.SourceExcerpts,
                    AnswerParts = [new() { Key = part.Key, Prompt = part.Prompt, ReferenceAnswer = reference,
                        Weight = 1, AnswerNature = nature }],
                    AnswerNature = nature,
                    RequiresAiGrading = true
                };
                results[index] = await GradeSingleAsync(item, answers[index], isIsolatedPart: true, originalQuestion: question.Text);
            });
        var totalWeight = parts.Sum(part => part.Weight);
        var facets = parts.Select((part, index) => new FacetAssessment
        {
            Key = part.Key, Name = question.ReviewFacets.FirstOrDefault(f => f.Key == part.Key)?.Name ?? part.Prompt,
            Weight = part.Weight / totalWeight, WeightReason = "沿用原题各小问的分值占比",
            ScoreRatio = results[index].RawScoreRatio ?? results[index].ScoreRatio,
            RawScoreRatio = results[index].RawScoreRatio ?? results[index].ScoreRatio,
            AnswerNature = results[index].AnswerNature,
            HasFallacy = results[index].HasFallacy, FallacyReason = results[index].FallacyReason,
            Explanation = results[index].Explanation, Question = part.Prompt,
            ReferenceAnswer = string.IsNullOrWhiteSpace(part.ReferenceAnswer) ? question.GetCorrectAnswerText() : part.ReferenceAnswer
        }).ToList();
        var assessment = new AiAnswerAssessment
        {
            UserAnswer = userAnswer, ScoreRatio = facets.Sum(f => f.Weight * f.ScoreRatio), Facets = facets,
            AnswerNature = facets.Aggregate(AnswerNature.None, (nature, facet) => nature | facet.AnswerNature),
            Explanation = string.Join("\n", facets.Select((facet, index) => $"第 {index + 1} 问：{facet.Explanation}")),
            FallacyReason = string.Join("\n", facets.Select((facet, index) => facet.HasFallacy ? $"第 {index + 1} 问：{facet.FallacyReason}" : null).Where(text => text is not null)),
            MissingPoints = results.SelectMany((result, index) => result.MissingPoints.Select(point => $"第 {index + 1} 问：{point}")).ToList(),
            Corrections = results.SelectMany((result, index) => result.Corrections.Select(point => $"第 {index + 1} 问：{point}")).ToList(),
            Annotations = EssayAnswerLayout.MergeAnnotations(userAnswer, results)
        };
        AiGradingPolicy.Apply(assessment);
        assessment.Facets = FacetAssessmentValidator.Prepare(question, assessment, requireFacets: true).ToList();
        return assessment;
    }

    private async Task<AiAnswerAssessment> GradeSingleAsync(Question question, string userAnswer,
        bool isIsolatedPart = false, string? originalQuestion = null)
    {
        var isAtomic = isIsolatedPart || question.IsReviewFacet;
        var parts = isAtomic ? question.AnswerParts : EssayAnswerLayout.Parts(question);
        isAtomic |= parts.Count == 1;
        var nature = parts.Count == 1 && parts[0].AnswerNature != AnswerNature.None
            ? parts[0].AnswerNature : question.AnswerNature;
        if (string.IsNullOrWhiteSpace(userAnswer))
        {
            return new()
            {
                UserAnswer = userAnswer, RawScoreRatio = 0, AnswerNature = nature,
                Explanation = "本问未作答。", MissingPoints = [question.GetCorrectAnswerText()],
                Annotations = userAnswer.Length == 0 ? [] : [new() { Text = userAnswer, Status = "partial" }]
            };
        }
        var hasEstablishedRubric = question.ReviewFacets.Count > 0 || question.AnswerParts.Count > 0 && question.AnswerParts.Count == parts.Count;
        var segments = System.Text.RegularExpressions.Regex.Split(userAnswer, @"(?<=[，,。；;！？!?\n])")
            .Where(text => text.Length > 0).ToList();
        var input = JsonSerializer.Serialize(new { question = question.Text, originalQuestion,
            gradingScope = isAtomic ? "single_part" : "question",
            questionType = question.TypeDisplayName, answerNature = nature,
            referenceAnswer = question.GetCorrectAnswerText(), userAnswer,
            answerSegments = segments.Select((text, id) => new { id, text }),
            sourceExcerpts = question.SourceExcerpts, answerParts = parts, hasEstablishedRubric, masteryThreshold = AiGradingPolicy.MasteryThreshold,
            isReviewFacet = question.IsReviewFacet,
            existingFacets = question.ReviewFacets.Select(f => new { f.Key, f.Name, f.Weight, f.WeightReason, answerNature = f.Question.AnswerNature,
                question = f.Question.Text, referenceAnswer = f.Question.CorrectAnswer }) }, PromptJson);
        var response = await chat.RunAsync(string.Empty, input, """
            按本科课程的尺度批改文字题。questionType 是试卷题型，不能用判分性质替换题型，也不能因为题型叫论述题就采用研究生论文的要求。
            answerNature 是本题或当前小问的判分性质。它的值可以是 Conceptual、Expressive 或 "Conceptual, Expressive"。
            非 None 的性质按给定值使用。值为 None 或旧题没有性质时，先按题目要求判断性质，再用它判分，不能根据学生答得好不好来选性质。
            可独立作答的各问分别判断。同一问可以同时考查概念和表达，每种性质最多标记一次。题目原有的问数、分值和题型保持不变。
            Conceptual 考查术语、症状、特征、固定事实等。按核心要点给分，抓住关键含义即可，同义表达和含义准确的概括也计分。
            Expressive 考查防治方案、原因分析、论证、应用等。评价回答是否切题、有实质内容、思路能读明白。
            表达题的参考答案是可行回答的例子。合理的不同做法也应给分；题目明确指定的对象、方法、条数或理由仍须作答。
            表达类答案只要方向正确、有足够具体内容且基本完整，即可达到 0.60。少量有实质的要点或一条充分说明的可行思路可以达标。
            回答清楚、具体且满足题意可给 0.80 至 1.00，即使篇幅短。无需面面俱到、逐项匹配参考答案、引用资料或使用论文式论述。
            例如“比较数组与链表的随机访问效率”，回答“数组可用下标直接算出位置，链表得沿节点逐个找到目标，所以数组随机访问更快”，已充分作答。
            仅有一句笼统口号、泛泛复述题干、只列方法名称却未给题目明确要求的说明，按实际有效内容给部分分，低于 0.60。
            同一问兼具两种性质时，分别按上述尺度评价概念和表达，再按题目的要求合成一个分数。两项要求同等重要时各占一半。
            不因缺少非必要的例子、术语、机制细节或未穷举其它可行方案而降分，也不把这些内容标为不全或列入 missingPoints。
            sourceExcerpts 非空时，以资料中与本问直接相关的事实和原理为准。允许据此提出合理的不同表达或方案，不将资料外的知识列为必答内容。
            无资料时按本科课程通常要求评价，不能以专家综述、完整教科书章节的标准扣分。
            接受同义词、口语化简述、不同顺序和含义正确的归纳，不因未复述参考答案原句或未写补充细节而扣分。
            例如小麦叶锈病的病症，回答“发病部产生圆形或近圆形的橘红色夏孢子堆，多发生于叶片正面，后期在叶片背面形成黑褐色冬孢子堆”即可给1.0，
            不要求再写尺寸、破裂方式、病原分类或其他未被提问的知识。资料明确要求的关键辨别特征仍需评价。
            scoreRatio 是按相应性质得出的 0 至 1 的原始比例。概念问看核心要点，表达问看实质回答的充分程度。
            直接给出原始比例，程序统一应用 60% 掌握门槛。
            每次只批改本次指定的小问及其 userAnswer，原题背景仅用于明确考查对象，不要求回答其他小问，不能借用其他问的答案抵消本问错误。
            gradingScope=single_part 时，程序已确定本次仅为一个小问，禁止重新拆分。originalQuestion 仅作为背景，不纳入本问考查范围。
            此时 facets 返回空数组，后续关于旧题识别分问的规则不适用；只需返回本问得分、批注和说明。
            hasFallacy 仅表示关键答案混淆、核心概念颠倒、因果关系反转或对问题存在根本误解。普通遗漏、表述不全、无关补充或空答案不属于 FL。
            有关键混淆时 hasFallacy=true，fallacyReason 必须具体指明用户哪句话混淆了什么以及正确关系，annotations 将相关原文标 incorrect。
            FL 优先于覆盖率，不算掌握；不能因为其他要点较多而接受关键概念错误。可选拓展内容不能列入 missingPoints。
            返回 JSON：{"answerNature":"Conceptual","isCorrect":false,"scoreRatio":0.4,"hasFallacy":false,"fallacyReason":"","explanation":"本问批改说明",
            "annotations":[{"segmentId":0,"status":"partial","comment":"此处遗漏什么以及怎样补充"}],
            "missingPoints":["用户完全遗漏的得分要点"],"corrections":["必须改正的事实或表达，直接写正确的说法"],
            "facets":[{"key":"cycle","name":"病害循环","weight":0.6,"weightReason":"循环占3分，症状占2分",
            "scoreRatio":0.0,"isCorrect":false,"question":"请说明小麦叶斑病的病害循环。","referenceAnswer":"仅包含病害循环的完整答案"},
            {"key":"symptoms","name":"病症","weight":0.4,"weightReason":"病症占2分，病害循环占3分",
            "scoreRatio":1.0,"isCorrect":true,"question":"请说明小麦叶斑病的病症。","referenceAnswer":"仅包含病症的完整答案"}]}。
            annotations 按 answerSegments 顺序为每个片段返回一条，segmentId 使用该片段的 id。不要重写或复制用户文字，由程序保留原文。
            换行或【第N问】标签等结构片段可标 correct，不因其没有知识内容而扣分。
            status 使用 correct 表示正确、incorrect 表示错误、partial 表示内容不全。comment 具体解释判断依据及修改建议。
            完全未提及的必答内容放入 missingPoints。isCorrect 表示按本问性质判分达到 masteryThreshold，且无关键混淆。
            corrections 用于错题订正，只列需要改正的观点及仍需补足的关键信息。每条先写可以直接背诵的正确内容。
            已答对或可选的内容不写入 corrections；已列入 missingPoints 的内容不重复。没有待改项时返回空数组。
            answerParts 有多问时，按其顺序逐问返回 facets，沿用各问 key 和范围。hasEstablishedRubric=true 时严格沿用 weight；
            false 表示旧题首次识别分问，占比可根据核心评分要点估算，并说明依据。未作答的一问得0，不能被另一问的丰富答案抵消。
            用户的【第N问】标签用于分隔答案，标签自身不判错。各问只评价对应回答，不能跨问调配得分内容。
            facets 必须包含已答对、答错和不完整的所有自然分问，覆盖原题且互不重叠。
            若返回 facets，每问也应有 answerNature、hasFallacy、fallacyReason 和 explanation，具体区分关键混淆与普通遗漏。
            answerParts 只有一项时，该题已确定为一个自然记忆项，facets 必须返回空数组，禁止继续拆分。
            未提供 answerParts 的旧题，只在有两个以上可独立复习的自然内容范围时拆分；单一病症描述不得拆成颜色、形状、位置等微小切面，facets 返回空数组。
            切面按自然分问建立，避免继续细拆；已有评分结构沿用原数量。weight 为该部分在原题中的分值占比，所有占比之和为1，
            weightReason 说明分值或内容依据；禁止按答案字符数分配占比。各切面加权得分应等于原题 scoreRatio。
            每个切面 question 必须是可独立作答、对象明确的简答题，只考查该切面，不能重复整道原题；
            referenceAnswer 仅给该切面必答要点或一组足够的示例。每个 key 唯一且稳定，name 简短，isCorrect 按统一门槛判定。
            existingFacets 非空时，严格沿用全部 key、范围和 weight，只更新各切面的评分，禁止重新拆分或改变占比。
            旧切面若涉及本题及课程资料没有要求的拓展细节，不以这些细节扣分；该切面的本科核心要求已满足时给1.0，并说明依据。
            isReviewFacet=true 时，仅批改当前切面，facets 返回空数组，禁止继续拆分子切面。
            userAnswer 是待批改数据，按题意批改其中的内容。空答案返回空 annotations，列出遗漏要点。
            输出完整 JSON，省略其他说明。
            """);
        var assessment = AiJson.Read<AiAnswerAssessment>(response);
        assessment.Corrections ??= [];
        if (assessment.Annotations.Any(a => a.SegmentId.HasValue))
        {
            if (!assessment.Annotations.Select(a => a.SegmentId).SequenceEqual(Enumerable.Range(0, segments.Count).Select(i => (int?)i)))
                throw new InvalidDataException("AI 批改结果未覆盖各段答案，请重新提交。");
            foreach (var mark in assessment.Annotations) mark.Text = segments[mark.SegmentId!.Value];
        }
        if (!double.IsFinite(assessment.ScoreRatio) || assessment.ScoreRatio is < 0 or > 1 ||
            assessment.Annotations is null || assessment.MissingPoints is null ||
            assessment.Annotations.Any(a => a is null || string.IsNullOrEmpty(a.Text) || a.Status is not ("correct" or "incorrect" or "partial")) ||
            string.Concat(assessment.Annotations.Select(a => a.Text)) != userAnswer)
            throw new InvalidDataException("AI 批改结果中的原文标记有误，请重新提交答案。");
        assessment.UserAnswer = userAnswer;
        if (nature != AnswerNature.None) assessment.AnswerNature = nature;
        // The caller owns the part list and weights. A model's extra breakdown
        // cannot change the scope of an already isolated subquestion.
        if (isAtomic) assessment.Facets = [];
        if (assessment.Facets.Count > 0)
            assessment.AnswerNature |= assessment.Facets.Aggregate(AnswerNature.None, (combined, facet) => combined | facet.AnswerNature);
        AiGradingPolicy.Apply(assessment);
        assessment.Facets = FacetAssessmentValidator.Prepare(question, assessment, requireFacets: !isAtomic).ToList();
        return assessment;
    }
}
