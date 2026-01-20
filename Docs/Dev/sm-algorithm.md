# 关于对复习算法的思考

本文档用于记录对于复习功能的算法设计思考，最终成品采用的算法可能与之不同。

## 算法选型

一个经典的记忆模型就是对数 Ebbinghaus 遗忘曲线。但是该曲线在实际应用场景中严重受阻，例如在 20 分钟后，记忆量就会下降到 58.2%，我们不可能要求用户每隔几十分钟就复习一次之前的题目，这样会极大的降低用户的学习量。

我们需要一个可应用的记忆算法模型。因此，采用设计简单、开源的算法 Supermemo-2 并进行优化。设 $EF$ 表示条目的容易程度，$I$ 表示复习的间隔（单位：天），$q$ 表示用户对本次复习质量的评分。根据用户本次的复习质量更新下一次复习的时间间隔：

$$
I_{\text{new}}(I, \text{EF}, q) =
\begin{cases}
1 &  q < 3  \\
\max(1, \lfloor I \times \text{EF} \rfloor) &  q \ge 3 
\end{cases}
$$

其中 $q$ 的评分标准为：

|评分|用户表现|
|:--:|:--:|
| 5 | 完美回忆 |
| 4 | 较难但正确回忆 |
| 3 |困难但最终正确回忆 |
| 2 |错误回忆（但看到答案后能理解）|
| 1 | 完全遗忘 |
| 0 |完全不会 |

最后更新易度因子 $EF$：

$$EF' = EF + (0.1 - (5 - q) × (0.08 + (5 - q) × 0.02))$$

## 评分标准的测定

传统使用 SM-2 及类似衍生算法的软件常常在复习完成一道题后要求用户给出 $q$ 分数。虽然可行，但是在实际测试中，这通常会明显的降低用户体验。

因此，我们使用答题时间 $t$、最近三次的质量评分平均数 $\bar {q}$、以及用户答案与标准答案的 Levenshtein 距离 $L$ 定量的衡量用户本次答题的 $q$ 值。

形式化地说，给定特征向量 $\mathbf{x} = (t, \bar{q}, L)$，预测质量评分 $q \in \{0,1,2,3,4,5\}$。

假设上述特征互相独立，选用朴素贝叶斯模型：

$$
P(q = k \mid t, \bar{q}, L) = \frac{P(q = k) \cdot P(t \mid q = k) \cdot P(\bar{q} \mid q = k) \cdot P(L \mid q = k)}{\sum_{j=0}^{5} P(q = j) \cdot P(t \mid q = j) \cdot P(\bar{q} \mid q = j) \cdot P(L \mid q = j)}
$$

### 交互特征处理

对于答题时间而言，考虑到答案长短不均一，我们使用输入速率 $r$ 来评价答题时间指标。该指标可以广泛的反应用户输入速度快慢，删改文字等情况。该指标与 Levenshtein 距离存在强交互作用，一个典型的示例如下：


| 输入速率 $r$ | 相似度 $S$ |	可能情况 |
|:--:|:--:|:--:|
| 高 ($>3$) | 高 ($>0.9$) |熟练回忆（$q \geq 4$）|
| 高 ($>3$) | 低 ($<0.5$) |	瞎jb猜 ($q \leq 1$)|
| 低 ($<1$)	| 高 ($>0.9$) |	努力回忆但最终正确（$q \geq 3$） |
| 低 ($<1$) | 低 ($<0.5$) |	努力回忆但失败 ($q \leq 2$)|

其中相似度 $S$ 是 Levenshtein 距离 $L$ 的归一化相似度。因此我们构建组合特征 $F = f(r, L)$ ，显然此时 $F$ 与 $\bar {q}$ 相互独立，朴素贝叶斯输入为 $[F, \bar {q}]$，表达式为：

$$
P(q = k \mid F = f, H = h) = \frac{P(q = k) \cdot P(F = f \mid q = k) \cdot P(H = h \mid q = k)}{\sum_{j=0}^{5} P(q = j) \cdot P(F = f \mid q = j) \cdot P(H = h \mid q = j)}
$$

$$
H = \text{discretize}(\bar{q}) \in \{\text{poor}, \text{medium}, \text{good}\}
$$

### 数据离散化

朴素贝叶斯是概率模型，适用于处理离散化特征，因此我们构造离散化函数，将以上指标离散化。

对于 组合特征 $F$ 而言，我们首先离散化组成 $F$ 的特征 $r$ 和 $L$ 。用于用户的打字速度可能存在差异，我们提前测量用户的目视打字速度 $\text{A}$，计算用户的相对答题速度：

$$
r_{rel} = \frac{r}{\text{A}}
$$

定义离散化函数 $\text{speed\_level}$ 为：

$$
\text{speed\_level}(r_{\text{rel}}) =
\begin{cases}
\text{very\_fast} &  r_{\text{rel}} > 0.8 \\
\text{fast} &  0.6 < r_{\text{rel}} \leq 0.8 \\
\text{normal} &  0.4 < r_{\text{rel}} \leq 0.6 \\
\text{slow} &  0.2 < r_{\text{rel}} \leq 0.4 \\
\text{very\_slow} &  r_{\text{rel}} \leq 0.2
\end{cases}
$$

对于历史 $q$ 值情况而言，首先求得历史三次的平均 $\bar{q}$ 值：

$$
\bar {q} = \frac{1}{3} \sum^3_{i=1} q_{t-i}
$$

之后采用等距分箱方案对 $\bar{q}$ 离散化：

$$
H(\bar{q}) =
\begin{cases}
\text{poor} &  \bar{q} < 2.0 \\
\text{medium} &  2.0 \leq \bar{q} < 3.5 \\
\text{good} &  \bar{q} \geq 3.5
\end{cases}
$$