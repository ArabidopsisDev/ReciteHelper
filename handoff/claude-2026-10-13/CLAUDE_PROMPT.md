# 可直接发送给 Claude 的提示词

请接手我的鲁信杯项目计划书工作。**我的产品项目是 ReciteHelper，不是 OpenRecite；OpenRecite 只是此前拆出的底层复习 SDK。**

先完整阅读：
`D:\Projects\ReciteHelper\handoff\claude-2026-10-13\HANDOFF.md`

全部材料已放在同一目录下 `materials` 中。若我上传的是 ZIP，请先解压到 D 盘，并以解压后的 HANDOFF.md 和 FILES.sha256.json 定位材料。部分原始脚本写死旧路径，运行前先调整，避免覆盖旧稿。

## 任务与署名

编写并打磨一份正式、完整、可编辑的 DOCX 项目计划书，面向“鲁信杯”第一届山东省“人工智能＋产教应用”大赛，拟申报教育创新赛道学校组。

参赛名称：**汇智云学——基于FSRS、Emotion-LLaMA和生成式AI多模态融合的智能复习系统**。

- 学校：山东农业大学。
- 学院：农学院。
- 负责人：刘宗睿。
- 学号：2024210172。
- 指导教师：张金诺，副教授。
- 团队名此前授权自拟，当前拟用“穗知创新团队”。

## 最重要的修订要求

1. 不要再写成 OpenRecite 项目计划书。围绕 **ReciteHelper 产品**叙述；提到 OpenRecite 时，准确说明它是技术组件。
2. 我不接受此前的“AI排版样式”：不要蓝色表头、圆角卡片、每页重复大标题、短页拼接或大量装饰。参考大学生创新创业大赛常见的正式 Word 计划书，采用宋体正文、多级编号、连续流排、规范图表题注、自动目录、页码与附录。
3. 21页太少，要有充分实质内容。现有第二版已经由Word导出为54页，但尚未完成视觉验收；不要机械追求54页，也不要为了减少工作退回21页。合并重复、补足论证，合理页数由内容决定。
4. 优先打开最新第二版及其PDF，再结合研究和第一版素材继续编辑。不要无意义地从头搜索、重复问已确认信息。
5. 写清项目目标、用户问题、具体学习场景、技术原理、知识图谱、FSRS与Emotion-LLaMA融合方式、验证方案、市场、商业模式、团队、实施计划、财务、风险及成果转化。
6. 不虚构调研、真人实验、用户量、收入、获奖、专利、合作协议或学校采购。文档中的价格、预算和三年测算是初步假设，可以调整，但必须说明依据和口径。
7. 现有ReciteHelper原型与OpenRecite核心属于已有开发基础；知识图谱和多模态融合仍属于拟研发。不能把Emotion-LLaMA论文成绩当作项目实测。模型、底模与数据许可分别处理。
8. 先保留FSRS真实评分和记忆更新，以情境建议、影子预测、独立验证后的记忆扩展为研发顺序。不要把表情直接映射为遗忘或分数。
9. 所有输出、下载、脚本、模型缓存、临时文件和渲染图片必须在D盘。不要修改或清理ReciteHelper工作区的其他代码变更。
10. 最终逐页渲染检查DOCX，核对目录、编号、跨页表格、图文、公式与财务勾稽；交付成稿DOCX，说明还有哪些真实材料需我补充。不要把未检查稿宣称为最终验收。

## 关键文件

- 最新文档：`materials\competition\revision2\output\汇智云学_鲁信杯项目计划书_规范扩展版.docx`
- 最新校验PDF：`materials\competition\revision2\qa\word-render.pdf`
- 第二版扩写正文：`materials\competition\revision2\work\expansion.md`
- 第二版生成器：`materials\competition\revision2\work\build_expanded.py`
- 第一版素材：`materials\competition\output`、`materials\competition\work`
- 前期技术研究：`materials\research\方案.md`及三个专项研究文件。
- 格式参考：`materials\competition\revision2\sources\format-decision.txt`与该目录网页快照。

请先简要说明读到的当前状态和准备优先修正的内容，然后直接执行。没有确实阻断工作的信息缺口时，不要停在方案或反复等待确认。
