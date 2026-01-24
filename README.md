# ReciteHelper

ReciteHelper 是一个基于 AI 的开源桌面记忆辅助软件，能自动解析课程内容、聚合知识点，帮助用户进行高效学习和复习，适合学生、考研党、自学者等使用。 

ReciteHelper 承诺永久保持开源、免费的性质。如果您感觉该项目对您有帮助，点个 star 就是对开发者最大的支持。

由于开发者并非科班专业，因此本软件中代码中存在调试遗留、迷惑命名、随性拼写、参数锁死、变量客串、魔法数字、拼音乱入、僵尸代码、脆弱读写、环境依赖、人机缝合、注释欺诈、分大于总、线程互锁等等问题，导致运行相当不稳定。欢迎有能力的各位朋友们优化代码。

本项目使用 C# (.NET 10 / WPF) 开发。

---

## 功能简介

- **PDF 自动知识点聚类**：支持导入 PDF 文本资料，自动拆分为章节/知识点。
- **章节与知识点浏览**：可按章节、知识点进行有序学习，可标记掌握情况。
- **题目练习与智能判分**：支持填空题自动判分，允许答案模糊匹配。
- **模拟考试**：随机抽题，计时考试，自动统计成绩，错题可回顾。
- **学习数据导出**：支持学习进度、答题记录导出为 JSON 文件。
- **游戏生成**：支持将题库转换为视觉小说，在游戏中学习吧!
- **后续预告**：将支持 OCR 图片识别、更多题型、遗忘曲线智能复习等。

---

## 如何运行

1. 环境依赖：需安装 .NET 10 及以上运行环境。
2. 下载项目源码或者直接下载发行版。
3. 启动主程序，按界面提示导入 PDF 学习资料，创建项目后即可体验全部功能。

具体使用方法，请参考**用户手册**。

---

## 如何贡献

非常欢迎社区贡献者参与！您的贡献将被永久记录在项目 README 文件的特别感谢一栏中。只需要：

1. Fork 本仓库，创建新分支进行开发；
2. 遵循现有代码风格，建议任何改动都配上适当的注释说明；
3. 代码提交前请确保能正常运行并通过基本测试；
4. 提 PR 前，请尽量关联 issue 或附带改动说明及截图（如有 UI 变更）；
5. 对于文档、翻译、测试用例也同样欢迎补充！

开发/讨论可以通过 [Issues](https://github.com/ArabidopsisDev/ReciteHelper/issues) 区或邮箱反馈。

---

## 许可证

本项目采用 **GNU AGPL v3.0** 或更高版本许可证分发。  
任何发布的衍生作品或者基于本项目进行的二次开发、SaaS 部署，均须共享完整源代码，并附带此协议原文说明。  
协议详细内容请参阅 [LICENSE](LICENSE) 文件或访问 [GNU 官网](https://www.gnu.org/licenses/agpl-3.0.html)。

---

## 用户手册

|语言|地址|
|:--:|:--:|
|中文（简体）|[中文用户手册](Docs/manual-cn.md)|
|English|[English Manual](Docs/manual-en.md)|

---

## 联系与反馈

- GitHub： [https://github.com/ArabidopsisDev/ReciteHelper](https://github.com/ArabidopsisDev/ReciteHelper)
- 邮箱：arab@methodbox.top
- QQ讨论群：1053379975

贡献前，请阅读[行为准则](CODE_OF_CONDUCT.md)。

欢迎提出建议、Bug 反馈或功能需求，PR 与 Issue 都会及时处理！

---

## 星标历史

[![Star History Chart](https://api.star-history.com/svg?repos=ArabidopsisDev/ReciteHelper&type=date&legend=top-left)](https://www.star-history.com/)

---

## 特别鸣谢

衷心感谢以下成员为项目做出的卓越贡献，他们的付出是项目成功的关键：

<div align="center">

| 头像 | 学校或单位 | 昵称 | 贡献内容 |
|:------:|:----------:|:-----:|------------|
| <img src="docs\Resources\Thanks\01.jpg" width="60" height="60" style="border-radius:50%;border:2px solid #4fc3f7"> | 南昌航空航天大学 |  **Sati** | 帮助测试并解决了多PDF的处理问题 |
| <img src="docs\Resources\Thanks\02.jpg" width="60" height="60" style="border-radius:50%;border:2px solid #ffb74d"> | 海南大学 | **Mrwhite3142** | 帮助测试发现了文件无法正常处理的问题（暂未能复现） |
| <img src="docs\Resources\Thanks\03.png" width="60" height="60" style="border-radius:50%;border:2px solid #ffb74d"> | / | **oife** | 帮助测试并解决了无法正常加载项目和测试代码未删除的问题 |


</div>

<br>

> “众人拾柴火焰高”——感谢每一位贡献者的热情参与和无私奉献，你们的每一行代码、每一次测试、每一份文档都让这个项目更加完善。期待未来继续携手同行，创造更多精彩！

<br>
<div align="center">
<p style="color:#666;font-size:0.9em">
感谢阅读 · 持续更新中 · 更多贡献者欢迎加入！
</p>
</div>