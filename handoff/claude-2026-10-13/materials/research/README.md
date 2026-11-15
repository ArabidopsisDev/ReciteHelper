# OpenRecite：知识图谱与 Emotion-LLaMA / FSRS 融合研究

日期：2026-10-13。目标：**个人电脑、本地中文复习、可选摄像头/麦克风**。本轮不编写或修改生产代码，不下载模型权重，不发布包。

## 从这里开始

1. **[确定方案与实施决策](方案.md)**：选型、架构、数据流、融合公式、部署边界和执行顺序。
2. [现有代码事实核查](local-code-audit.md)：接入点和必须处理的事件/持久化问题。
3. [知识图谱专项研究](knowledge-graph-research.md)。
4. [Emotion-LLaMA 专项研究](emotion-llama-research.md)。
5. [融合与实验专项研究](fusion-research.md)。
6. [数据契约示例](contracts.example.json)：纯研究示例，不是已实现 API 或真实用户数据。
7. [公式敏感性实验脚本](experiments/sensitivity.py)、[结果](experiments/sensitivity-summary.json)、[明细](experiments/sensitivity.csv)。

## 结论摘要

- 知识图谱采用**本地 SQLite + 显式概念/关系/证据/题目映射**，首先支持知识关联、错题溯源和先修补救。保持 FSRS 的题目级记忆状态。
- Emotion-LLaMA 采用**独立、可禁用的研究推理服务**；首期融合在会话策略层，只有通过域内校准和实验的信号才用于辅助建议。多模态改写记忆曲线留在影子实验中。
- 当前机器未发现 CUDA 设备环境；官方 v1 demo 也不是论文完整特征路径。因此不能承诺直接本地实时部署或复现论文成绩。
- 所有主动保存的文档、证据快照、实验脚本和结果位于本目录（D 盘）。未运行涉及参与者、摄像头、麦克风或模型推理的实验。

## 证据保存

- [本地源码哈希与提交](evidence/local-baseline.json)。
- [通用一手参考资料快照清单](evidence/references/manifest.json)。
- [Emotion-LLaMA 固定提交资料快照清单](evidence/emotion-llama/manifest.json)。

文中区分三类陈述：**源码/文献事实**、**本项目的设计决定**、**尚需实验验证的假设**。候选窗口、阈值、样本量和性能预算不表示已经完成验证。
