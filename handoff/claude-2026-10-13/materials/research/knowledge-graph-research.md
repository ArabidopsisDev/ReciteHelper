# OpenRecite 知识图谱接入研究与确定方案

研究日期：2026-10-13。范围：个人电脑、本地中文复习资料；只制定方案，未修改 SDK、未下载模型、未运行产品实验。所有本次手工保存文件均位于 D 盘。

本文中“现状”指实际读取 D:\Projects\OpenRecite 的代码；“来源事实”指文末一手来源支持的事实；“设计决定/假设/验收门槛”是针对本项目的工程选择，并不代表经过用户数据验证。

## 1. 确定决定

采用**本地 SQLite 属性图 + 可追溯证据 + 题目—概念多对多映射**。图谱作为独立能力接入核心，服务于资料定位、相关概念解释、先修补救与学习覆盖。现有 FSRS-6 / SM-2 / Leitner 的记忆更新仍以实际答题事件为准。

建议将来分包：OpenRecite.KnowledgeGraph.Abstractions（跨平台契约）、OpenRecite.KnowledgeGraph（抽取和策略）、OpenRecite.KnowledgeGraph.Sqlite（本地存储）。它们均不依赖 WPF；OpenRecite 主包不强制安装图数据库、LLM 或摄像头组件。

首期选 SQLite，使用 Microsoft.Data.Sqlite；不部署 Neo4j 或 Qdrant，不引入完整 Microsoft GraphRAG 索引流程。采用 GraphRAG 的“实体关联原文后局部检索”的思路，但自定义教育概念模式。图谱可以在离线、无大模型条件下由现有知识点和用户整理建立；大模型抽取只是候选生成器。

**上线顺序固定为：身份与证据 → 已审核图谱检索 → 图谱辅助复习 → 数据支持后的扩展。** 未过质量门槛时，图谱只呈现候选，不参与学习顺序。

## 2. 实际代码现状与缺口

| 已读代码 | 现有行为 | 接入含义 |
|---|---|---|
| `Core/Entities/Question.cs`、`SharedKernel/Entity.cs` | 题型、答案、ReviewTag、FSRS Memory、SM-2 EFValue、LeitnerBox；整型 Id 默认 0，手工 SetId 才成为持久实体 | 不能假设现有每题具有唯一、跨移动稳定的 ID；不能以题干文本或序号单独作主键 |
| `Core/ValueObjects/KnowledgePoint.cs` | Name、ContentMarkdown、IsMastered；按 Name 比较相等；没有 ID、关系、证据 | 当前知识点不是知识图谱；重名并不等于同一概念，IsMastered 不能直接成为图谱概率 |
| `Core/Entities/Chapter.cs` | Questions、KnowledgePoints 分别挂在章节下 | 章节共同归属不能推出某题考察章节内所有知识点 |
| `Core/Aggregates/Project.cs` | KnowledgeBasePath、FileVectorStore，项目级 SchedulerParameters | 现有 knowledge_base 指向向量 JSON；需增加独立 graph sidecar 描述，不能改变原字段含义 |
| `Core/ValueObjects/FileVectorStore.cs` | 全部向量载入内存、余弦遍历排序、每次写整份 JSON；维度不符直接给 0 分 | 可作为过渡检索适配器；不是图数据库；新模块应显式拒绝模型/维度不符而非给伪合法分数 |
| `Core/Entities/VectorEntry.cs` | Id、Text、Vector、SourceFile、EmbeddingModel、Semantics | 没有原文页码/位置、文档版本、内容哈希、图关系或题目关联 |
| `Core/Interfaces/Services/IKnowledgeBaseService.cs` | Build、SearchAsync、EmbedTextsAsync | SDK 有接口；src 中未见实际知识库构建实现，不能声称开箱即用自动构图 |
| `Core/DTOs/KnowledgeBaseMatch.cs` | Title、Content、Score | 缺 source span、edge path、graph version 和检索理由；新返回值需要独立契约 |
| `Application/Services/ReviewGenerator.cs` | Rank 调 PredictRecall；默认到期 → 新题 → 其他，分组内依回忆概率/随机顺序；参数化模式可按 NewQuestionRatio 取样 | 图谱策略应包裹显式选定的基础策略，不偷偷把不同生成方法统一成另一套规则 |
| `Core/Interfaces/Services/IReviewScheduler.cs` | PredictRecall、Record、EnsureMemory；记忆状态按题维护 | 图谱不直接调用 Record，也不把先修或相邻题同步“复习” |
| `Core/Entities/ReviewTag.cs` | 已有 grade、correctness、response_seconds、retrievability、scheduler 等 | 概念视图可投影已发生答题记录；使用 scheduler 字段区分来源，不能混为同一校准概率 |
| `Infrastructure/Services/ProjectFileService.cs` | JSON 项目保存；导出使用 Path.GetTempPath()；整个项目目录打包 | 新图谱存储的快照、导出临时目录须指定到用户选定数据盘；不能照搬默认临时路径满足“禁止 C 盘产物”要求 |
| `OpenRecite.csproj` | net8.0，已经依赖 jieba.NET 0.42.2，LGPL-3.0-only | 可复用中文分词能力；依赖版本与许可证在将来实现时锁定，不因研究改写当前包 |

这些是代码审阅结论，不是自动构建、性能测试或运行期验证结论。

## 3. 图谱用来解决什么

1. **资料证据定位**：复习“事务隔离级别”时找到本项目教材的准确定义和原文位置，区分同名、别名和跨章节概念。
2. **知识结构**：展示某概念属于何类、与哪些概念有关；把“包含”“相关”“先修”保持为不同关系。
3. **错题补救**：答错后，依据已审核的直接先修关系给出一个补救概念及其证据，用户可继续练习已存在的相应题目。
4. **选题覆盖**：在不改变基础复习紧急程度的前提下，避免一次会话同概念题过密，补足已选集合内的不同概念。

不以图上连接强度直接预测记忆，不因为 A→B 就声称学会 A 能掌握 B。先修关系是教学假设；共现、语义相似和章节顺序均不足以自动确定先修。

## 4. 模式、身份与数据契约

### 4.1 身份

使用 project_uid、question_uid、concept_uid、document_uid、chunk_uid、assertion_uid、review_event_uid，值为持久 UUID。UUID 在首次分配后保存；内容哈希只用于变更检测，不能成为可变题目身份。

第一阶段把映射保存在 `knowledge/identity-map.json`：旧的持久整数 ID（若存在且唯一）、章节与题目位置、题型、规范化题干/答案哈希、人工确认的对应关系一起作为定位线索。保存后重新读取一致性校验。只有定位唯一才能自动复用 UUID；重复题、移动+改写导致歧义时进入 unresolved 队列，保留历史，不自动嫁接。

后续 SDK 小版本可为项目、题目添加可空持久 uid 字段，新写入对象必填，老文件迁移分配。仅靠外部 sidecar 无法保证任意外部编辑后的无损匹配，这是明确限制。图谱启用调度前必须解决当前项目所有涉及题目的身份歧义。

### 4.2 核心表/对象

| 对象 | 必需字段 | 说明 |
|---|---|---|
| GraphManifest | project_uid, schema_version, ontology_version, graph_revision, source_snapshot_hash, created_at_utc, builder_version | 区分文件格式版本、关系词表版本和内容修订 |
| Concept | concept_uid, project_uid, preferred_label, language, definition, scope_note, status | 同名不同学科分别存储；language 默认 zh-Hans |
| ConceptAlias | concept_uid, label, language, normalized_label, alias_type | 简繁、缩写、英文名分别保留；标准化不覆盖原标签 |
| ConceptAssertion | assertion_uid, subject_uid, predicate, object_uid, status, confidence_raw?, confidence_calibrated?, provenance_uid | assertion 作为独立记录，支持一个事实多个来源、意见冲突、撤回 |
| QuestionConcept | question_uid, concept_uid, role, weight, status, provenance_uid | role 为 primary / supporting；primary 权重和为 1，supporting 单独保存，不参与题分数重复加权 |
| DocumentVersion | document_uid, version_uid, relative_path, sha256, media_type, extracted_text_hash, extraction_version | 文档替换产生新 version_uid；所有路径相对于项目数据根 |
| EvidenceSpan | span_uid, version_uid, chunk_uid, start_offset, end_offset, page?, section?, quoted_text_hash | offset 对应固定提取文本，采用 Unicode scalar 索引；PDF 页码可空；OCR 坐标另存，不把字符偏移当 PDF 坐标 |
| AssertionEvidence | assertion_uid, span_uid, evidence_role | support / contradict；没有证据的候选与已审核事实明确分开 |
| ExtractionRun | run_uid, model_id?, model_revision?, prompt_hash?, schema_hash, tokenizer_version, input_hash, started_at_utc | 记录规则/用户/模型来源以及重现信息 |
| DecisionAudit | assertion_uid, previous_status, new_status, actor, reason, at_utc | 人工审核与冲突解决的追加审计 |
| ReviewProjection | review_event_uid, question_uid, scheduler, graded_at_utc, result, aided, graph_revision_at_event | 学习视图的可重建投影；不会反向篡改 SDK ReviewTag |

状态为 proposed → accepted / rejected；accepted 可撤为 superseded / invalidated。自动抽取的置信度只作为审核排序线索；若未做概率校准，字段 confidence_calibrated 必须为空。人工事实可没有数值置信度。

### 4.3 允许的概念关系

| predicate | 方向和约束 | 首期用途 |
|---|---|---|
| broader | 子概念 → 上位概念；不从路径自动生成直接边 | 分类导航 |
| related | 无向语义关系；存储时规范化两端顺序 | 有限检索扩展 |
| prerequisite_of | 先修 A → 目标 B；活动子图必须是 DAG；拒绝自环 | 新概念导航、答错后的补救提示 |
| contrasts_with | A 与 B；对称，须有具体区分证据 | 易混知识比较 |

SKOS 提供概念标签、层级、关联的参考语义，且 broader 不被自动定义为传递关系 [S1]。本项目的 prerequisite_of、contrasts_with 是自定义教育关系，不宣称属于 SKOS 标准。查询传递先修时只得到推导路径，不把路径端点伪装成已由原文直接支持的边。DAG 是项目自身的产品约束，不是所有知识领域天然无环的事实；现实中的双向依赖需拆为更小概念或先保留 related。

题目关联、文档片段证据与概念关系使用不同表；不把题目到概念也塞进无类型 edges，以保持约束可校验。关系的多来源不按重复次数盲目提升可信度，转载、同一教材多个块不是独立证据。

## 5. 建图流程与质量控制

**确定流程：导入 → 稳定身份 → 来源切块 → 候选抽取 → 规范化/消歧 → 校验 → 人工审核 → 原子发布。**

- 先导入 Chapter.KnowledgePoints，保留章节作用域、Name 和完整 ContentMarkdown；不因重名自动跨章节合并，不把 IsMastered=true 导入成概率 1。
- 切块按标题和段落，目标 250–400 个 embedding tokenizer token，最多 480，预留模型特殊 token；相邻块 30–50 token 重叠。数字只是起始工程参数，须用检索指标验证。公式、代码块、表格保持整体，并提供超长块切分标识。
- 规则抽取明确术语定义、现有知识点名称；关联题目时联合题干、答案、章节和证据，不只检查是否共现。新概念别名由用户字典优先。
- 可选本地模型只输出满足固定 JSON Schema 的候选概念、有限 predicate、确切 span 引用。一次失败允许重试一次；继续失败则跳过并报告。模型无权编辑历史答案、执行命令、读取任意文件或自创来源路径。
- 校验 span 确实存在于所引用文档版本；语义支持由人审/标注集检查，字符串存在不能证明推论正确。混入文档的指令只是资料，不得成为抽取控制命令。
- 同义合并采取“建议+审核”；同字词、近似向量分数、LLM 表述相近都不能直接跨作用域合并。相似度不是等价关系，也不是已校准的准确率。
- 初版所有 prerequisite_of、contrasts_with 以及所有参与排序的 QuestionConcept 必须人工 accepted。批量生成不等于批量批准。broader / related 可先作为 proposed 展示；未经批准不参与正式检索扩展或策略。
- 发布只增加一个完整 graph_revision；文档变化时标记受影响证据、边和映射为 invalidated，然后重新审核。已有用户编辑保留 provenance 与 override，不被重建覆盖。

将来抽取模型选择与 Emotion-LLaMA 解耦；可使用宿主已配置的本地文本模型，但它不是启用图谱的前提。研究阶段不凭空指定机器一定可运行的 LLM 或报告未经实测的吞吐。

## 6. 本地存储与中文检索

### 6.1 SQLite 方案

目录约定为用户选定的数据盘项目下 `knowledge/graph.sqlite`、`knowledge/identity-map.json`、`knowledge/sources/`、`knowledge/snapshots/`。模型缓存、临时文件、导入工作目录显式传入 `DataRoot` / `WorkRoot`，本用户取 D 盘。应用仍可装在 C 盘，但本研究与将来相关用户数据不得默认落 C 盘。

SQLite 用外键约束、事务、WAL、单写入队列、按 subject/predicate/object 和 question_uid 建索引。局部查询最多 2 跳、100 个概念、200 条边；普通题目提示默认只取 1 跳。SQLite 官方提供递归 CTE 图遍历，且说明应限定查询范围 [S2]。这足以支持首期本地概念图，不代表本机图规模已跑过性能验证。

可选未来服务端适配器仍遵守同一 UUID/版本/证据契约，只有出现跨设备多人写入、远程授权共享或本地性能门槛持续失败时才评估 PostgreSQL/Neo4j。服务端迁移不改变知识关系语义，也不使 SDK 强依赖服务器。

备份使用 SQLite Backup API 或等效一致快照，不能在 WAL 活跃时只拷贝 graph.sqlite；WAL 有配套文件与单写者约束，网络共享目录也不是默认安全的本地部署方式 [S3][S4]。`.rhp` 导出应先备份到 D 盘 WorkRoot，再打包指定快照、manifest 与 sidecar；不直接压缩活跃数据库目录，不把临时日志当静态项目内容。

### 6.2 确定检索路线

1. 精确概念标签/别名匹配，作用域优先。
2. 中文文本经固定版本 jieba.NET + 项目术语词典预分词，分词后空格连接交给 SQLite FTS5 做词汇检索。另保留原文本、字符位置映射和拼写/单字兜底。不能默认 unicode61 会替用户做好中文分词。trigram 可作为补充，但官方指出不足 3 个字符的完整文本匹配有局限，所以不作为唯一中文检索器 [S5]。
3. 可选本地向量通道默认 BAAI/bge-small-zh-v1.5：中文、512 维；按官方建议采用 CLS pooling、L2 normalization，短问题加入查询指令，原文不加入 [S6]。CPU/ONNX 是将来部署候选，导出模型必须对照参考实现验证；本研究没有导出或下载。每次索引锁定模型 revision、维度、pooling、query_prefix 和 tokenizer，不混合不同模型向量。
4. 词汇与向量各取前 20；rank 从 1 开始，用等权 RRF，score(d)=Σ 1/(60+rank_channel(d))；缺失某通道贡献为 0。60 是首期参数，不声称对本资料最优。RRF 原始论文支持用排名融合不同系统，但不证明本项目一定提升 [S7]。
5. 根据候选片段映射的 accepted 概念，扩展直接先修/相关证据，最多补 5 个片段；默认最多 10 个片段进入答案上下文，并做文档/重叠片段去重。上下文总量由实际回答模型 token 上限预算，不能把本地模型上限当无限。
6. 返回原文、来源页/节、概念、关系路径、graph_revision、通道分数和检索理由。RRF 分数与向量分数不可标作“答案可信度”。无充分证据时返回 evidence_insufficient；不以生成解释替代证据。

图谱可在无 embedding 模型时使用前两步；返回降级状态 lexical_only。不悄悄调用云服务。若题目正在进行无提示回忆，不在提交前展示答案、定义、先修证据或关键词；用户请求帮助后应记录 aided=true，将该次帮助练习与无辅助记忆测量区别保存。

Microsoft GraphRAG 局部搜索确实把实体关系和原文片段共同装配为上下文 [S8]。其全局社区摘要论文面向全局语料总结任务 [S9]，不直接证明能提高复习保留率；因此本方案首期不建立社区摘要索引。

## 7. 与 FSRS、传统算法及多模态部分的边界

### 7.1 FSRS 与图谱

按题保存的 S、D、最后复习时刻和 grade 是唯一记忆更新路径。图谱不重写 DesiredRetention，不把情绪、概念相关性或边数代入 S/D，不对未答题调用 Record。图谱影响属于 session policy；原算法仍可独立调用。

首期正式可启用策略为：先运行用户选择的基础生成器，得到最多 N 个 question_uid；图谱不得替换这个集合。仅当基础策略显式允许时，在相同优先级组和相同 recall bucket 内重排。FSRS 桶宽初始为 0.05，bucket=floor(clamp(R,0,1)/0.05)；到期/新题/非到期类别不跨越。桶内贪心优先覆盖本次尚未覆盖的 accepted primary 概念，再按基础顺序、UUID 确定顺序。新题只能重排基础策略已分配的新题位置；图谱缺失时完全沿用基础顺序。

这样图谱只改善已选会话的结构；不因高关联度插入提前复习题，也不会把到期题挤出 N。0.05 是需验证的策略参数，未验证之前默认关闭图谱重排。研究离线实验使用固定随机种子与固定 now；当前 SDK 生成器的 DateTime.Now 和随机并列顺序在未来实现适配时要显式注入/记录。

答错后的直接先修补救单独列为可选练习，明确不占已锁定正式复习名额。若其展示过答案/提示则标 aided；实际发生的新测验才产生复习事件。不要用一道题多概念关联给每个概念制造多个虚拟正确事件。

### 7.2 概念学习视图

首期只显示：accepted 映射题数、已独立作答题数、近期错误题数、最旧复习日期、FSRS 已有题目的回忆概率范围，以及证据不足标识。可显示映射权重加权的已答题 R 均值作为 descriptive_recall，仅是这些题的描述统计，不叫概念掌握概率；同时展示样本覆盖与缺失数量。新题/无记忆不以 R=1 填充。

不训练概念级 FSRS，不把概念当作与题目可交换的记忆项，不由 KnowledgePoint.IsMastered 推断 S/D。将来若研究知识追踪，需要单独的可检验模型与训练集，而非把图图传播称为“已实现迁移学习”。

### 7.3 SM-2 / Leitner

继续走各自到期时间/盒子规则。不存在可与 FSRS 直接比较的校准 R 时，不构造伪 R。正式复习集合完全由传统生成器选择；图谱只在相同到期等级内重排或提供检索/补救。概念报告注明算法和量纲，不合并 FSRS R、SM-2 EF、Leitner box 成一个分数。

### 7.4 与 Emotion-LLaMA

知识图谱保存教材知识，情绪推断保存学习会话观测；分开存储，以 question_uid、review_event_uid、session_uid 关联。情绪结果不得成为教材事实、先修边或“概念已掌握”的证据。若未来有多模态会话策略，先遵守其数据质量和缺失模态门控，再在上述不改变记忆更新的边界内工作。情绪延迟、不可靠或关闭摄像头时不阻断图谱检索或普通复习。

## 8. 迁移、发布和回滚

- V1：新增图谱独立 sidecar，不重写原知识库 JSON；导入现有知识点/题目，人工处理身份歧义，基础 SDK 使用者不必迁移。
- V1.1：稳定 uid 加入可选序列化字段；迁移工具读旧写新、事务分配、保留来源版本。schema_version 与 ontology_version 独立变化，迁移前一致快照；应用旧版本遇到未知 graph schema 明确禁用插件，原复习照常工作。
- V1.2：发布 accepted 概念/边/题目映射；启用有来源局部检索；记录 graph_revision，能按版本重现解释。
- V1.3：通过检索与调度门槛后，让用户显式启用图谱重排；默认配置和基础算法输出可复现地对照。
- 改变 embedding 时新建索引版本、全量重嵌入、完成后原子切换；不得直接混写向量。原文变更只重做受影响版本与断言，保留人工审核记录。
- 发生损坏、身份歧义或 revision 不匹配时：禁用图谱策略并回退基础生成器；不删除任何原复习记录。失败构建不得覆盖上一份可用图。

未来契约建议包含 IKnowledgeGraphRepository（读取版本快照/提交已审核变更）、IKnowledgeGraphBuilder（候选构建）、IKnowledgeRetriever（带证据检索）、IQuestionIdentityResolver、IReviewSessionPolicy（输入基础计划/图快照，输出相同 question_uid 集合的带理由计划）。这些名称仅是设计，当前仓库尚无上述实现。

## 9. 验证计划与可判定门槛

本阶段没有真实图谱数据、标注集合或性能测量。以下是以后实施必须达到的验收定义，不能写成已有实验结果。

| 实验 | 固定设计 | 初始验收门槛 |
|---|---|---|
| 构图质量 | 至少 3 类中文资料；人工双标 200 条概念/关系和 200 条题目映射；争议仲裁；同源段落同组划分训练/验证 | accepted prerequisite 精确率≥95%；题目主要概念 micro-F1≥0.85；证据引用有效率100%；接受子图自环/环为0 |
| 检索消融 | 至少100个真实问法，按文档组隔离；A词汇、B词汇+向量、C词汇+向量+图；含单字/双字术语、同名概念、反例、跨节问题 | C 的 Recall@5 不低于B；有关系查询的 nDCG@5 相对B提升≥5%；总体不下降超过2%；人工证据支持率≥95% |
| 身份与升级 | 重名题、复制题、移动章节、改题干/答案、外部编辑、旧档、重建、崩溃、重复迁移 | 0 次静默历史串题；有歧义必须阻止图谱策略；重复迁移幂等 |
| 调度安全 | 固定now/随机种子回放多个项目，分别 FSRS/SM-2/Leitner；启用/关闭图谱对照 | 题目集合完全相同；无跨优先级/桶交换；无新增 Record；图缺失时基础结果一致 |
| 本地资源 | 预设10,000概念、100,000关系、20,000片段及真实样本，记录CPU/RAM/磁盘型号；冷/热分开 | 不含LLM/embedding的图扩展 p95<100ms；词汇+图检索 p95<300ms；目标均为待测工程预算 |
| 教学收益 | 同人交叉、不同概念组、固定学习时长，随机平衡策略顺序；先按日/会话随机分配作为先导，避免简单前后比较受练习趋势混淆，7/30日延迟无提示测验；后续样本量按先导方差做功效分析 | 主要指标为每分钟学习的延迟正确率；图谱不能仅凭点击率、满意度或顺序变化获判有效 |

未达到关系精确率时：继续人工图谱；未达到检索门槛时：保留B，不启用图扩展；未达到调度安全或教学门槛时：保留检索，重排继续关闭。小样本同人试验只支持个体效果，不能推广为人群因果结论。离线回放能检验策略约束，无法直接证明新排序的真实学习收益。

## 10. 主要风险与处置

- **先修幻觉/语义混淆**：有限关系、人工审核、保留 evidence 与矛盾断言；未确认关系不得影响会话。
- **题目身份错绑**：持久 UUID；不唯一匹配必须人工处理；保留历史，拒绝无声合并。
- **泄题导致回忆统计污染**：提示放在提交后或明确请求后，保存 aided 标志；辅助练习不混为独立测验。
- **中文术语与短查询**：术语词典+分词，单字/双字专用路径；不用 trigram 单点方案。
- **模型分数虚假确定性**：embedding/LLM confidence 与事实可信度分开；阈值由标注集校准。
- **繁重构图**：先手工/规则入口、增量候选、局部图；无须初始构建整个语料世界图谱。
- **多模态数据侵入教材图**：情绪会话库与知识图物理/逻辑分开；只以稳定事件ID连接。
- **备份不完整/C盘默认缓存**：注入D盘工作根；SQLite一致快照导出；不直接执行当前默认临时导出作为实现范本。

## 11. 一手来源与结论边界

以下均于 2026-10-13 访问；链接而非复制全文，论文与文档的结论不等于本项目完成验证。

- [S1] W3C, SKOS Simple Knowledge Organization System Primer：标签、概念、broader/narrower/related 以及层级传递语义。https://www.w3.org/TR/skos-primer/ 。本方案借鉴概念词表；关系审批、教育先修与DAG是自定义决定。
- [S2] SQLite, The WITH Clause：递归查询、图遍历与限制搜索范围。https://sqlite.org/lang_with.html 。支持用SQL表达本方案局部图查询；性能预算须实测。
- [S3] SQLite, Write-Ahead Logging：https://sqlite.org/wal.html 。WAL读写/单写者及文件约束是来源事实；本方案据此选单写队列与本地目录。
- [S4] SQLite, Online Backup API：https://sqlite.org/backup.html 。来源支持一致备份机制；本方案决定由D盘WorkRoot生成项目包快照。
- [S5] SQLite, FTS5 Extension：https://sqlite.org/fts5.html 。来源说明tokenizer与trigram短查询限制；复用jieba.NET是仓库现状支持的项目决定。
- [S6] BAAI official model card, bge-small-zh-v1.5：https://huggingface.co/BAAI/bge-small-zh-v1.5 。中文模型、512维、MIT、查询指令、CLS pooling/归一化及相似度阈值谨慎使用来自官方卡；CPU部署和本资料质量尚未测量。
- [S7] Cormack, Clarke, Büttcher, Reciprocal Rank Fusion outperforms Condorcet and individual Rank Learning Methods, SIGIR 2009：https://cormack.uwaterloo.ca/cormacksigir09-rrf.pdf 。排名融合方法的原始论文；k=60在本项目是否合适须消融实验。
- [S8] Microsoft GraphRAG, Local Search：https://microsoft.github.io/graphrag/query/local_search/ 。来源说明实体、关系和原文联合检索；本方案选择局部教育图结构而非整套依赖。
- [S9] Edge et al., From Local to Global: A Graph RAG Approach to Query-Focused Summarization：https://arxiv.org/abs/2404.16130 。研究全局总结任务；不是教育学习增益证据。
- [S10] W3C, PROV-O：https://www.w3.org/TR/prov-o/ 。Entity/Activity/Agent、派生、归属、引用提供溯源参考；SQLite evidence/audit是轻量映射，不宣称完整实现OWL/RDF推理。
- [S11] Microsoft, Microsoft.Data.Sqlite overview：https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/ 。提供.NET SQLite访问方式；将来需锁版本并确认目标运行时原生库部署。

## 12. 实施交付清单

将来实施按依赖交付：① UUID迁移与来源版本；② SQLite事务/快照和版本契约；③ 人工建图/候选审核；④ 带证据词汇检索；⑤ 可选向量混合与图扩展；⑥ 固定基础计划的图谱会话适配；⑦ 标注集、回放与真实学习实验。

完成①–④即可交付本地可用知识图谱；⑤–⑥分别经门槛启用。与情绪融合可并行设计，但不能跳过身份、证据、学习测量这三个前置条件。
