# OpenRecite 接入前代码事实核查

研究日期：2026-10-13。基线提交：`71d6f3bf82c1cf656a7003a1b708411f1245081e`。
本文件描述现有实现，不表示已实现本次研究功能。文件哈希见 [local-baseline.json](evidence/local-baseline.json)。

## 知识与题目

- `Core/Entities/Question.cs`：题目、答案、历史、FSRS Memory、SM-2 EF 和 LeitnerBox 已存在，没有稳定的跨项目 ConceptId 或图关系字段。
- `Core/ValueObjects/KnowledgePoint.cs`：有 Name、ContentMarkdown、IsMastered；用名称参与值相等比较。名称不是图节点唯一标识，IsMastered 也不是经过校准的概率。
- `Core/Entities/Chapter.cs`：章节分别持有 Questions 和 KnowledgePoints，二者没有逐题的显式知识点映射。反序列化后清除 ExtensionData，不能把未来图谱状态寄存在这里并期待保留。
- `Core/ValueObjects/FileVectorStore.cs`：本地 JSON 向量列表与余弦排序，属于向量检索，不是知识图谱。知识图谱仍需独立存储节点、边、来源、版本和题目映射。
- `Core/Aggregates/Project.cs`：已有 KnowledgeBasePath 和 SchedulerParameters，当前没有 KnowledgeGraphPath。

## 复习与历史

- `Application/Services/QuizService.cs`：先判答案，再读取 `DateTime.Now`，用二值正确性映射 Again/Good，调用 scheduler，随后追加 ReviewTag。`ResponseSeconds` 包含异步判题所耗时间，不完全等于用户思考时间。
- `Application/Services/FsrsReviewScheduler.cs`：`Record` 更新 Question.Memory 并返回 ReviewOutcome，**本身不追加 ReviewTag**。独立调用它的宿主需要统一的历史写入入口；否则优化器看到的历史不完整。
- `Application/Services/ReviewGenerator.cs`：根据回忆概率与 desiredRetention 分为到期、新题、未到期；排序有随机打散。要做 A/B 或重放，需要额外记录候选集、基线顺序、随机种子和实际呈现顺序，不能依靠事后重算。
- `Core/Entities/ReviewTag.cs`：有作答时间、正确性、相似度、响应秒数、FSRS 状态与 scheduler 字段，没有 SessionId、AttemptId、呈现/提交/反馈时间、图版本、模态窗口或提示暴露标记。
- `Core/Scheduling/ReviewCalendar.cs`：以宿主本地时间 04:00 为复习日边界；UTC 输入转换到系统本地。跨设备和时区实验必须固定 studyTimezone 并记录原始 UTC 时间，不能默默改用服务器时区。
- `Core/Scheduling/FsrsOptimizer.cs`：从 ReviewTag 重放每题历史，以 Grade 或旧 QValue 作为标签；同日记录更新状态，但只对跨日至少一天的记录计损失。不要把情绪、自述、提示阅读或概念推断伪装成作答记录。
- `Application/Services/ReviewPersonalizationService.cs`：当前门槛为至少 800 个可评分记录、已有拟合记录增长 1.25 倍，比较训练目标值，随后重建 Memory。该门槛不是多模态模型样本量依据，训练目标改善也不是时间外验证。

## 持久化与隐私边界

- `Infrastructure/Services/ProjectFileService.cs`：保存 `.rhproj`；导出复制项目目录内文件（排除 output 等既有规则）。**原始音视频不能放在项目目录内**，否则未来导出可能连同项目打包。
- 推荐图谱使用项目侧文件，情绪与实验日志使用独立的本地私有研究目录，并采用显式导出白名单。
- 用户当前电脑的只读枚举显示 Intel Iris Xe（另有虚拟显示设备），约 15.73 GiB 内存，未发现 `nvidia-smi`。这不是性能测试，不能据此宣称支持官方 Emotion-LLaMA 推理。

## 实施前必须解决的接口问题（本轮不改代码）

1. 建立 ProjectId、QuestionId、ConceptId、SessionId、AttemptId；迁移旧数字 Id 时保留映射，并验证唯一性。
2. 显式记录 shownAt、submittedAt、judgedAt、feedbackAt，用户反应时使用 submittedAt − shownAt。
3. 普通复习记录与辅助教学曝光分流；提示后正确不应当作无帮助回忆成功。
4. 所有实验采样、候选排序、图快照和模型版本可重放；一个真实作答只更新一次 Memory、追加一次历史。
5. 不混用不同调度算法的历史做新 FSRS 多模态训练；明确迁移或分别建模。
6. 对图和上下文故障保留完整基线降级；旧版读写可能丢掉未知字段，不能宣称双向无损兼容。
