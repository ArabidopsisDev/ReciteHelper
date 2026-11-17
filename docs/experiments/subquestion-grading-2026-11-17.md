# 分问独立判分与状态块验证

日期：2026-11-17。该记录检查功能行为，不评价长期学习效果或一般判分准确率。

多问现在分别提交本问范围、参考要点、课程资料与本问答案，最多同时请求两问。空白问直接记零，不向模型借用其他问的内容。输出重新组合到原答案位置，原题总分按既定占比合计。

## 必要回归

完整测试集 143 项通过。新增检查覆盖 45% 与 70% 的边界、FL 优先级、独立请求与并发上限、占比和小问顺序、原文批注重组、空白问及 FL 持久化。

原生 WPF 检查包括四种状态块、悬停时的 Tooltip 说明、小宽度换行、答题页和考试回顾的参考答案上方位置，以及切题后隐藏旧结果。

## 一次真实模型样例

使用 DeepSeek Chat 与提供的课程参考要点，批改数据结构的四个小问；总耗时约 4.63 秒。

| 小问 | 输入情况 | 原始覆盖率 | 实际状态 |
| --- | --- | --- | --- |
| 栈 | 后进先出，入栈和出栈都在栈顶 | 100% | AC |
| 队列 | 写出先进先出和队尾入队，遗漏队头出队 | 67% | PT |
| 数组 | 回答“不会” | 0% | WA |
| 单链表 | 错认为连续存储并可通过下标直接访问 | 0% | FL |

FL 说明指出单链表与数组的关键存储和访问概念被混淆。该问未计为掌握。原始结果见 [JSON](subquestion-grading-2026-11-17.json)，界面见 [状态块截图](../design/screenshots/subquestion-results-live.png)与[悬停说明](../design/screenshots/subquestion-tooltip-live.png)。这是一个四问样例，不能据此推出普遍识别准确率。

复现实验：

```powershell
$env:TEMP = 'D:\Temp\ReciteHelperTask'
$env:TMP = $env:TEMP
dotnet run --project experiments/cfsrs/CfsrsExperiment.csproj --no-restore -- --live --subquestions
```

该命令通过环境变量读取 DeepSeek 密钥；原始模型响应仅保存在 D 盘临时目录。
