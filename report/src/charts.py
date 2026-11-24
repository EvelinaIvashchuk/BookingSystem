#!/usr/bin/env python3
"""Графіки для пояснювальної записки (українською) з CSV-результатів BookingSystem.Evaluation.

Запуск:  tools/.venv/bin/python report/src/charts.py
Вихід:   report/figures/ch_*.png (300 dpi, білий фон — для друку).
"""
import csv
import json
import os
import re
import statistics

import locale

import matplotlib

matplotlib.use("Agg")
locale.setlocale(locale.LC_NUMERIC, "uk_UA.UTF-8")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.colors import LinearSegmentedColormap  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "BookingSystem.Evaluation", "results")
DS = os.path.join(ROOT, "BookingSystem.Evaluation", "datasets")
OUT = os.path.join(ROOT, "report", "figures")
os.makedirs(OUT, exist_ok=True)

SURFACE = "#ffffff"
TEXT = "#0b0b0b"
TEXT2 = "#52514e"
GRID = "#e4e3df"
BLUE, ORANGE, AQUA, YELLOW, MAGENTA, GREEN, VIOLET, RED = (
    "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948")
SEQ = ["#f4f8fe", "#cde2fb", "#9ec5f4", "#6da7ec", "#3987e5", "#256abf", "#184f95"]

plt.rcParams.update({
    "font.family": "Arial", "font.size": 9.5,
    "figure.facecolor": SURFACE, "axes.facecolor": SURFACE, "savefig.facecolor": SURFACE,
    "axes.edgecolor": GRID, "axes.labelcolor": TEXT2, "text.color": TEXT,
    "xtick.color": TEXT2, "ytick.color": TEXT2,
    "axes.spines.top": False, "axes.spines.right": False,
    "axes.grid": True, "grid.color": GRID, "grid.linewidth": 0.7,
    "legend.frameon": False, "figure.dpi": 100, "savefig.dpi": 300,
    "axes.formatter.use_locale": True,
})


def dc(text):
    """Десяткова кома для підписів-чисел (українська нотація)."""
    return re.sub(r"(?<=\d)\.(?=\d)", ",", text)

MODEL_LABEL = {
    "gemini:gemini-3.8-flash": "gemini-3.8-flash",
    "gemini:gemini-3.6-flash": "gemini-3.6-flash",
    "gemini:gemini-3.5-flash-lite": "gemini-3.5-flash-lite",
    "gemini:gemini-3.1-flash-lite": "gemini-3.1-flash-lite",
    "groq:openai/gpt-oss-120b": "gpt-oss-120b",
    "groq:openai/gpt-oss-20b": "gpt-oss-20b",
    "groq:qwen/qwen3.8-27b": "qwen3.8-27b",
}
MODEL_ORDER = list(MODEL_LABEL)
TYPE_LABEL = {
    "simple": "прості", "constraints": "обмеження", "incomplete": "неповні", "multi-turn": "багатокрокові",
    "relative-dates": "відносні дати", "faq": "правила (FAQ)", "booking": "бронювання", "out-of-scope": "поза темою",
}
TYPE_ORDER = list(TYPE_LABEL)


def read(name):
    with open(os.path.join(RES, name), encoding="utf-8") as f:
        return list(csv.DictReader(f))


def f(x):
    try:
        return float(x)
    except (TypeError, ValueError):
        return float("nan")


def save(fig, name):
    fig.savefig(os.path.join(OUT, name), bbox_inches="tight", pad_inches=0.08)
    plt.close(fig)
    print("wrote", name)


def hbar_labels(ax, bars, values, fmt="{:.3f}", pad=0.006):
    for b, v in zip(bars, values):
        ax.text(b.get_width() + pad, b.get_y() + b.get_height() / 2, dc(fmt.format(v)),
                va="center", ha="left", fontsize=8.5, color=TEXT2)


# ---------------------------------------------------------------- 1. Склад датасетів
def datasets():
    agent = json.load(open(os.path.join(DS, "agent_scenarios.json"), encoding="utf-8"))["scenarios"]
    sec = json.load(open(os.path.join(DS, "security_scenarios.json"), encoding="utf-8"))["scenarios"]
    qrels = json.load(open(os.path.join(DS, "retrieval_qrels.json"), encoding="utf-8"))["queries"]

    fig, axes = plt.subplots(1, 3, figsize=(10.5, 3.3), gridspec_kw={"width_ratios": [1.25, 1, 1]})

    counts = [sum(1 for s in agent if s["type"] == t) for t in TYPE_ORDER]
    ax = axes[0]
    ys = range(len(TYPE_ORDER))[::-1]
    bars = ax.barh(list(ys), counts, color=BLUE, height=0.62, edgecolor=SURFACE, linewidth=2, zorder=3)
    ax.set_yticks(list(ys))
    ax.set_yticklabels([TYPE_LABEL[t] for t in TYPE_ORDER])
    hbar_labels(ax, bars, counts, "{:d}", 0.3)
    ax.set_title(f"а) сценарії агента (E1, E2), n = {len(agent)}", loc="left", fontsize=10)
    ax.set_xlim(0, max(counts) * 1.18)
    ax.grid(axis="y", visible=False)

    sec_types = ["leak", "override", "exfiltration", "action", "indirect", "benign"]
    sec_lbl = {"leak": "витік інструкцій", "override": "зміна ролі", "exfiltration": "чужі дані",
               "action": "заборонені дії", "indirect": "непряма ін'єкція", "benign": "легітимні (контроль)"}
    counts = [sum(1 for s in sec if s["type"] == t) for t in sec_types]
    ax = axes[1]
    ys = range(len(sec_types))[::-1]
    bars = ax.barh(list(ys), counts, color=BLUE, height=0.62, edgecolor=SURFACE, linewidth=2, zorder=3)
    ax.set_yticks(list(ys))
    ax.set_yticklabels([sec_lbl[t] for t in sec_types])
    hbar_labels(ax, bars, counts, "{:d}", 0.1)
    ax.set_title(f"б) атакувальні сценарії (E4), n = {len(sec)}", loc="left", fontsize=10)
    ax.set_xlim(0, max(counts) * 1.25)
    ax.grid(axis="y", visible=False)

    ax = axes[2]
    groups = [("uk", "keyword"), ("uk", "descriptive"), ("en", "keyword"), ("en", "descriptive")]
    lbl = {"keyword": "короткі", "descriptive": "описові", "uk": "укр.", "en": "англ."}
    counts = [sum(1 for q in qrels if q["lang"] == s and q["kind"] == k) for s, k in groups]
    ys = range(len(groups))[::-1]
    bars = ax.barh(list(ys), counts, color=BLUE, height=0.62, edgecolor=SURFACE, linewidth=2, zorder=3)
    ax.set_yticks(list(ys))
    ax.set_yticklabels([f"{lbl[k]}, {lbl[s]}" for s, k in groups])
    hbar_labels(ax, bars, counts, "{:d}", 0.3)
    ax.set_title(f"в) запити до бази знань (E3), n = {len(qrels)}, 5 фолдів", loc="left", fontsize=10)
    ax.set_xlim(0, max(counts) * 1.2)
    ax.grid(axis="y", visible=False)
    for a in axes:
        a.set_xlabel("кількість")
        a.spines["left"].set_visible(False)
    fig.tight_layout(w_pad=2.2)
    save(fig, "ch_datasets.png")


# ---------------------------------------------------------------- 2. E1: метрики за моделями
def e1_metrics():
    rows = [r for r in read("summary_agent.csv") if r["experiment"] == "E1"]
    by = {r["model"]: r for r in rows}
    models = [m for m in MODEL_ORDER if m in by]
    metrics = [("task_success", "Task success"), ("tool_selection_acc", "Tool selection"),
               ("arg_acc", "Argument accuracy")]
    fig, axes = plt.subplots(1, 3, figsize=(10.5, 3.4), sharey=True)
    ys = list(range(len(models)))[::-1]
    for ax, (key, title) in zip(axes, metrics):
        vals = [f(by[m][key + "_mean"]) for m in models]
        errs = [f(by[m][key + "_std"]) for m in models]
        bars = ax.barh(ys, vals, color=BLUE, height=0.62, edgecolor=SURFACE, linewidth=2, zorder=3,
                       xerr=errs, error_kw={"elinewidth": 0.9, "ecolor": TEXT2, "capsize": 2})
        hbar_labels(ax, bars, vals, "{:.3f}", 0.012)
        ax.set_xlim(0, 1.17)
        ax.set_xticks([0, 0.25, 0.5, 0.75, 1.0])
        ax.set_title(title, loc="left", fontsize=10)
        ax.grid(axis="y", visible=False)
        ax.spines["left"].set_visible(False)
    axes[0].set_yticks(ys)
    axes[0].set_yticklabels([MODEL_LABEL[m] for m in models])
    fig.supxlabel("частка сценаріїв (середнє ± std за повторами)", fontsize=9, color=TEXT2)
    fig.tight_layout(w_pad=1.5)
    save(fig, "ch_e1_metrics.png")


# ---------------------------------------------------------------- 3. E1: якість vs затримка і вартість
def e1_tradeoffs():
    rows = {r["model"]: r for r in read("summary_agent.csv") if r["experiment"] == "E1"}
    runs = [r for r in read("agent_runs.csv") if r["experiment"] == "E1"]
    # Медіанна затримка ходу з сирих прогонів (у Groq середнє спотворене очікуванням через ліміти).
    med = {}
    for m in rows:
        lat = []
        for r in runs:
            if r["model"] != m:
                continue
            for x in (r["latency_ms_turns"] or "").replace(";", "|").split("|"):
                if x.strip():
                    lat.append(f(x) / 1000)
        med[m] = statistics.median(lat) if lat else float("nan")

    fig, axes = plt.subplots(1, 2, figsize=(10.5, 3.8), sharey=True)
    ax = axes[0]
    offsets = {
        "gemini:gemini-3.8-flash": (-6, 7, "right"), "gemini:gemini-3.6-flash": (6, -12, "left"),
        "gemini:gemini-3.5-flash-lite": (6, 5, "left"), "gemini:gemini-3.1-flash-lite": (6, -10, "left"),
        "groq:openai/gpt-oss-120b": (-6, 6, "right"), "groq:openai/gpt-oss-20b": (-6, -12, "right"),
        "groq:qwen/qwen3.8-27b": (6, 0, "left"),
    }
    for m, r in rows.items():
        x, y = med[m], f(r["task_success_mean"])
        ax.scatter([x], [y], s=46, color=BLUE, edgecolor=SURFACE, linewidth=1.5, zorder=3)
        dx, dy, ha = offsets[m]
        ax.annotate(MODEL_LABEL[m], (x, y), xytext=(dx, dy), textcoords="offset points", ha=ha,
                    fontsize=8.5, color=TEXT)
    ax.set_xscale("log")
    ax.set_xlim(0.6, 12)
    ax.set_xticks([0.7, 1, 2, 4, 8])
    ax.set_xticklabels(["0,7", "1", "2", "4", "8"])
    ax.set_xlabel("медіанна затримка ходу, с (логарифмічна шкала)")
    ax.set_ylabel("Task success")
    ax.set_title("а) якість і швидкість", loc="left", fontsize=10)
    ax.set_ylim(0.45, 1.03)

    ax = axes[1]
    for m, r in rows.items():
        cost = f(r["cost_per_turn_usd_mean"])
        # вартість на сценарій ≈ на хід × середня кількість ходів; беремо з сирих рядків, щоб бути точними
        c = [f(x["cost_usd"]) for x in runs if x["model"] == m and x["cost_usd"]]
        scen_cost = statistics.mean(c) if c else cost
        y = f(r["task_success_mean"])
        ax.scatter([scen_cost * 100], [y], s=46, color=BLUE, edgecolor=SURFACE, linewidth=1.5, zorder=3)
        lbl = MODEL_LABEL[m] + (" (free tier)" if m.startswith("groq") else "")
        if m == "gemini:gemini-3.8-flash":
            lbl = "gemini-3.8-flash і 3.6-flash"
        if m == "gemini:gemini-3.6-flash":
            continue
        dx, dy, ha = {"gemini:gemini-3.8-flash": (-8, 6, "right"), "gemini:gemini-3.6-flash": (-8, -11, "right"),
                      "gemini:gemini-3.5-flash-lite": (6, 3, "left"), "gemini:gemini-3.1-flash-lite": (6, -10, "left"),
                      "groq:openai/gpt-oss-120b": (-7, 3, "right"), "groq:openai/gpt-oss-20b": (-7, -9, "right"),
                      "groq:qwen/qwen3.8-27b": (-7, -3, "right")}[m]
        ax.annotate(lbl, (scen_cost * 100, y), xytext=(dx, dy), textcoords="offset points", ha=ha,
                    fontsize=8.5, color=TEXT)
    ax.set_xlim(-0.42, 0.8)
    ax.set_xticks([0, 0.2, 0.4, 0.6, 0.8])
    ax.set_xlabel("середня вартість одного сценарію, центів США")
    ax.set_title("б) якість і вартість", loc="left", fontsize=10)
    fig.tight_layout(w_pad=2)
    save(fig, "ch_e1_tradeoffs.png")
    return med


# ---------------------------------------------------------------- 4. E1: теплокарта за типами
def heat(ax, matrix, rows, cols, vmin=0, vmax=1, fmt="{:.2f}", cmap=None):
    cmap = cmap or LinearSegmentedColormap.from_list("seq", SEQ)
    im = ax.imshow(matrix, cmap=cmap, vmin=vmin, vmax=vmax, aspect="auto")
    ax.set_xticks(range(len(cols)))
    ax.set_xticklabels(cols, rotation=0)
    ax.set_yticks(range(len(rows)))
    ax.set_yticklabels(rows)
    ax.grid(False)
    for s in ax.spines.values():
        s.set_visible(False)
    ax.set_xticks([x - 0.5 for x in range(1, len(cols))], minor=True)
    ax.set_yticks([y - 0.5 for y in range(1, len(rows))], minor=True)
    ax.grid(which="minor", color=SURFACE, linewidth=2)
    ax.tick_params(which="minor", length=0)
    ax.tick_params(which="major", length=0)
    for i, row in enumerate(matrix):
        for j, v in enumerate(row):
            if v != v:
                ax.text(j, i, "—", ha="center", va="center", fontsize=8.5, color=TEXT2)
                continue
            dark = (v - vmin) / (vmax - vmin) > 0.62
            ax.text(j, i, dc(fmt.format(v)), ha="center", va="center", fontsize=8.5,
                    color="#ffffff" if dark else TEXT)
    return im


def e1_by_type():
    rows = [r for r in read("summary_agent_by_type.csv") if r["experiment"] == "E1"]
    models = [m for m in MODEL_ORDER if any(r["model"] == m for r in rows)]
    mat = []
    for m in models:
        line = []
        for t in TYPE_ORDER:
            v = [f(r["task_success_mean"]) for r in rows if r["model"] == m and r["type"] == t]
            line.append(v[0] if v else float("nan"))
        mat.append(line)
    fig, ax = plt.subplots(figsize=(10.5, 3.6))
    im = heat(ax, mat, [MODEL_LABEL[m] for m in models], [TYPE_LABEL[t] for t in TYPE_ORDER])
    ax.xaxis.tick_top()
    cb = fig.colorbar(im, ax=ax, fraction=0.025, pad=0.015)
    cb.outline.set_visible(False)
    cb.set_label("Task success", color=TEXT2)
    fig.tight_layout()
    save(fig, "ch_e1_by_type.png")


# ---------------------------------------------------------------- 5. E1: розподіл затримок
def e1_latency():
    runs = [r for r in read("agent_runs.csv") if r["experiment"] == "E1"]
    models = [m for m in MODEL_ORDER if any(r["model"] == m for r in runs)]
    data = []
    for m in models:
        lat = []
        for r in runs:
            if r["model"] == m:
                for x in (r["latency_ms_turns"] or "").replace(";", "|").split("|"):
                    if x.strip():
                        lat.append(f(x) / 1000)
        data.append(lat)
    fig, ax = plt.subplots(figsize=(10.5, 3.4))
    bp = ax.boxplot(data, vert=False, widths=0.55, patch_artist=True, showfliers=True,
                    flierprops={"marker": "o", "markersize": 2.5, "markerfacecolor": TEXT2,
                                "markeredgecolor": "none", "alpha": 0.5},
                    medianprops={"color": TEXT, "linewidth": 1.4},
                    whiskerprops={"color": TEXT2}, capprops={"color": TEXT2},
                    boxprops={"edgecolor": BLUE, "linewidth": 1})
    for b in bp["boxes"]:
        b.set_facecolor("#cde2fb")
    ax.set_yticks(range(1, len(models) + 1))
    ax.set_yticklabels([f"{MODEL_LABEL[m]}  (медіана {dc(f'{statistics.median(d):.1f}')} с)" for m, d in zip(models, data)])
    ax.invert_yaxis()
    ax.set_xscale("log")
    ax.set_xticks([0.5, 1, 2, 5, 10, 30, 100, 300])
    ax.set_xticklabels(["0,5", "1", "2", "5", "10", "30", "100", "300"])
    ax.set_xlabel("затримка одного ходу агента, с (логарифмічна шкала)")
    ax.grid(axis="y", visible=False)
    fig.tight_layout()
    save(fig, "ch_e1_latency.png")


# ---------------------------------------------------------------- 6. E2: ablation
def e2():
    rows = [r for r in read("summary_agent.csv") if r["experiment"] == "E2"]
    models = ["gemini:gemini-3.8-flash", "gemini:gemini-3.5-flash-lite"]
    metrics = [("task_success", "Task success"), ("tool_selection_acc", "Tool selection"),
               ("arg_acc", "Arg. accuracy"), ("clarification_acc", "Clarification")]
    fig, axes = plt.subplots(1, 2, figsize=(10.5, 3.5), sharey=True)
    for ax, m in zip(axes, models):
        width = 0.36
        for i, (mode, color, lbl) in enumerate([("on", BLUE, "slot filling увімкнено"),
                                                ("off", ORANGE, "slot filling вимкнено")]):
            r = next(x for x in rows if x["model"] == m and x["slot_filling"] == mode)
            xs = [k + (i - 0.5) * width for k in range(len(metrics))]
            vals = [f(r[k + "_mean"]) for k, _ in metrics]
            errs = [f(r[k + "_std"]) for k, _ in metrics]
            bars = ax.bar(xs, vals, width=width, color=color, edgecolor=SURFACE, linewidth=2, zorder=3,
                          label=lbl, yerr=errs, error_kw={"elinewidth": 0.9, "ecolor": TEXT2, "capsize": 2})
            for b, v, e in zip(bars, vals, errs):
                ax.text(b.get_x() + b.get_width() / 2, v + (e if e == e else 0) + 0.002, dc(f"{v:.3f}"),
                        ha="center", va="bottom", fontsize=7.5, color=TEXT2)
        ax.set_xticks(range(len(metrics)))
        ax.set_xticklabels([t for _, t in metrics])
        ax.set_ylim(0.85, 1.05)
        ax.set_title(MODEL_LABEL[m], loc="left", fontsize=10)
        ax.grid(axis="x", visible=False)
    axes[0].set_ylabel("частка сценаріїв (середнє ± std)")
    handles, labels = axes[0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="lower center", ncol=2, bbox_to_anchor=(0.5, -0.04))
    fig.tight_layout(rect=(0, 0.06, 1, 1))
    save(fig, "ch_e2_ablation.png")


# ---------------------------------------------------------------- 7. E3: методи пошуку (5-кратна крос-валідація)
METHOD_LABEL = {
    "Vector": "Vector (векторний)",
    "BM25": "BM25 (власний)",
    "MySqlFullText": "MySQL FULLTEXT",
    "BM25+PRF": "BM25 + PRF",
    "HybridRrf": "HybridRrf (k = 60)",
    "HybridAdaptive(initial)": "Adaptive, початкові",
    "HybridAdaptive(cv,free)": "Adaptive, CV без обмеження",
    "HybridAdaptive(cv)": "Adaptive, CV, w_ft ≥ 0,1",
    "HybridAdaptive(cv,PRF)": "Adaptive + PRF, CV (сайт)",
}
SITE_METHOD = "HybridAdaptive(cv,PRF)"


def e3_methods():
    rows = read("summary_retrieval_cv.csv")
    methods = list(METHOD_LABEL)
    groups = [("all", "усі запити"), ("keyword", "короткі з термінами"), ("descriptive", "описові")]
    fig, axes = plt.subplots(1, 3, figsize=(10.5, 3.9), sharey=True)
    ys = list(range(len(methods)))[::-1]
    for ax, (g, title) in zip(axes, groups):
        sel = {r["method"]: r for r in rows if r["group"] == g}
        vals = [f(sel[m]["mrr10"]) for m in methods]
        colors = [ORANGE if m == SITE_METHOD else (BLUE if m == "Vector" else "#9ec5f4") for m in methods]
        bars = ax.barh(ys, vals, color=colors, height=0.62, edgecolor=SURFACE, linewidth=2, zorder=3)
        hbar_labels(ax, bars, vals, "{:.3f}", 0.012)
        ax.set_xlim(0, 1.17)
        ax.set_title(f"{title} ({sel['Vector']['queries']})", loc="left", fontsize=10)
        ax.grid(axis="y", visible=False)
        ax.spines["left"].set_visible(False)
    axes[0].set_yticks(ys)
    axes[0].set_yticklabels([METHOD_LABEL[m] for m in methods])
    fig.supxlabel("MRR@10 поза фолдом навчання (5-кратна крос-валідація); помаранчевий — метод сайту, синій — Vector",
                  fontsize=9, color=TEXT2)
    fig.tight_layout(w_pad=1.5)
    save(fig, "ch_e3_methods.png")


def e3_recall():
    rows = [r for r in read("summary_retrieval_cv.csv") if r["group"] == "all"]
    methods = list(METHOD_LABEL)
    fig, ax = plt.subplots(figsize=(10.5, 3.5))
    width = 0.27
    for i, (key, color, lbl) in enumerate([("recall3", BLUE, "Recall@3"), ("recall5", ORANGE, "Recall@5"),
                                           ("mrr10", AQUA, "MRR@10")]):
        xs = [k + (i - 1) * width for k in range(len(methods))]
        vals = [f(next(r for r in rows if r["method"] == m)[key]) for m in methods]
        bars = ax.bar(xs, vals, width=width, color=color, edgecolor=SURFACE, linewidth=2, zorder=3, label=lbl)
        for b, v in zip(bars, vals):
            ax.text(b.get_x() + b.get_width() / 2, v + 0.01, dc(f"{v:.2f}"), ha="center", va="bottom",
                    fontsize=7, color=TEXT2)
    ax.set_xticks(range(len(methods)))
    ax.set_xticklabels([METHOD_LABEL[m].replace(" (", "\n(").replace(", ", ",\n") for m in methods], fontsize=8)
    ax.set_ylim(0, 1.12)
    ax.set_ylabel("значення метрики (205 запитів)")
    ax.grid(axis="x", visible=False)
    ax.legend(loc="upper right", ncol=3, bbox_to_anchor=(1, 1.12))
    fig.tight_layout()
    save(fig, "ch_e3_recall.png")


def e3_bootstrap():
    data = {c["comparison"]: c for c in json.load(open(os.path.join(RES, "retrieval_cv_bootstrap.json"), encoding="utf-8"))["comparisons"]
            if c["group"] == "all"}
    order = [
        ("HybridAdaptive(cv,PRF) - Vector", "Adaptive + PRF − Vector"),
        ("HybridAdaptive(cv,PRF) - HybridAdaptive(cv)", "Adaptive + PRF − Adaptive (w_ft ≥ 0,1)"),
        ("HybridAdaptive(cv) - Vector", "Adaptive (w_ft ≥ 0,1) − Vector"),
        ("HybridAdaptive(cv,free) - Vector", "Adaptive без обмеження − Vector"),
        ("HybridAdaptive(cv) - HybridRrf", "Adaptive (w_ft ≥ 0,1) − RRF"),
        ("HybridRrf - Vector", "RRF − Vector"),
        ("BM25+PRF - BM25", "BM25 + PRF − BM25"),
        ("BM25 - MySqlFullText", "BM25 − MySQL FULLTEXT"),
    ]
    fig, ax = plt.subplots(figsize=(10.5, 3.6))
    ys = list(range(len(order)))[::-1]
    for y, (key, _) in zip(ys, order):
        c = data[key]
        lo, hi = c["ci95"]
        m = c["meanDifference"]
        ax.plot([lo, hi], [y, y], color=BLUE, linewidth=2, solid_capstyle="round", zorder=3)
        ax.scatter([m], [y], s=48, color=BLUE, edgecolor=SURFACE, linewidth=1.5, zorder=4)
        ax.text(0.27, y, dc(f"Δ = {m:+.3f}   95 % ДІ [{lo:+.3f}; {hi:+.3f}]   краще/гірше: {c['better']}/{c['worse']}"),
                va="center", fontsize=8.5, color=TEXT2)
    ax.axvline(0, color=TEXT2, linewidth=1, linestyle=(0, (3, 3)))
    ax.set_yticks(ys)
    ax.set_yticklabels([lbl for _, lbl in order])
    ax.set_xlim(-0.16, 0.62)
    ax.set_xticks([-0.1, 0, 0.1, 0.2])
    ax.set_xlabel("різниця MRR@10 (205 запитів), точка — середнє, відрізок — 95 % довірчий інтервал (10 000 бутстреп-вибірок)")
    ax.grid(axis="y", visible=False)
    ax.spines["left"].set_visible(False)
    fig.tight_layout()
    save(fig, "ch_e3_bootstrap.png")


def e3_weights():
    def wft(n, d, num, p):
        raw = p["a"] + p["b"] * d - p["c"] * max(0, n - p["n0"]) + p["e"] * num
        return min(max(raw, p["min"]), p["max"])

    t = json.load(open(os.path.join(RES, "retrieval_tuned_params_cv.json"), encoding="utf-8"))["prf"]
    site = {"a": t["A"], "b": t["B"], "c": t["C"], "n0": t["N0"], "e": t["E"], "min": t["MinWeight"], "max": t["MaxWeight"]}
    init = {"a": 0.45, "b": 0.40, "c": 0.05, "n0": 4, "e": 0.10, "min": 0.15, "max": 0.85}
    ns = list(range(1, 13))
    fig, axes = plt.subplots(1, 2, figsize=(10.5, 3.5), sharey=True)
    panels = [
        (axes[0], init, "а) початкові параметри",
         [(1.0, 0, BLUE, "усі слова — терміни (d = 1)"), (0.5, 0, ORANGE, "половина — терміни (d = 0,5)"),
          (0.0, 0, AQUA, "без термінів (d = 0)")]),
        (axes[1], site, "б) параметри сайту (5-кратна крос-валідація, b = 0)",
         [(0.0, 1, ORANGE, "запит із числом (num = 1)"), (0.0, 0, BLUE, "запит без числа (num = 0)")]),
    ]
    for ax, p, title, lines in panels:
        for d, num, color, lbl in lines:
            ys = [wft(n, d, num, p) for n in ns]
            ax.plot(ns, ys, color=color, linewidth=2, marker="o", markersize=4.5,
                    markeredgecolor=SURFACE, markeredgewidth=1, label=lbl, zorder=3)
        ax.set_title(title, loc="left", fontsize=10)
        ax.set_xlabel("кількість змістовних слів у запиті, n")
        ax.set_xticks(ns)
        ax.set_ylim(0, 1)
        ax.legend(loc="upper right", fontsize=8.5)
    axes[0].set_ylabel("вага повнотекстового пошуку w_ft\n(w_vec = 1 − w_ft)")
    fig.tight_layout()
    save(fig, "ch_e3_weights.png")


def e3_folds():
    rows = read("retrieval_cv_runs.csv")
    methods = [("Vector", BLUE), ("HybridAdaptive(cv)", "#9ec5f4"), ("HybridAdaptive(cv,PRF)", ORANGE)]
    folds = sorted({int(r["fold"]) for r in rows})
    fig, ax = plt.subplots(figsize=(10.5, 3.0))
    width = 0.27
    for i, (m, color) in enumerate(methods):
        vals = []
        for fo in folds:
            xs = [f(r["mrr10"]) for r in rows if r["method"] == m and int(r["fold"]) == fo]
            vals.append(sum(xs) / len(xs))
        pos = [k + (i - 1) * width for k in range(len(folds))]
        bars = ax.bar(pos, vals, width=width, color=color, edgecolor=SURFACE, linewidth=2, zorder=3, label=METHOD_LABEL[m])
        for b, v in zip(bars, vals):
            ax.text(b.get_x() + b.get_width() / 2, v + 0.004, dc(f"{v:.3f}"), ha="center", va="bottom", fontsize=7.5, color=TEXT2)
    counts = [sum(1 for r in rows if r["method"] == "Vector" and int(r["fold"]) == fo) for fo in folds]
    ax.set_xticks(range(len(folds)))
    ax.set_xticklabels([f"фолд {fo}\n({c} запитів)" for fo, c in zip(folds, counts)])
    ax.set_ylim(0.8, 1.04)
    ax.set_ylabel("MRR@10 на відкладеному фолді")
    ax.grid(axis="x", visible=False)
    ax.legend(loc="upper left", ncol=3, bbox_to_anchor=(0, 1.16))
    fig.tight_layout()
    save(fig, "ch_e3_folds.png")


# ---------------------------------------------------------------- 8. E1 → E1-v2
def e1v2():
    data = [  # з results/README.md, розділ 5 (24 спільні сценарії)
        ("gemini-3.6-flash", 0.958, 0.944),
        ("gemini-3.5-flash-lite", 0.875, 0.931),
        ("gemini-3.1-flash-lite", 0.778, 0.944),
        ("gpt-oss-120b", 0.833, 0.958),
        ("gpt-oss-20b", 0.875, 0.958),
        ("усі моделі разом", 0.867, 0.943),
    ]
    booking = [
        ("gemini-3.6-flash", 0.833, 0.778),
        ("gemini-3.5-flash-lite", 0.667, 0.833),
        ("gemini-3.1-flash-lite", 0.333, 1.000),
        ("gpt-oss-120b", 0.500, 0.833),
        ("gpt-oss-20b", 0.833, 1.000),
        ("усі моделі разом", 0.621, 0.879),
    ]
    fig, axes = plt.subplots(1, 2, figsize=(10.5, 3.4), sharey=True)
    for ax, rows, title, xlim in [(axes[0], data, "а) 24 спільні сценарії (booking, incomplete, relative-dates)", (0.70, 1.02)),
                                  (axes[1], booking, "б) лише бронювання B-01…B-06", (0.25, 1.08))]:
        ys = list(range(len(rows)))[::-1]
        for y, (name, a, b) in zip(ys, rows):
            ax.plot([a, b], [y, y], color="#c3c2b7", linewidth=2, zorder=2)
            ax.scatter([a], [y], s=46, color=BLUE, edgecolor=SURFACE, linewidth=1.5, zorder=3,
                       label="E1 (до покращень)" if y == ys[0] else None)
            ax.scatter([b], [y], s=46, color=ORANGE, edgecolor=SURFACE, linewidth=1.5, zorder=3,
                       label="E1-v2 (після)" if y == ys[0] else None)
            ax.text(max(a, b) + (xlim[1] - xlim[0]) * 0.03, y, dc(f"{a:.3f} → {b:.3f}"), va="center", fontsize=8, color=TEXT2)
        ax.set_yticks(ys)
        ax.set_yticklabels([r[0] for r in rows])
        ax.set_xlim(*xlim)
        ax.set_title(title, loc="left", fontsize=10)
        ax.grid(axis="y", visible=False)
        ax.spines["left"].set_visible(False)
        ax.set_xlabel("Task success")
    for lbl in axes[0].get_yticklabels():
        if lbl.get_text() == "усі моделі разом":
            lbl.set_fontweight("bold")
    handles, labels = axes[0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="lower center", ncol=2, bbox_to_anchor=(0.5, -0.04))
    fig.tight_layout(rect=(0, 0.06, 1, 1))
    save(fig, "ch_e1v2.png")


# ---------------------------------------------------------------- 9. E4
def e4():
    rows = read("summary_security.csv")
    models = ["gemini:gemini-3.6-flash", "gemini:gemini-3.5-flash-lite", "gemini:gemini-3.1-flash-lite",
              "groq:openai/gpt-oss-120b", "groq:openai/gpt-oss-20b"]
    types = [("leak", "витік\nінструкцій"), ("override", "зміна\nролі"), ("exfiltration", "чужі\nдані"),
             ("action", "заборонені\nдії"), ("indirect", "непряма\nін'єкція"), ("ALL_ATTACKS", "ASR\nусі атаки"),
             ("benign", "надмірні відмови\n(легітимні)")]
    mat = []
    for m in models:
        line = []
        for t, _ in types:
            r = next(x for x in rows if x["model"] == m and x["type"] == t)
            line.append(f(r["attack_success_rate"]))
        mat.append(line)
    fig, ax = plt.subplots(figsize=(10.5, 2.8))
    im = heat(ax, mat, [MODEL_LABEL[m] for m in models], [t for _, t in types], vmin=0, vmax=0.5, fmt="{:.3f}")
    ax.xaxis.tick_top()
    ax.axvline(5.5, color=TEXT, linewidth=1)
    cb = fig.colorbar(im, ax=ax, fraction=0.025, pad=0.015)
    cb.outline.set_visible(False)
    cb.set_label("частка успішних атак\n(для легітимних — відмов)", color=TEXT2)
    fig.tight_layout()
    save(fig, "ch_e4.png")


# ---------------------------------------------------------------- 10. Токени та вартість на хід
def e1_tokens():
    rows = {r["model"]: r for r in read("summary_agent.csv") if r["experiment"] == "E1"}
    runs = [r for r in read("agent_runs.csv") if r["experiment"] == "E1"]
    models = [m for m in MODEL_ORDER if m in rows]
    inp, out = [], []
    for m in models:
        rr = [r for r in runs if r["model"] == m]
        turns = sum(int(r["turns"]) for r in rr)
        inp.append(sum(f(r["input_tokens"]) for r in rr) / turns)
        out.append(sum(f(r["output_tokens"]) for r in rr) / turns)
    fig, ax = plt.subplots(figsize=(10.5, 3.1))
    ys = list(range(len(models)))[::-1]
    b1 = ax.barh(ys, inp, color=BLUE, height=0.62, edgecolor=SURFACE, linewidth=2, zorder=3, label="вхідні токени")
    b2 = ax.barh(ys, out, left=inp, color=ORANGE, height=0.62, edgecolor=SURFACE, linewidth=2, zorder=3,
                 label="вихідні токени (разом із «thinking»)")
    for y, a, b in zip(ys, inp, out):
        ax.text(a + b + 60, y, f"{a:,.0f} + {b:,.0f}".replace(",", " "), va="center", fontsize=8.5, color=TEXT2)
    ax.set_yticks(ys)
    ax.set_yticklabels([MODEL_LABEL[m] for m in models])
    ax.set_xlabel("середня кількість токенів на один хід агента (усі виклики моделі в межах ходу)")
    ax.set_xlim(0, 7600)
    ax.grid(axis="y", visible=False)
    ax.spines["left"].set_visible(False)
    ax.legend(loc="lower right")
    fig.tight_layout()
    save(fig, "ch_e1_tokens.png")


if __name__ == "__main__":
    datasets()
    e1_metrics()
    e1_tradeoffs()
    e1_by_type()
    e1_latency()
    e1_tokens()
    e2()
    e3_methods()
    e3_recall()
    e3_bootstrap()
    e3_weights()
    e3_folds()
    e1v2()
    e4()
