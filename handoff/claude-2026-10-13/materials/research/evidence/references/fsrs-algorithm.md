The FSRS (Free Spaced Repetition Scheduler) algorithm originates in the [DHP model from MaiMemo](https://www.maimemo.com/paper/), which is a variant of the DSR (Difficulty, Stability, Retrievability) model used to predict memory states.

Here is a visualizer to preview the interval with specific parameters and review history: [FSRS Visualizer (open-spaced-repetition.github.io)](https://open-spaced-repetition.github.io/anki_fsrs_visualizer/).

In case you find this article difficult to understand, perhaps you will like this more: [https://expertium.github.io/Algorithm.html.](https://expertium.github.io/Algorithm.html)

# Symbol

- $R$: Retrievability (probability of recall)
- $S$: Stability (interval when $R=90%$)
  - $S^\prime_r$: new stability after recall
  - $S^\prime_f$: new stability after forgetting
- $D$: Difficulty ( $D \in [1, 10]$ )
- $G$: Grade/Rating:
  - $1$: `again`
  - $2$: `hard`
  - $3$: `good`
  - $4$: `easy`

# FSRS-6

## Default parameters

```python
[0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722, 0.1666, 0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425, 0.0912, 0.0658, 0.1542]
```

## Formula

> The $w_i$ denotes w[i]. This version uses 21 parameters. The memory state is represented by Stability (S) and Difficulty (D).

***

The formula of stability after a same-day review is changed in this update:

$$S^\prime(S,G) = S \cdot e^{w_{17} \cdot (G - 3 + w_{18})} \cdot S^{-w_{19}}.$$

So the S increases faster when it's small and slower when it's large. S will converge when $$SInc = e^{w_{17} \cdot (G - 3 + w_{18})} \cdot S^{-w_{19}} = 1$$.

In practice, we should ensure that $$SInc \geq 1$$ when $$G \geq 2$$.

***

The forgetting curve's decay is trainable:

$$R(t,S) = \left(1 + factor \cdot \cfrac{t}{S}\right)^{-w_{20}},$$

where $$factor = 0.9 ^ {-\cfrac{1}{w_{20}}} - 1$$ to ensure $$R(S,S) = 90\\%.$$

# FSRS-5

## Default parameters

```python
[0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192, 1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621]
```

## Formula

> The $w_i$ denotes w[i]. This version uses 19 parameters. The memory state is represented by Stability (S) and Difficulty (D).

***

The stability after a same-day review:

$$S^\prime(S,G) = S \cdot e^{w_{17} \cdot (G - 3 + w_{18})}$$

***

The initial difficulty after the first rating: 

$$D_0(G) = w_4 - e^{w_5 \cdot (G - 1)} + 1,$$

where $w_4 = D_0(1)$, i.e., the initial difficulty when the first rating is `Again`.

***

Linear Damping for the new difficulty after review:

$$\Delta D(G) = - w_6 \cdot (G - 3)$$

$$D^\prime = D + \Delta D \cdot \cfrac{10 - D}{9}$$

***

In FSRS 5, $D_0(4)$ is the target of mean reversion. Earlier versions had $D_0(3)$ as the target.

$$D^{\prime\prime} = w_7 \cdot D_0(4) + (1 - w_7) \cdot D^\prime.$$

The other formulas are the same as FSRS-4.5.

# FSRS-4.5

## Default parameters

```python
[0.4872, 1.4003, 3.7145, 13.8206, 5.1618, 1.2298, 0.8975, 0.031, 1.6474, 0.1367, 1.0461, 2.1072, 0.0793, 0.3246, 1.587, 0.2272, 2.8755]
```

## Formula

> The $w_i$ denotes w[i]. This version uses 17 parameters. The memory state is represented by Stability (S) and Difficulty (D).

***

The formula of forgetting curve is changed in this update.

The retrievability after $t$ days since the last review:

$$R(t,S) = \left(1 + FACTOR \cdot \cfrac{t}{S}\right)^{DECAY},$$

where $R(t,S)=0.9$ when $t=S$.

The next interval can be calculated by solving for $t$ in the above equation after putting the request retention in place of $R$:

$$I(r,S) = \cfrac{S}{FACTOR} \cdot \left(r^\cfrac{1}{DECAY} - 1\right),$$

where $I(r,S)=S$ when $r=0.9$.

In FSRS v4, $DECAY=-1$ and $FACTOR=\cfrac{1}{9}$

In FSRS-4.5, $DECAY=-0.5$ and $FACTOR=\cfrac{19}{81}$

The new forgetting curve drops sharply before $S$ and flatly after $S$.

# FSRS v4

## Default parameters

```python
[0.4, 0.6, 2.4, 5.8, 4.93, 0.94, 0.86, 0.01, 1.49, 0.14, 0.94, 2.18, 0.05, 0.34, 1.26, 0.29, 2.61]
```

## Formula

> The $w_i$ denotes w[i]. This version uses 17 parameters. The memory state is represented by Stability (S) and Difficulty (D).

***

The initial stability after the first rating: 

$$S_0(G) = w_{G-1}.$$

For example, $S_0(1)=w_0$ is the initial stability when the first rating is `again`. When the first rating is `easy`, the initial stability is $S_0(4)=w_3$.

***

The initial difficulty after the first rating: 

$$D_0(G) = w_4 - (G-3) \cdot w_5,$$

where the $D_0(3)=w_4$ when the first rating is `good`.

***

The new difficulty after review:

$$D^\prime(D,G) = w_7 \cdot D_0(3) +(1 - w_7) \cdot (D - w_6 \cdot (G - 3)).$$

It will calculate the new difficulty with $D^\prime = D - w_6 \cdot (G - 3)$, and then apply the mean reversion $w_7 \cdot D_0(3) + (1 - w_7) \cdot D^\prime$ to avoid "ease hell".

***

The retrievability after $t$ days since the last review:

$$R(t,S) = \left(1 + \cfrac{t}{9 \cdot S}\right)^{-1},$$

where $R(t,S)=0.9$ when $t=S$.

The next interval can be calculated by solving for $t$ in the above equation after putting the request retention in place of $R$:

$$I(r,S) = 9 \cdot S \cdot \left(\cfrac{1}{r} - 1\right),$$

where $I(r,S)=S$ when $r=0.9$.

***

The new stability after a successful review (the user pressed "Hard", "Good" or "Easy"):

$$
\begin{align*}
S^\prime_r(D,S,R,G) = & S \\
& \cdot (e^{w_8} \\
& \cdot (11-D) \\
& \cdot S^{-w_9} \\
& \cdot (e^{w_{10}\cdot(1-R)}-1) \\
& \cdot w_{15}(\textrm{if G = 2}) \\
& \cdot w_{16}(\textrm{if G = 4}) \\
& + 1).
\end{align*}
$$

Let $SInc$ (the increase in stability) denotes $\cfrac{S^\prime_r(D,S,R,G)}{S}$, which is equivalent to Anki's factor.

1. The larger the value of $D$, the smaller the $SInc$ value. This means that the increase in memory stability for difficult material is smaller than for easy material.
2. The larger the value of $S$, the smaller the $SInc$ value. This means that the higher the stability of the memory, the harder it becomes to make the memory even more stable.
3. The smaller the value of $R$, the larger the $SInc$ value. This means that the [spacing effect](https://en.wikipedia.org/wiki/Spacing_effect) accumulates over time.
4. The value of $SInc$ is always greater than or equal to 1 if the review was successful.

In FSRS, a delay in reviewing (i.e., overdue reviews) affects the next interval as follows:

As the delay increases, retrievability (R) decreases. If the review was successful, the subsequent stability (S) would be higher, according to point 3 above. However, instead of increasing linearly with the delay like the SM-2/Anki algorithm, the subsequent stability converges to an upper limit, which depends on your FSRS parameters

![image](https://user-images.githubusercontent.com/32575846/235642239-25de48b8-5dc6-4450-94e5-0bbf8165a559.png)

You can modify them in this playground: https://www.geogebra.org/calculator/ahqmqjvx.

***

The stability after forgetting (i.e., post-lapse stability):

$$
\begin{align*}
S^\prime_f(D,S,R) =  & w_{11} \\
& \cdot D^{-w_{12}} \\
& \cdot ((S+1)^{w_{13}} - 1) \\
& \cdot e^{w_{14} \cdot (1-R)}.
\end{align*}
$$

For example, if $D=2$ and $R=0.9$, with default parameters, $S^\prime_f(S=100) = 2\cdot 2^{-0.2} \cdot ((100+1)^{0.2}-1) \cdot e^{1(1-0.9)} \approx 3$ and $S^\prime_f(S=1) \approx 0.3$.

# FSRS v3

## Default parameters

```python
[0.9605, 1.7234, 4.8527, -1.1917, -1.2956, 0.0573, 1.7352, -0.1673, 1.065, 1.8907, -0.3832, 0.5867, 1.0721]
```

## Formula

> The $w_i$ denotes w[i]. This version uses 13 parameters. The memory state is represented by Stability (S) and Difficulty (D).

***

The initial stability after the first rating: 

$$S_0(G) = w_0 + (G-1) \cdot w_1,$$

where the $S_0(1)=w_0$ is the initial stability when the first rating is `again`. When the first rating is `easy`, the initial stability is $S_0(4)=w_0 + 3 \cdot w_1$.

***

The initial difficulty after the first rating: 

$$D_0(G) = w_2 + (G-3) \cdot w_3,$$

where the $D_0(3)=w_2$ when the first rating is `good`.

***

The new difficulty after review:

$$D^\prime(D,G) = w_5 \cdot D_0(3) +(1 - w_5) \cdot (D + w_4 \cdot (G - 3)).$$

It will calculate the new difficulty with $D^\prime = D + w_4 \cdot (G - 3)$, and then apply the mean reversion $w_5 \cdot D_0(3) + (1 - w_5) \cdot D^\prime$ to avoid "ease hell".

***

The retrievability of $t$ days since the last review:

$$R(t,S) = 0.9^{\frac{t}{S}},$$

where $R(t,S)=0.9$ when $t=S$.

The next interval can be calculated by solving for $t$ in the above equation after putting the request retention in place of $R$.

$$I(r,S) = S \cdot \cfrac{\ln(r)}{\ln(0.9)},$$

where $I(r,S)=S$ when $r=0.9$.

Note: the intervals after Hard and Easy ratings are calculated differently. The interval after Easy rating will multiply `easyBonus`. The interval after Hard rating is `lastInterval` multiplied `hardInterval`.

***

The new stability after recall:

$$S^\prime_r(D,S,R) = S\cdot(e^{w_6}\cdot (11-D)\cdot S^{w_7}\cdot(e^{w_8\cdot(1-R)}-1)+1).$$

Let $SInc$ (the increase in stability) denotes $\cfrac{S^\prime_r(D,S,R)}{S}$, which is equivalent to Anki's factor.

1. The larger the value of $D$, the smaller the $SInc$ value. This means that the increase in memory stability for difficult material is smaller than for easy material.
2. The larger the value of $S$, the smaller the $SInc$ value. This means that the higher the stability of the memory, the harder it becomes to make the memory even more stable.
3. The smaller the value of $R$, the larger the $SInc$ value. This means that the [spacing effect](https://en.wikipedia.org/wiki/Spacing_effect) accumulates over time.
4. The value of $SInc$ is always greater than or equal to 1 if the review was successful.

The following 3D visualization could help understand.

![image](https://user-images.githubusercontent.com/32575846/192936891-1a3c4fd8-974b-4b87-ad76-8ba774500361.png)

In FSRS, a delay in reviewing (i.e., overdue reviews) affects the next interval as follows:

As the delay increases, retrievability (R) decreases. If the review was successful, the subsequent stability (S) would be higher, according to point 3 above. However, instead of increasing linearly with the delay like the SM-2 algorithm, the subsequent stability converges to an upper limit, which depends on your FSRS parameters.

***

The stability after forgetting (i.e., post-lapse stability):

$$S^\prime_f(D,S,R) = w_9\cdot D^{w_{10}}\cdot S^{w_{11}}\cdot e^{w_{12}\cdot(1-R)}.$$

For example, if $D=2$ and $R=0.9$, with default parameters, $S^\prime_f(S=100) = 2\cdot 2^{-0.2} \cdot 100^{0.2} \cdot e^{1(1-0.9)} \approx 5$ and $S^\prime_f(S=1) \approx 2$.

You can play the function in [post-lapse stability - GeoGebra](https://www.geogebra.org/calculator/dzwn2zwh).

![image](https://user-images.githubusercontent.com/32575846/192935271-3b868490-b893-41bd-873c-2a0cf9d96bb1.png)

***

# FSRS v2

## Default parameters

```python
[1, 1, 1, -1, -1, 0.2, 3, -0.8, -0.2, 1.3, 2.6, -0.2, 0.6, 1.5]
```

## Formula

> The $w_i$ denotes w[i]. This version uses 14 parameters. The memory state is represented by Stability (S) and Difficulty (D).

***

The initial stability after the first rating:

$$S_0(G) = w_0 \cdot (w_1 \cdot (G - 1) + 1)$$

***

The initial difficulty after the first rating:

$$D_0(G) = w_2 \cdot (w_3 \cdot (G - 4) + 1)$$

The difficulty is clamped within the range $[1, 10]$.

***

The new difficulty after review:

$$D^\prime(D,G) = w_5 \cdot D_0(3) + (1 - w_5) \cdot (D + w_4 \cdot (G - 3))$$

It first computes a temporary new difficulty, then applies mean reversion towards $D_0(3) = w_2 \cdot (-w_3 + 1)$ to prevent "ease hell". The new difficulty is also clamped within $[1, 10]$.

***

The new stability after a successful review (the user pressed "Hard", "Good" or "Easy"):

$$S^\prime_r(S, D', R) = S \cdot \left(1 + e^{w_6} \cdot (D')^{w_7} \cdot S^{w_8} \cdot (e^{(1-R) \cdot w_9}-1)\right)$$

***

The new stability after forgetting (the user pressed "Again"):

$$S^\prime_f(S, D', R) = w_{10} \cdot (D')^{w_{11}} \cdot S^{w_{12}} \cdot (e^{(1-R) \cdot w_{13}}-1)$$

# FSRS v1

## Default parameters

```python
[2, 5, 3, -0.7, -0.2, 1, -0.3]
```

## Formula

> The $w_i$ denotes w[i]. This version uses 7 parameters. The memory state is represented by Stability (S), Difficulty (D), and Lapses (L), the total number of times the card has been forgotten.

***

The retrievability of $t$ days since the last review:

$$R(t,S) = 0.9^{\frac{t}{S}}$$

***

The initial state after the first rating:
- Initial Stability: $$S_0(G) = w_0 \cdot 0.25 \cdot 2^{G-1}$$
- Initial Difficulty: $$D_0(G) = w_1 - (G - 3)$$
- Initial Lapses: $$L_0(G) = \max(0, 2-G)$$
  (This means $L_0$ is 1 if the first rating is `Again`, and 0 otherwise.)

***

The new difficulty after review:

$$D^\prime(D, R, G) = \max(0, D + R - 0.25 \cdot 2^{G-1} + 0.1)$$

***

The new stability after a successful review (the user pressed "Hard", "Good" or "Easy"):

$$S^\prime_r(S, D', R) = S \cdot \left(1 + e^{w_2} \cdot (D'+0.1)^{w_3} \cdot S^{w_4} \cdot (e^{(1-R) \cdot w_5}-1)\right)$$

***

The new stability after forgetting (the user pressed "Again"):

$$S^\prime_f(L) = w_0 \cdot e^{w_6 \cdot L}$$

In this version, the post-lapse stability only depends on the total number of lapses.

***

Updating lapses after review:

$$L' = L + \max(0, 2-G)$$

The lapse count is incremented by 1 each time the user presses `Again`.

***

# FSRS v0

- Difficulty range - $D\in [1,10]$
- Initial difficulty - $D_0 = 5$
- Initial stability - $S_0 = 2$
- Exponential forgetting curve model - $R = \exp(\ln 0.9 \cdot \cfrac{I}{S})$
- Stability updating formula after successful review - $S^\prime = S\cdot (1 + a \times D ^ {-b} \times S^{-c} \times (\exp(1 - R)-1))$
  - Stability increase coefficient - a = 60
  - Difficulty decay coefficient - b = 0.7
  - Stability decay coefficient - c = 0.2
- Stability after failed review - $S_{L} = S_0^{-f\cdot L}$
  - Number of lapses - $L >= 0$
  - Forgetting decay coefficient - f = 0.3
- Difficulty updating formula after review - $D^\prime = D - d\cdot(Grade - 1) - e\cdot(1-R)$
  - Rating range - $Grade\in \{0,1,2\}$
    - 0 means forgetting (failed review)
    - 1 indicates remembered (successful review)
    - 2 indicates so easy (successful review without effort)
  - Grading influence coefficient - d = 1
  - Retrievability influence coefficient - e = 1
- Adaptive initial stability  - $S_0 = \cfrac{\ln 0.9}{\cfrac{\sum_i^n \ln R_i \times I_i\times cnt_i}{\sum\limits_i^n I_i^2\times cnt_i}}$
    - A linear regression of the first review to the forgetting curve is performed to calculate the actual stability.
- Adaptive initial difficulty $D_0 = \cfrac{\ln R_t}{\ln R_c}^{\frac{1}{-b}} \times D_0$
  - Requested recall rate - $R_t$
  - Current recall rate - $R_c$
  - If $R_c < R_t$, increase the initial difficulty so that the growth rate of the review interval for new cards decreases, and vice versa

***

If you find this introduction helpful, I'd be grateful if you could give it a star:

[![Star on GitHub](https://img.shields.io/github/stars/open-spaced-repetition/awesome-fsrs.svg?style=social)](https://github.com/open-spaced-repetition/awesome-fsrs)