# cFSRS 论文

论文：**cFSRS：面向多问简答题的内容组件化间隔复习**。作者：**Arabidopsis**。

`cFSRS_Arabidopsis.pdf` 为 8 页中文学术预印本。使用 A4 单栏、宋体风格正文、衬线数学字体、编号公式、三线表、向量图和 IEEE 数字引用。论文没有虚构学校、期刊、被试或长期保持率实验。

## 文件

- `main.typ`、`references.bib`：论文排版源与参考文献。
- `figures/`：矢量框架图及解析曲线，均为程序绘制。
- `analysis.json`：解析示例的初值、FSRS-6 参数及数值结果。
- `snapshot-manifest.json`：论文依据的工作区关键文件与实验记录的 SHA-256 摘要。基础 Git 提交不含全部后续改动，因此记录同时标明存在未提交工作。
- `build_analysis.py`：在本仓库中重新生成解析曲线及来源摘要；需要 NumPy、Matplotlib 和 Noto Sans CJK SC 字体。
- `data/`：撰写论文时使用的五份实验记录副本。

## 编译

本次使用 D 盘的 Typst 0.15.1，外部字体为 Noto Serif CJK SC、Noto Sans CJK SC 及其粗体。数学字体 New Computer Modern Math 和西文 Libertinus Serif 由 Typst 提供。编译命令如下：

```powershell
powershell -NoProfile -File .\build.ps1
```

可通过 `-TypstExecutable` 与 `-FontDirectory` 指定本机路径。字体没有打包进源文件；可从 [Noto CJK 官方仓库](https://github.com/notofonts/noto-cjk)取得。无需网络字体、Typst 第三方包或付费插件。

实验程序位于仓库的 `experiments/cfsrs`，详细实验说明见 `docs/experiments/cfsrs-2026-11-16.md`。7 个评分示例和两组出题记录属于探索性原型验证；确定性曲线不是学习者轨迹，131 项软件测试也不是教育实验的样本量。

## 数学与版面检查

核对了课程得分映射、局部影响界、权重扰动界、组合间隔唯一性及 FSRS-6 公式附录。逐页渲染并检查了公式下标、分段函数间距、图中连线、表格、引用与页码；PDF 文字可检索，作者元数据为 Arabidopsis。
