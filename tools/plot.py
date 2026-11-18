#!/usr/bin/env python3
"""Charts for the coursework experiments (E1–E3) from the summary CSVs.

Usage:
    python3 tools/plot.py [--results BookingSystem.Evaluation/results]

Reads summary_agent.csv, summary_agent_by_type.csv, summary_retrieval.csv
(written by BookingSystem.Evaluation) and writes PNGs to results/charts/.
Charts that have no data yet are skipped.

Design: one validated categorical order (blue, orange, aqua, yellow, magenta, green, violet, red), assigned to
entities in fixed order; thin bars with a 2px surface gap; recessive grid; a legend for >= 2
series; value labels on bars (the CSV files are the table view of every chart).
"""
import argparse
import csv
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

SURFACE = "#fcfcfb"
TEXT = "#0b0b0b"
TEXT_SECONDARY = "#52514e"
GRID = "#e4e3df"
SERIES = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"]
SEQUENTIAL = ["#cde2fb", "#9ec5f4", "#6da7ec", "#3987e5", "#256abf", "#184f95"]

plt.rcParams.update({
    "figure.facecolor": SURFACE, "axes.facecolor": SURFACE, "savefig.facecolor": SURFACE,
    "axes.edgecolor": GRID, "axes.labelcolor": TEXT_SECONDARY, "text.color": TEXT,
    "xtick.color": TEXT_SECONDARY, "ytick.color": TEXT_SECONDARY, "font.size": 10,
    "axes.spines.top": False, "axes.spines.right": False, "axes.spines.left": False,
    "axes.grid": True, "axes.grid.axis": "y", "grid.color": GRID, "grid.linewidth": 0.8,
    "legend.frameon": False, "figure.dpi": 150,
})


def read(path):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(f))


def num(row, key):
    try:
        return float(row.get(key, "") or "nan")
    except ValueError:
        return float("nan")


def short_model(model):
    return model.split(":", 1)[-1].replace("openai/", "")


def grouped_bars(ax, categories, series, values, errors=None, label_values=True, fmt="{:.2f}"):
    """series: list of names; values[s][c]; errors same shape or None."""
    n = len(series)
    width = 0.8 / max(n, 1)
    for i, name in enumerate(series):
        xs = [c + (i - (n - 1) / 2) * width for c in range(len(categories))]
        ys = values[i]
        bars = ax.bar(xs, ys, width=width, color=SERIES[i % len(SERIES)], edgecolor=SURFACE, linewidth=2,
                      label=name, zorder=3,
                      yerr=errors[i] if errors else None,
                      error_kw={"elinewidth": 1, "ecolor": TEXT_SECONDARY, "capsize": 2})
        if label_values:
            for bar, y in zip(bars, ys):
                if y == y:  # not NaN
                    ax.annotate(fmt.format(y), (bar.get_x() + bar.get_width() / 2, y),
                                xytext=(0, 3), textcoords="offset points", ha="center", va="bottom",
                                fontsize=7, color=TEXT_SECONDARY)
    ax.set_xticks(range(len(categories)))
    ax.set_xticklabels(categories)


def save(fig, out_dir, name):
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name)
    fig.tight_layout()
    fig.savefig(path, bbox_inches="tight")
    plt.close(fig)
    print("wrote", path)


def plot_e1(summary, out_dir):
    rows = [r for r in summary if r["experiment"] == "E1" and r["slot_filling"] == "on"]
    if not rows:
        return
    metrics = [("tool_selection_acc", "Tool selection"), ("arg_acc", "Argument accuracy"),
               ("task_success", "Task success"), ("clarification_acc", "Clarification")]
    models = [short_model(r["model"]) for r in rows]
    values = [[num(r, m + "_mean") for m, _ in metrics] for r in rows]
    errors = [[num(r, m + "_std") for m, _ in metrics] for r in rows]

    fig, ax = plt.subplots(figsize=(8, 4))
    grouped_bars(ax, [label for _, label in metrics], models, values, errors)
    ax.set_ylim(0, 1.1)
    ax.set_ylabel("Share (mean ± std over repeats)")
    ax.set_title("E1. Agent quality by model (slot filling on)", loc="left", fontsize=11)
    ax.legend(loc="upper center", bbox_to_anchor=(0.5, -0.1), ncol=len(models))
    save(fig, out_dir, "e1_agent_metrics.png")

    # Latency vs accuracy: one point per model, every point direct-labelled.
    fig, ax = plt.subplots(figsize=(6, 4))
    for i, r in enumerate(rows):
        x, y = num(r, "latency_ms_avg") / 1000, num(r, "task_success_mean")
        ax.scatter([x], [y], s=60, color=SERIES[i % len(SERIES)], edgecolor=SURFACE, linewidth=2, zorder=3)
        ax.annotate(short_model(r["model"]), (x, y), xytext=(6, 4), textcoords="offset points", fontsize=8)
    ax.set_xlabel("Average latency per turn, s")
    ax.set_ylabel("Task success")
    ax.set_ylim(0, 1.05)
    ax.grid(axis="x", color=GRID, linewidth=0.8)
    ax.set_title("E1. Latency vs task success", loc="left", fontsize=11)
    save(fig, out_dir, "e1_latency_vs_success.png")


def plot_by_type(by_type, out_dir):
    rows = [r for r in by_type if r["experiment"] == "E1" and r["slot_filling"] == "on"]
    if not rows:
        return
    models = sorted({r["model"] for r in rows})
    types = ["simple", "constraints", "incomplete", "multi-turn", "relative-dates", "faq", "booking", "out-of-scope"]
    types = [t for t in types if any(r["type"] == t for r in rows)]
    grid = [[next((num(r, "task_success_mean") for r in rows if r["model"] == m and r["type"] == t), float("nan"))
             for t in types] for m in models]

    fig, ax = plt.subplots(figsize=(9, 0.6 * len(models) + 1.6))
    from matplotlib.colors import LinearSegmentedColormap
    cmap = LinearSegmentedColormap.from_list("seq", SEQUENTIAL)
    ax.imshow(grid, cmap=cmap, vmin=0, vmax=1, aspect="auto")
    ax.set_xticks(range(len(types)))
    ax.set_xticklabels(types, rotation=20, ha="right")
    ax.set_yticks(range(len(models)))
    ax.set_yticklabels([short_model(m) for m in models])
    ax.grid(False)
    for i, row in enumerate(grid):
        for j, v in enumerate(row):
            if v == v:
                ax.text(j, i, f"{v:.2f}", ha="center", va="center", fontsize=8,
                        color="white" if v > 0.6 else TEXT)
    ax.set_title("E1. Task success by scenario type", loc="left", fontsize=11)
    save(fig, out_dir, "e1_success_by_type.png")


def plot_e2(summary, out_dir):
    rows = [r for r in summary if r["experiment"] == "E2"]
    if not rows:
        return
    models = sorted({r["model"] for r in rows})
    metrics = [("task_success", "Task success"), ("arg_acc", "Argument acc."),
               ("invalid_call_rate_executed", "Invalid calls executed"), ("clarification_acc", "Clarification")]
    fig, axes = plt.subplots(1, len(models), figsize=(5 * len(models), 4), sharey=True, squeeze=False)
    for ax, model in zip(axes[0], models):
        values, errors, names = [], [], []
        for mode, name in (("on", "Slot filling on"), ("off", "Slot filling off")):
            r = next((x for x in rows if x["model"] == model and x["slot_filling"] == mode), None)
            if r is None:
                continue
            names.append(name)
            values.append([num(r, m + "_mean") for m, _ in metrics])
            errors.append([num(r, m + "_std") for m, _ in metrics])
        grouped_bars(ax, [label for _, label in metrics], names, values, errors)
        ax.set_title(short_model(model), loc="left", fontsize=10)
        ax.tick_params(axis="x", labelrotation=15)
        ax.set_ylim(0, 1.1)
    axes[0][0].set_ylabel("Share (mean ± std over repeats)")
    axes[0][0].legend(loc="upper left")
    fig.suptitle("E2. Slot filling ablation", x=0.01, ha="left", fontsize=11)
    save(fig, out_dir, "e2_slot_filling_ablation.png")


def plot_e3(retrieval, out_dir):
    if not retrieval:
        return
    split = "test" if any(r["split"] == "test" for r in retrieval) else retrieval[0]["split"]
    methods = []
    for r in retrieval:
        if r["method"] not in methods:
            methods.append(r["method"])

    rows = {r["method"]: r for r in retrieval if r["split"] == split and r["kind"] == "all"}
    metrics = [("recall3", "Recall@3"), ("recall5", "Recall@5"), ("mrr10", "MRR@10")]
    fig, ax = plt.subplots(figsize=(8, 4))
    grouped_bars(ax, [label for _, label in metrics], methods,
                 [[num(rows[m], k) for k, _ in metrics] for m in methods if m in rows])
    ax.set_ylim(0, 1.1)
    ax.set_title(f"E3. Retrieval quality on the {split} split (n={rows[methods[0]]['queries']})", loc="left", fontsize=11)
    ax.legend(loc="upper center", bbox_to_anchor=(0.5, -0.1), ncol=3)
    save(fig, out_dir, "e3_retrieval.png")

    kinds = [("keyword", "Short term queries"), ("descriptive", "Descriptive questions")]
    by_kind = {(r["method"], r["kind"]): r for r in retrieval if r["split"] == split}
    fig, ax = plt.subplots(figsize=(8, 4))
    grouped_bars(ax, [label for _, label in kinds], methods,
                 [[num(by_kind.get((m, k), {}), "mrr10") for k, _ in kinds] for m in methods])
    ax.set_ylim(0, 1.1)
    ax.set_ylabel("MRR@10")
    ax.set_title(f"E3. MRR@10 by query kind ({split} split)", loc="left", fontsize=11)
    ax.legend(loc="upper center", bbox_to_anchor=(0.5, -0.1), ncol=3)
    save(fig, out_dir, "e3_retrieval_by_kind.png")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", default=os.path.join("BookingSystem.Evaluation", "results"))
    args = parser.parse_args()
    out_dir = os.path.join(args.results, "charts")

    summary = read(os.path.join(args.results, "summary_agent.csv"))
    plot_e1(summary, out_dir)
    plot_by_type(read(os.path.join(args.results, "summary_agent_by_type.csv")), out_dir)
    plot_e2(summary, out_dir)
    plot_e3(read(os.path.join(args.results, "summary_retrieval.csv")), out_dir)


if __name__ == "__main__":
    main()
