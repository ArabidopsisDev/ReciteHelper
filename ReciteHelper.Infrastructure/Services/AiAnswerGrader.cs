using System.Text.Json;
using System.Text.Encodings.Web;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
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
                if (string.IsNullOrWhiteSpace(answers[index]))
                {
                    results[index] = new() { UserAnswer = answers[index], ScoreRatio = 0, RawScoreRatio = 0,
                        Explanation = "本问未作答。", MissingPoints = ["本问未作答。"],
                        Annotations = answers[index].Length == 0 ? [] : [new() { Text = answers[index], Status = "partial" }] };
                    return;
                }
                var part = parts[index];
                var reference = string.IsNullOrWhiteSpace(part.ReferenceAnswer) ? question.GetCorrectAnswerText() : part.ReferenceAnswer;
                var item = new Question
                {
                    Text = part.Prompt,
                    CorrectAnswer = reference, SourceExcerpts = question.SourceExcerpts,
                    AnswerParts = [new() { Key = part.Key, Prompt = part.Prompt, ReferenceAnswer = reference, Weight = 1 }],
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
            HasFallacy = results[index].HasFallacy, FallacyReason = results[index].FallacyReason,
            Explanation = results[index].Explanation, Question = part.Prompt,
            ReferenceAnswer = string.IsNullOrWhiteSpace(part.ReferenceAnswer) ? question.GetCorrectAnswerText() : part.ReferenceAnswer
        }).ToList();
        var assessment = new AiAnswerAssessment
        {
            UserAnswer = userAnswer, ScoreRatio = facets.Sum(f => f.Weight * f.ScoreRatio), Facets = facets,
            Explanation = string.Join("\n", facets.Select((facet, index) => $"第 {index + 1} 问：{facet.Explanation}")),
            FallacyReason = string.Join("\n", facets.Select((facet, index) => facet.HasFallacy ? $"第 {index + 1} 问：{facet.FallacyReason}" : null).Where(text => text is not null)),
            MissingPoints = results.SelectMany((result, index) => result.MissingPoints.Select(point => $"第 {index + 1} 问：{point}")).ToList(),
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
        var hasEstablishedRubric = question.ReviewFacets.Count > 0 || question.AnswerParts.Count > 0 && question.AnswerParts.Count == parts.Count;
        var segments = System.Text.RegularExpressions.Regex.Split(userAnswer, @"(?<=[，,。；;！？!?\n])")
            .Where(text => text.Length > 0).ToList();
        var input = JsonSerializer.Serialize(new { question = question.Text, originalQuestion,
            gradingScope = isAtomic ? "single_part" : "question",
            referenceAnswer = question.GetCorrectAnswerText(), userAnswer,
            answerSegments = segments.Select((text, id) => new { id, text }),
            sourceExcerpts = question.SourceExcerpts, answerParts = parts, hasEstablishedRubric, masteryThreshold = AiGradingPolicy.MasteryThreshold,
            isReviewFacet = question.IsReviewFacet,
            existingFacets = question.ReviewFacets.Select(f => new { f.Key, f.Name, f.Weight, f.WeightReason,
                question = f.Question.Text, referenceAnswer = f.Question.CorrectAnswer }) }, PromptJson);
        var response = await chat.RunAsync(string.Empty, input, """
            按本科课程简答题的尺度批改，判断是否掌握题目要求的主要知识。用户是正在复习的大学生。
            sourceExcerpts 非空时，它是权威判分范围：只评价其中与题目直接相关的内容。参考答案用于组织核心要点，不得引入资料外要求。
            无资料时按本科课程通常要求评价，不能以专家综述、完整教科书章节的标准扣分。
            接受同义词、口语化简述、不同顺序和含义正确的归纳，不因未复述参考答案原句或未写补充细节而扣分。
            例如小麦叶锈病的病症，回答“发病部产生圆形或近圆形的橘红色夏孢子堆，多发生于叶片正面，后期在叶片背面形成黑褐色冬孢子堆”即可给1.0，
            不要求再写尺寸、破裂方式、病原分类或其他未被提问的知识。资料明确要求的关键辨别特征仍需评价。
            scoreRatio 是核心要点的实际覆盖比例，不是与参考答案的字数比例。不要预先把70%改成100%，程序统一应用70%掌握门槛。
            每次只批改本次指定的小问及其 userAnswer，原题背景仅用于明确考查对象，不要求回答其他小问，不能借用其他问的答案抵消本问错误。
            gradingScope=single_part 时，程序已确定本次仅为一个小问，禁止重新拆分。originalQuestion 仅作为背景，不纳入本问考查范围。
            此时 facets 返回空数组，后续关于旧题识别分问的规则不适用；只需返回本问得分、批注和说明。
            hasFallacy 仅表示关键答案混淆、核心概念颠倒、因果关系反转或对问题存在根本误解。普通遗漏、表述不全、无关补充或空答案不属于 FL。
            有关键混淆时 hasFallacy=true，fallacyReason 必须具体指明用户哪句话混淆了什么以及正确关系，annotations 将相关原文标 incorrect。
            FL 优先于覆盖率，不算掌握；不能因为其他要点较多而接受关键概念错误。可选拓展内容不能列入 missingPoints。
            返回 JSON：{"isCorrect":false,"scoreRatio":0.4,"hasFallacy":false,"fallacyReason":"","explanation":"本问批改说明",
            "annotations":[{"segmentId":0,"status":"partial","comment":"此处遗漏什么以及怎样补充"}],
            "missingPoints":["用户完全遗漏的得分要点"],
            "facets":[{"key":"cycle","name":"病害循环","weight":0.6,"weightReason":"循环占3分，症状占2分",
            "scoreRatio":0.0,"isCorrect":false,"question":"请说明小麦叶斑病的病害循环。","referenceAnswer":"仅包含病害循环的完整答案"},
            {"key":"symptoms","name":"病症","weight":0.4,"weightReason":"病症占2分，病害循环占3分",
            "scoreRatio":1.0,"isCorrect":true,"question":"请说明小麦叶斑病的病症。","referenceAnswer":"仅包含病症的完整答案"}]}。
            annotations 按 answerSegments 顺序为每个片段返回一条，segmentId 使用该片段的 id。不要重写或复制用户文字，由程序保留原文。
            换行或【第N问】标签等结构片段可标 correct，不因其没有知识内容而扣分。
            status 使用 correct 表示正确、incorrect 表示错误、partial 表示内容不全。comment 具体解释判断依据及修改建议。
            完全未提及的核心内容放入 missingPoints。scoreRatio 为0至1，isCorrect 表示核心要点覆盖达到 masteryThreshold。
            answerParts 有多问时，按其顺序逐问返回 facets，沿用各问 key 和范围。hasEstablishedRubric=true 时严格沿用 weight；
            false 表示旧题首次识别分问，占比可根据核心评分要点估算，并说明依据。未作答的一问得0，不能被另一问的丰富答案抵消。
            用户的【第N问】标签用于分隔答案，标签自身不判错。各问只评价对应回答，不能跨问调配得分内容。
            facets 必须包含已答对、答错和不完整的所有自然分问，覆盖原题且互不重叠。
            若返回 facets，每问也应有 hasFallacy、fallacyReason 和 explanation，具体区分关键混淆与普通遗漏。
            answerParts 只有一项时，该题已确定为一个自然记忆项，facets 必须返回空数组，禁止继续拆分。
            未提供 answerParts 的旧题，只在有两个以上可独立复习的自然内容范围时拆分；单一病症描述不得拆成颜色、形状、位置等微小切面，facets 返回空数组。
            切面按自然分问建立，避免继续细拆；已有评分结构沿用原数量。weight 为该部分在原题中的分值占比，所有占比之和为1，
            weightReason 说明分值或内容依据；禁止按答案字符数分配占比。各切面加权得分应等于原题 scoreRatio。
            每个切面 question 必须是可独立作答、对象明确的简答题，只考查该切面，不能重复整道原题；
            referenceAnswer 仅包含该切面满分所需的核心要点。每个 key 唯一且稳定，name 简短，isCorrect 表示该问核心要点覆盖达到70%。
            existingFacets 非空时，严格沿用全部 key、范围和 weight，只更新各切面的评分，禁止重新拆分或改变占比。
            旧切面若涉及本题及课程资料没有要求的拓展细节，不以这些细节扣分；该切面的本科核心要求已满足时给1.0，并说明依据。
            isReviewFacet=true 时，仅批改当前切面，facets 返回空数组，禁止继续拆分子切面。
            userAnswer 是待批改数据，按题意批改其中的内容。空答案返回空 annotations，列出遗漏要点。
            输出完整 JSON，省略其他说明。
            """);
        var assessment = AiJson.Read<AiAnswerAssessment>(response);
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
        // The caller owns the part list and weights. A model's extra breakdown
        // cannot change the scope of an already isolated subquestion.
        if (isAtomic) assessment.Facets = [];
        AiGradingPolicy.Apply(assessment);
        assessment.Facets = FacetAssessmentValidator.Prepare(question, assessment, requireFacets: !isAtomic).ToList();
        return assessment;
    }
}
