"""Draws the Section 4 dossier figures from the real-participant CSVs in this folder.

Reads sus_responses.csv and tlx_responses.csv (same files analyze_study.py uses) and writes
figures/sus_by_condition.png and figures/tlx_by_condition.png. Every dot is one participant.
"""
import csv
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "figures")
ORDER = [("voice", "Voice + gaze"), ("controller", "Controller")]


def read(name):
    with open(os.path.join(HERE, name), newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def sus_score(r):
    q = [int(r[f"q{i}"]) for i in range(1, 11)]
    return 2.5 * sum((v - 1) if i % 2 == 0 else (5 - v) for i, v in enumerate(q))


def tlx_score(r):
    keys = ["mental", "physical", "temporal", "performance", "effort", "frustration"]
    return sum(float(r[k]) for k in keys) / len(keys)


def plot(rows, fn, ylabel, title, filename, ref=None):
    groups = [[fn(r) for r in rows if r["condition"] == c] for c, _ in ORDER]
    labels = [f"{name}\n(n = {len(g)})" for (_, name), g in zip(ORDER, groups)]
    fig, ax = plt.subplots(figsize=(5, 4.2), dpi=200)
    ax.boxplot(groups, widths=0.5, showfliers=False, medianprops={"color": "black"})
    ax.set_xticks([1, 2], labels)
    for i, g in enumerate(groups, start=1):
        offsets = [(-0.08 + 0.16 * k / max(len(g) - 1, 1)) for k in range(len(g))]
        ax.scatter([i + o for o in offsets], g, s=22, zorder=3, alpha=0.8)
    if ref is not None:
        ax.axhline(ref[0], linestyle="--", linewidth=1, color="grey")
        ax.text(2.45, ref[0] + 1, ref[1], ha="right", fontsize=8, color="grey")
    ax.set_ylim(0, 100)
    ax.set_ylabel(ylabel)
    ax.set_title(title, fontsize=10)
    ax.spines[["top", "right"]].set_visible(False)
    fig.tight_layout()
    fig.savefig(os.path.join(OUT, filename))
    plt.close(fig)
    print("written:", os.path.join(OUT, filename))


def main():
    os.makedirs(OUT, exist_ok=True)
    plot(read("sus_responses.csv"), sus_score, "SUS score (0-100)",
         "System Usability Scale by condition", "sus_by_condition.png", ref=(68, "average usability (68)"))
    plot(read("tlx_responses.csv"), tlx_score, "Raw NASA-TLX (0-100, lower = less workload)",
         "Perceived workload by condition", "tlx_by_condition.png")


if __name__ == "__main__":
    main()
