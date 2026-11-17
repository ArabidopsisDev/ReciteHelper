"""Reproduce the numerical illustration and figures; no learner data are simulated."""
from __future__ import annotations

import hashlib
import json
import math
import os
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
os.environ.setdefault("MPLCONFIGDIR", r"D:\Temp\ReciteHelperPaper\mpl-cache")
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager
from matplotlib.patches import FancyArrowPatch, Rectangle
import numpy as np

font_path = Path(r"D:\Temp\ReciteHelperPaper\fonts\NotoSansCJKsc-Regular.otf")
font_manager.fontManager.addfont(str(font_path))
plt.rcParams.update({"font.family": "Noto Sans CJK SC", "font.size": 9,
                     "mathtext.fontset": "stix", "axes.spines.top": False,
                     "axes.spines.right": False, "svg.fonttype": "path",
                     "axes.linewidth": 0.7, "savefig.facecolor": "white"})

parameter_source = (REPO / "ReciteHelper.Core/Scheduling/SchedulerParameters.cs").read_text(encoding="utf-8-sig")
parameter_block = re.search(r"DefaultWeights\s*=\s*\[(.*?)\];", parameter_source, re.S).group(1)
parameter_block = re.sub(r"//[^\n]*", "", parameter_block)
w = [float(value) for value in re.findall(r"\d+(?:\.\d+)?", parameter_block)]
assert len(w) == 21
beta = w[20]
factor = 0.9 ** (-1 / beta) - 1

def recall(stability: float, elapsed: float) -> float:
    return (1 + factor * max(0, elapsed) / stability) ** (-beta)

def after_good(stability: float, difficulty: float, elapsed: float) -> float:
    r = recall(stability, elapsed)
    return stability * (1 + math.exp(w[8]) * (11 - difficulty) * stability ** (-w[9])
                        * (math.exp(w[10] * (1-r)) - 1))

sa, sb = w[2], w[0]
sb_after = after_good(sb, w[4], 1)
before = 0.4 * recall(sa, 1)
after = before + 0.6
def projected(h: float) -> float:
    return 0.4 * recall(sa, 1+h) + 0.6 * recall(sb_after, h)

low, high = 0.0, max(sa, sb_after)
for _ in range(64):
    mid = (low+high)/2
    if projected(mid) > 0.9:
        low = mid
    else:
        high = mid
interval = (low+high)/2
assert abs(after-before-0.6) < 1e-12
assert abs(projected(interval)-0.9) < 1e-12

figdir = ROOT / "figures"
figdir.mkdir(exist_ok=True)
blue, amber, grey = "#24557A", "#B76528", "#6C7378"
fig, axes = plt.subplots(1, 2, figsize=(6.8, 2.65), gridspec_kw={"width_ratios": [1.6, 1]})
ax, at = axes
t_before = np.linspace(0, 1, 100)
t_after = np.linspace(1, 9, 300)
ta = np.linspace(0, 9, 300)
ax.plot(ta, [0.4*recall(sa, t) for t in ta], "--", color=grey, lw=1.25, label="病症组件的贡献")
ax.plot(t_after, [0.6*recall(sb_after, t-1) for t in t_after], "-.", color=amber, lw=1.25, label="循环组件的贡献")
ax.plot(t_before, [0.4*recall(sa, t) for t in t_before], color=blue, lw=1.7)
ax.plot(t_after, [projected(t-1) for t in t_after], color=blue, lw=1.7, label="原题掌握度指标")
ax.plot([1, 1], [before, after], color=blue, lw=1.1)
ax.scatter([1, 1], [before, after], s=17, color=blue, zorder=4)
ax.annotate(f"{before:.3f} → {after:.3f}", xy=(1, after), xytext=(2.1, 1.0), fontsize=8,
            arrowprops={"arrowstyle":"-", "lw":0.6, "color":grey})
ax.set(xlim=(0, 9), ylim=(0, 1.08), xlabel="距首次整题作答的时间 / d", ylabel="加权指标")
ax.axvline(1, color="#ADB1B4", lw=0.6, ls=":")
ax.legend(fontsize=7.6, frameon=False, loc="lower right", labelspacing=0.35)
ax.set_title("(a) 仅复习循环组件", fontsize=9, loc="left", pad=10)
ax.grid(axis="y", color="#E0E2E4", lw=0.5)
xleft, xright = np.linspace(0, 0.7, 120, endpoint=False), np.linspace(0.7, 1, 80)
at.plot(xleft, xleft, color=blue, lw=1.7)
at.plot(xright, np.ones_like(xright), color=blue, lw=1.7)
at.plot([0, 1], [0, 1], "--", color="#B9BDC1", lw=0.8)
at.scatter([0.7], [0.7], s=25, facecolors="white", edgecolors=blue, zorder=5)
at.scatter([0.7], [1], s=25, color=blue, zorder=5)
at.axvline(0.7, color=grey, lw=0.7, ls=":")
at.annotate("门槛 0.70", xy=(0.7, 0.16), xytext=(0.39, 0.16), fontsize=8)
at.set(xlim=(0, 1.02), ylim=(0, 1.08), xlabel="原始覆盖比例 s", ylabel="政策得分 gτ(s)")
at.set_title("(b) 课程评分门槛", fontsize=9, loc="left", pad=10)
at.grid(axis="y", color="#E0E2E4", lw=0.5)
fig.tight_layout(w_pad=2)
fig.savefig(figdir / "projection.svg", bbox_inches="tight")
fig.savefig(figdir / "projection.pdf", bbox_inches="tight")
plt.close(fig)

fig, ax = plt.subplots(figsize=(6.8, 1.55))
ax.set_xlim(0, 10)
ax.set_ylim(0, 2)
ax.axis("off")
nodes = [(0.05, "课程资料与范围", "可核对的原文依据"),
         (2.65, "自然分问与占比", "固定标识、范围和权重"),
         (5.25, "逐问作答与评分", "原始覆盖 s、达标状态 y"),
         (7.85, "独立 FSRS 状态", "每个组件的 S、D 与时钟")]
for x, title, detail in nodes:
    ax.add_patch(Rectangle((x, 0.85), 2.1, 0.9, facecolor="white", edgecolor="#535B61", lw=0.8))
    ax.text(x+1.05, 1.43, title, ha="center", va="center", fontsize=9)
    ax.text(x+1.05, 1.1, detail, ha="center", va="center", fontsize=7.3, color=grey)
for x in [2.15, 4.75, 7.35]:
    ax.add_patch(FancyArrowPatch((x, 1.3), (x+0.48, 1.3), arrowstyle="->", mutation_scale=9, lw=0.8, color=grey))
ax.add_patch(FancyArrowPatch((8.9, 0.85), (8.1, 0.26), connectionstyle="angle,angleA=-90,angleB=0,rad=0",
                            arrowstyle="->", mutation_scale=9, lw=0.8, color=blue))
ax.text(4.8, 0.24, "加权汇总 → 到期筛选与同源穿插 → 局部检索练习", ha="center", fontsize=8.5, color=blue)
ax.text(1.1, 0.24, "原题与组件保留关联", ha="center", fontsize=7.8, color=grey)
fig.savefig(figdir / "framework.svg", bbox_inches="tight")
fig.savefig(figdir / "framework.pdf", bbox_inches="tight")
plt.close(fig)

data = {"alpha": [0.4, 0.6], "fsrs_parameters": w, "stability_symptoms": sa,
        "stability_cycle_before": sb, "stability_cycle_after": sb_after,
        "mastery_before": before, "mastery_after": after, "combined_interval_days": interval,
        "note": "Analytical continuous interpolation using FSRS-6 defaults, not learner observations."}
(ROOT / "analysis.json").write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(data, ensure_ascii=False))

evidence = ["ReciteHelper.Core/Scheduling/FsrsAlgorithm.cs", "ReciteHelper.Core/Scheduling/AiGradingPolicy.cs",
            "ReciteHelper.Core/Scheduling/CfsrsPolicy.cs", "ReciteHelper.Core/Scheduling/FacetMemoryProjection.cs",
            "ReciteHelper.Core/Scheduling/FsrsOptimizer.cs", "ReciteHelper.Core/Services/EssayAnswerLayout.cs",
            "ReciteHelper.Application/Services/FacetReviewService.cs", "ReciteHelper.Application/Services/ReviewGenerator.cs"]
evidence += [str(p.relative_to(REPO)).replace("\\", "/") for p in (REPO / "docs/experiments").glob("cfsrs-*-2026-11-16.json")]
manifest = {"date": "2026-11-16", "base_commit": "f68557c353accb9885a0b8bfb0f70e57a64503d5",
            "includes_uncommitted_work": True,
            "sha256": {name: hashlib.sha256((REPO/name).read_bytes()).hexdigest() for name in evidence}}
(ROOT / "snapshot-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
