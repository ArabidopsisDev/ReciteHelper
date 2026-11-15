# Emotion-LLaMA 接入研究：决定与证据

研究日期：2026-10-13。用途：个人电脑本地复习。本文是方案研究，未安装依赖、未下载权重、未运行模型，所有性能预期均未经本机实测。生产代码未修改。

## 1. 冻结决定

1. 当前电脑以 FSRS 原始评分和本地会话事件为主；Emotion-LLaMA 默认关闭，不能成为复习可用性的前置条件。主代理只读硬件检查报告为 Intel Iris Xe、物理内存约 15.73 GiB、未发现 NVIDIA/CUDA。不能把“7B 可以量化”当作该多模态栈已支持本机 CPU 的证据。
2. Emotion-LLaMA 采用可选、异步、进程外的研究适配器。若将来具备本人控制的 CUDA 电脑，可在其上离线分析经本人选择的短片段；默认不向公共 Hugging Face 演示站发送学习录像。当前机器不为该项采购或安装任何服务。
3. 第一个复现实验固定官方 v1 的已发布 demo 路径，并诚实命名为 `emotion-llama-v1-demo-firstframe-audio`。不能声称复现论文完整多视角模型。恢复完整特征提取是独立实验，不与 demo 混算。
4. 情绪输出只进入会话辅助策略和观测日志；不得据此直接把 Again 改成 Hard、修改答题正确性或乘改 FSRS Stability/Difficulty。学习效益需要新的延迟回忆实验。
5. OpenRecite LGPL NuGet 仅定义中立观测契约。第三方 Python 模型、权重、训练数据、运行时依赖均独立管理；不能把这些资产一概标为 LGPL。

## 2. 确切对象和可复现快照

官方 v1 是 **Emotion-LLaMA: Multimodal Emotion Recognition and Reasoning with Instruction Tuning**，NeurIPS 2024，作者仓库 [ZebangCheng/Emotion-LLaMA](https://github.com/ZebangCheng/Emotion-LLaMA)，论文 [arXiv v2](https://arxiv.org/html/2406.11161v2)，[会议记录](https://proceedings.neurips.cc/paper_files/paper/2024/hash/c7f43ada17acc234f568dc66da527418-Abstract-Conference.html)。不要与 `xxtars/EMO-LLaMA`（另一篇面部情绪论文）混淆。

本次固定仓库 commit：`6f8d05087b2a91e445ad986545977fda7eb1b6a4`，提交时间 2026-09-23；抓取时间见 `evidence/emotion-llama/manifest.json`。快照保存了 README、三个许可文件、模型配置、模型构建、conversation、runtime、批量接口和文档。manifest 给出完整固定 URL 和 SHA-256。原仓库文件只作为研究证据，不是本项目新增实现。

已确认还有 2026 年作者后续论文 [Emotion-LLaMAv2 and MMEVerse](https://arxiv.org/html/2601.16449v1)，其正文直接指向 [ooochen-30/Emotion-LLaMA-v2](https://github.com/ooochen-30/Emotion-LLaMA-v2)。本次 v2 快照 commit 为 `5f0a408b193e9b94f1cc53470de707de125fea40`，见 `v2-manifest.json`。v2 改用 Whisper-Large-v3、EVA、Conv-Attention 和 MMEVerse；它仍基于 Llama-2-7B。它不是轻量 CPU 替代品；其仓库仍包含同样的 MER EULA，不能据新版本名称假定许可问题消失。冻结 v1 实验，v2 只列为以后同数据的独立对照。

## 3. 模型真实数据流与 demo 陷阱

论文完整架构：视频经人脸检测、裁剪、对齐和 16 帧抽样；EVA 编码 448×448 全局图像，MAE 编码局部人脸，VideoMAE 编码时序人脸，HuBERT-Chinese-large 编码音频；线性投影到 LLaMA-2-chat 7B 的表示空间，用 LoRA 调整语言模型。训练特征预先提取，官方 README 明确训练需要 FaceMAE、VideoMAE、HuBERT 三个特征目录。文本是外部字幕/转录加入 prompt，HuBERT 本身不生成字幕。[论文方法及实现](https://arxiv.org/html/2406.11161v2#S3)，[官方训练说明](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/README.md#-training)。

**已读源码发现：公开 demo 与完整论文输入不同。** 固定版本 [conversation.py](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/minigpt4/conversation/conversation.py#L287) 的 `encode_img` 只读取视频第一帧，提取 HuBERT 音频均值；第 325 行创建两组零向量作为局部/时序特征，再拼入音频。`EmotionLLaMARuntime.analyze` 复用这条 Chat 路径，所以 CLI 的 batch 也没有自动恢复 MAE、VideoMAE 或 peak frame。批量处理是模型复用下的串行逐条推理，不是张量批处理。

工程后果：

- 所有结果必须附带 `pipelineId`，区分 `v1-demo-firstframe-audio` 与将来的 `v1-full-precomputed`。两个流程不能混用校准器。
- 不把原始视频里出现的全模态等同于模型实际利用的全模态。demo 的 `effectiveModalities` 是首帧视觉、音频和显式 prompt 文本。
- `analyze(video_path,prompt)` 返回文本，不返回原生已校准情绪概率。不可把文字里的“90% sure”直接当概率。
- 音频经临时 WAV 以 16 kHz 提取。没有音轨、无可读首帧必须在适配器预检并返回不可用，不合成空白人脸或静音伪装完整输入。

官方运行时及接口证据：[runtime.py](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/minigpt4/inference/runtime.py)，[batch 文档](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/docs/api/batch-inference.md)。

## 4. 权重、语言与资源条件

公开 demo 至少需要：Llama-2-7b-chat-hf、Emotion-LLaMA demo checkpoint、HuBERT-Chinese-large，以及 EVA 初始化所需资产；完整论文路线额外需要对应 MAE/VideoMAE 权重和一致的特征提取流程。MiniGPT-v2 的预训练 checkpoint 是再训练时的上游初始化要求，不应重复宣称所有最终 checkpoint 推理都要重新运行其训练。

[官方 getting-started](https://zebangcheng.github.io/Emotion-LLaMA/getting-started/) 给出的 NVIDIA 24 GB 明确括注为 **for training**，RAM 建议 32 GB、存储至少 50 GB。论文报告 4×A100 训练。两者都不是可引用的推理最低显存或端到端延迟保证。7B 单看 FP16 权重大约 14 GB 是参数量算术估算，尚未含视觉/音频编码器、KV cache、激活和框架；本机 15.73 GiB 不构成可靠 FP16 部署条件。

[app.py](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/app.py#L53) 硬编码 CUDA；[demo.yaml](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/eval_configs/demo.yaml) 打开 `low_resource`；[base_model.py](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/minigpt4/models/base_model.py#L172) 对应 8-bit 和 GPU device map。runtime 即使能解析 CPU device，也不等于带权重整栈通过 CPU 运行。未实测任何推理速度，也不承诺实时监测。

[HuBERT 模型卡](https://huggingface.co/TencentGameMate/chinese-hubert-large) 表明其是中文音频特征模型，预训练于 WenetSpeech，并不附有现成转录 tokenizer。Llama-2-chat [原始模型卡](https://huggingface.co/meta-llama/Llama-2-7b-chat-hf) 以英语为主要场景。中文语音编码器与英语 prompt 同时存在，不能推出“中文学习情绪已验证”。中文学习材料、英文朗读、沉默思考、口音、镜头高度、光照以及知识文本的情绪词都需分别验证；翻译字幕还会引入额外误差。

当前可实施的是：本机继续正常复习并形成事件日志；若本人以后启用离线实验，再将本人选择的片段复制到本人控制的 GPU 环境。异步观测超时/不可用时立即回到 FSRS 基线，绝不阻塞评分或等模型作答。

## 5. 许可边界：代码、数据、权重要分开

| 资产 | 核实来源 | 本方案处理 |
|---|---|---|
| Emotion-LLaMA v1 源码 | [LICENSE.md](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/LICENSE.md)：BSD-3-Clause | 可独立适配，保留版权和声明；不改成 LGPL 第三方代码 |
| MiniGPT-4 相关源码 | 同仓库 `LICENSE_MiniGPT4.md`：BSD-3-Clause | 单列第三方声明 |
| MER 数据、标注、数据衍生模型 | [LICENSE_EULA.md](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/LICENSE_EULA.md) | 数据仅学术研究，派生模型仅科学、非商业应用；个人使用本身不是自动满足“scientific”条款的证明 |
| Llama-2-7b-chat-hf | [Meta 模型卡及链接许可](https://huggingface.co/meta-llama/Llama-2-7b-chat-hf) | 受 Llama 2 Community License 和使用政策约束；不是 BSD/LGPL |
| HuBERT-Chinese-large | [模型卡](https://huggingface.co/TencentGameMate/chinese-hubert-large)：MIT 标记 | 保留相应许可；不推及整个组合 |
| Emotion-LLaMA checkpoint | 作者 README 指向下载；模型以 MER 训练 | 不能只凭仓库 BSD 推断 checkpoint 可无条件商业分发；科学实验以适用许可为界 |
| v2 源码及 checkpoint | v2 BSD LICENSE；同仓库仍有 MER EULA，模型 zoo 指向作者 HF | 本次未确认所有混合训练数据对权重的完整许可链，故不作为许可更宽松替代 |

EULA 的关键原文（22 词）：“Any models, derived from data contained in MER may only be used for scientific, non-commercial applications.” 它还限制第三方转交数据库和标注等衍生文件。本研究不宣称其对本人所有日常用途已授权。确定方案是：保持插件边界、默认关闭权重，若模型要成为日常发布功能，先取得该用途的明确授权，或改用许可清楚且在本域评估过的模型。此项是已读条款产生的具体上线条件，不影响当前无模型的本地复习。

## 6. 发给融合层的信号契约

这是拟定数据契约，不是已经存在的 C# API。

| 字段 | 意义及约束 |
|---|---|
| `observationId/sessionId/reviewEventId` | 唯一观测、会话和可选关联答题事件；重试幂等 |
| `windowStartUtc/windowEndUtc/producedAtUtc` | 原始采样窗口与完成时间分开；防止异步结果冒充当前状态 |
| `modelId/revision/pipelineId/calibrationId` | 权重版本、代码 SHA、真实预处理和校准器版本 |
| `inputModalities/effectiveModalities/modalityMask` | 采到的模态、实际使用的模态、缺失状态分别记录 |
| `labelSet/rawScores/calibratedProbabilities` | 初始固定六类 happy/sad/neutral/angry/worried/surprise；未计算或未校准用 null；各分数注明是 token likelihood 或分类分数 |
| `validity/status/reasonCodes` | `ok/uncalibrated/insufficient_signal/unsupported_modality/error/stale`，多脸、无声、噪声、字幕不可靠分别记录 |
| `qualityMetrics` | 有声覆盖率、脸部可见率、音频剪切/视觉模糊等可测量质量；不把它命名为心理状态 |
| `selfReport` | 本人可选自评，与模型观测分栏；禁止自评悄悄变成模型真值 |
| `latencyMs/policyEligibility` | 实际时延与是否可用于当前会话动作；离线结果默认不能实时动作 |

不设 `fatigueProbability`、`attentionScore`、`masteryScore`。模型情绪类别不等同于疲劳、注意力或知识掌握；neutral 不等同于理解，worried 不等同于遗忘。`valence/arousal` 只有独立监督验证后才能作为额外字段，不能由六类标签武断映射出连续值。

## 7. 置信度、缺失模态与学习域验证

校准先决条件：由固定的情绪标签候选读取完整序列 likelihood，或专门训练可审计分类头；开放式说明只保留研究阅读价值。按本人不同日期/会话划分训练、校准、保留集，不能随机把同一录像近邻帧分到不同集。数据少时不微调 7B，先比较原始观测与不含情绪的基线。

可采用 temperature scaling，但需用保留校准集拟合，不能把生成 temperature 当校准 temperature。[Guo 等 ICML 2017](https://proceedings.mlr.press/v70/guo17a.html) 是方法依据，不保证在本模型有效。报告 NLL、Brier、ECE、宏平均 F1、混淆矩阵和拒判覆盖率；同时对语音缺失/视频缺失/正文含情绪词/朗读强烈情绪材料做分层检查。初期全部标记 `uncalibrated`，不得发自动动作；仅在本域误判风险满足预先约定上限后，才启用会话提示。不能预先拍定“confidence > 0.8 即可靠”。

固定 demo 只支持经过预检的视频音轨；纯文本或无声视频返回 `unsupported_modality`，后续若另实现模态子集，则必须单独校准。可在本人明确说“累了/想休息”时按自评提供休息，这是用户事件规则，不应包装为 Emotion-LLaMA 推断。

建议采样窗口约 5–10 秒、限于本人主动启用的答题片段，是待验证的工程初值，不是作者验证参数。初次离线研究对同一片段比较首帧、显式文字加入前后、音频静音或视觉遮蔽；如果结果主要由题目中“悲伤/愤怒”等词驱动，则不能使用该预测辅助策略。正式保留率评价一律在后续回忆发生前固定预测，杜绝使用答案反馈、正确答案、未来结果造成泄漏。

## 8. 与 FSRS 的明确接口决定

**阶段 A（当前默认）：** FSRS 按人工真实回忆评分更新。记录反应时间、是否使用提示、本人选择休息/继续等会话事件。情绪模型缺席也产生完整复习记录，SM-2/Leitner 同样可用。

**阶段 B（可选科学实验）：** 原 FSRS 安排不变，异步产生 EmotionObservation；只作离线增量预测比较，记录 missingness。必要时本人读取观察而手动调整会话节奏；观测不会创建第二条复习记录。

**阶段 C（评估通过后）：** 只允许提示休息、改变下一题呈现或同一“已到期”集合内的顺序。本人确认才执行休息；不能因外观判断跳过到期题或改动未来 due。若要把情绪加入记忆模型，另设有版本的研究预测器，例如在 FSRS 预测对数几率上拟合增量残差，并接受与纯 FSRS 的独立时间留出比较；这不是目前确定启用的生产规则。

最低成功指标：本域观测有效率与校准、实际峰值内存和时延可接受，缺失/错误时回到基线，延迟回忆或单位学习时长的收益在独立比较中成立。仅“模型能输出文字”不算融合有效。

## 9. 原论文指标如何正确引用

| 结果 | 实际设置与边界 |
|---|---|
| MER2023-SEMI F1 0.9036 | A/V/T，全模型经数据集任务训练；不是本人中文复习、也不是首帧 demo 性能 |
| MER2023-SEMI F1 0.8905 | A/V 条件；证明论文比较过模态配置，不证明运行时任意缺失模态鲁棒 |
| EMER 7.83 / 6.25 | 0–10 的 clue/label overlap，ChatGPT 评价解释文本；不是 78.3%/62.5% 回忆正确率 |
| DFEW UAR 45.59 / WAR 59.37 | 论文零样本视频情绪识别设置；并非 90% 通用情绪准确率 |
| MER-NOISE 0.8530 冠军 | SZTU-CMU 后续 Conv-Attention 系统及伪标注流程，不应当作单独部署 v1 checkpoint 的固定性能 |

指标来源：[v1 论文实验](https://arxiv.org/html/2406.11161v2#S4)，[官方 README 表格](https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/README.md)，[MER2024 作者论文](https://arxiv.org/abs/2408.10500)。没有发现以 FSRS+Emotion-LLaMA 对个人复习的延迟回忆进行验证的官方证据；所有学习效益目前是研究假设。

## 10. D 盘约束与后续实验边界

本次证据与研究文本均保存于本研究目录，未下载任何大权重。以后若启动作者程序，必须在启动前将 TEMP、TMP、TMPDIR、HF_HOME、HUGGINGFACE_HUB_CACHE、TRANSFORMERS_CACHE、TORCH_HOME、XDG_CACHE_HOME、GRADIO_TEMP_DIR、pip/conda 缓存及工作目录都指向 D 盘，并检查 MoviePy/ffmpeg 的临时路径。`conversation.py` 使用 `TemporaryDirectory`，默认 Windows TEMP 通常在系统盘，不能原样启动后假定所有中间物都在 D 盘。个人视频优先只在内存处理；本人主动保留的实验片段按明确周期删除，文档/摘要日志不包含原始录像。

本次没有执行模型推理，没有验证 CPU、GPU、量化或端到端延迟。下一步如确实准备启用模型，只进行最小离线复现实验：固定单个 checkpoint、固定 pipeline、固定一小组自愿片段，报告质量/资源/缺失失败并决定是否继续。用户当前只要求研究，因此不实施上述步骤。
