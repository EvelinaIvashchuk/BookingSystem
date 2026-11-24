#!/usr/bin/env python3
"""E6: puts all rankers on one table: baselines, LightGBM (E5), two neural networks and Gemini (zero-shot ranker).

Usage (repository root; after train.py, train_nn.py and gemini_rank.py):
    tools/.venv/bin/python tools/ranking/compare_models.py

Reads the per-wish test metrics of every method (same 160 test wishes), computes means with 95 % bootstrap CIs (10 000
resamples over wishes) and paired differences between selected methods, and draws the figures.
Outputs: results/ranking/comparison.json, report/figures/ch_e6_methods.png, ch_e6_split.png, ch_e6_needs.png
"""
import csv
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import train as base  # noqa: E402

RES, FIG = base.RES, base.FIG
M = base.METRICS
G36, GLITE = "gemini-3.6-flash", "gemini-3.5-flash-lite"

LABEL = {"price": "За ціною (поточний)", "cosine": "Схожість ембедингів", "logreg": "Логістична регресія",
         "lgbm": "LightGBM LambdaRank", "mlp": "Нейромережа MLP", "two_tower": "Двобаштова нейромережа",
         GLITE: "Gemini 3.5 Flash-Lite", G36: "Gemini 3.6 Flash"}
ORDER = ["price", "cosine", "logreg", "lgbm", "mlp", "two_tower", GLITE, G36]


def read(path, prefixes):
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    return rows, {p: {m: np.array([float(r[f"{p}_{m}"]) for r in rows]) for m in M} for p in prefixes}


def main():
    rng = np.random.default_rng(base.SEED)
    rows0, a = read(os.path.join(RES, "test_per_wish.csv"), ["price", "cosine", "logreg", "lgbm"])
    rows1, b = read(os.path.join(RES, "nn_test_per_wish.csv"), ["mlp", "two_tower"])
    rows2, c = read(os.path.join(RES, "gemini_test_per_wish.csv"), [GLITE, G36])
    ids = [r["wish_id"] for r in rows0]
    assert ids == [r["wish_id"] for r in rows1] == [r["wish_id"] for r in rows2], "wish order differs"
    per = {**a, **b, **c}

    e5 = json.load(open(os.path.join(RES, "metrics.json"), encoding="utf-8"))["test"]
    # E5 baselines keep exactly the intervals already reported in section 5.8; the new methods get their own bootstrap
    summary = {k: {m: ({"mean": e5[k][m]["mean"], "ci": e5[k][m]["ci"]} if k in e5 else
                       {"mean": float(per[k][m].mean()), "ci": base.bootstrap_ci(per[k][m], rng)}) for m in M} for k in ORDER}

    def diff(x, y):
        d = per[x]["ndcg5"] - per[y]["ndcg5"]
        means = d[rng.integers(0, len(d), size=(base.N_BOOT, len(d)))].mean(axis=1)
        return {"mean": float(d.mean()), "ci": [float(np.percentile(means, 2.5)), float(np.percentile(means, 97.5))],
                "wins": int((d > 1e-9).sum()), "losses": int((d < -1e-9).sum()), "ties": int((abs(d) <= 1e-9).sum())}

    pairs = [("two_tower", "lgbm"), ("mlp", "lgbm"), ("two_tower", "mlp"), ("two_tower", G36), ("mlp", G36), ("lgbm", G36),
             ("two_tower", GLITE), ("lgbm", GLITE), (G36, "price")]
    diffs = {f"{x}-{y}": diff(x, y) for x, y in pairs}

    # by need / language (needs are in the E5 per-wish file)
    needs = [r["needs"].split() for r in rows0]
    langs = [r["lang"] for r in rows0]
    show = ["lgbm", "mlp", "two_tower", G36]
    by_need = {}
    for need in base.NEED_UK:
        idx = [i for i, ns in enumerate(needs) if need in ns]
        if idx:
            by_need[need] = {"n": len(idx), **{k: float(per[k]["ndcg5"][idx].mean()) for k in show}}
    by_lang = {lg: {"n": langs.count(lg), **{k: float(per[k]["ndcg5"][[i for i, x in enumerate(langs) if x == lg]].mean())
                                            for k in ORDER}} for lg in ("uk", "en")}

    gem = json.load(open(os.path.join(RES, "gemini_metrics.json"), encoding="utf-8"))
    nn = json.load(open(os.path.join(RES, "nn_metrics.json"), encoding="utf-8"))
    out = {"test_wishes": len(ids), "summary": summary, "diffs_ndcg5": diffs, "by_need": by_need, "by_lang": by_lang,
           "gemini": gem["models"], "nn": {k: {"params": v["params"], "val": v["val"],
                                              "single_seed_ndcg5": v["test_single_seed_ndcg5"]} for k, v in nn["models"].items()},
           "nn_selection": {k: {"config": v["config"], "epochs": v["epochs"], "select_val_ndcg5": v["select_val_ndcg5"]}
                            for k, v in nn["selection"].items()}}
    with open(os.path.join(RES, "comparison.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)

    charts(summary, by_need)
    for k in ORDER:
        s = summary[k]
        print(f"{k:24s} " + "  ".join(f"{m} {s[m]['mean']:.3f} [{s[m]['ci'][0]:.3f}; {s[m]['ci'][1]:.3f}]" for m in M))
    for k, d in diffs.items():
        print(f"diff {k}: {d['mean']:+.3f} [{d['ci'][0]:+.3f}; {d['ci'][1]:+.3f}] wins {d['wins']} losses {d['losses']} ties {d['ties']}")
    print("by lang", {k: {a: round(b, 3) for a, b in v.items()} for k, v in by_lang.items()})


def charts(summary, by_need):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import FancyBboxPatch
    surface, text, text2, grid = "#fcfcfb", "#0b0b0b", "#52514e", "#e4e3df"
    blue, orange, green, grey = "#2a78d6", "#eb6834", "#1baf7a", "#c9c7c1"
    plt.rcParams.update({
        "figure.facecolor": surface, "axes.facecolor": surface, "savefig.facecolor": surface, "axes.edgecolor": grid,
        "axes.labelcolor": text2, "text.color": text, "xtick.color": text2, "ytick.color": text2, "font.size": 13.5,
        "axes.spines.top": False, "axes.spines.right": False, "axes.spines.left": False, "axes.grid": True,
        "axes.grid.axis": "x", "grid.color": grid, "grid.linewidth": 0.8, "legend.frameon": False, "figure.dpi": 150,
    })

    def dc(x):
        return f"{x:.3f}".replace(".", ",")

    color = {"price": grey, "cosine": grey, "logreg": grey, "lgbm": "#9ec5f4", "mlp": green, "two_tower": green,
             GLITE: orange, G36: orange}
    fig, ax = plt.subplots(figsize=(7.4, 4.6))
    ys = np.arange(len(ORDER))
    vals = [summary[o]["ndcg5"]["mean"] for o in ORDER]
    lo = [summary[o]["ndcg5"]["mean"] - summary[o]["ndcg5"]["ci"][0] for o in ORDER]
    hi = [summary[o]["ndcg5"]["ci"][1] - summary[o]["ndcg5"]["mean"] for o in ORDER]
    ax.barh(ys, vals, height=0.62, color=[color[o] for o in ORDER], xerr=[lo, hi],
            error_kw={"ecolor": text2, "elinewidth": 1, "capsize": 2})
    for y, v, h in zip(ys, vals, hi):
        ax.text(v + h + 0.012, y, dc(v), va="center", fontsize=13, color=text)
    ax.set_yticks(ys)
    ax.set_yticklabels([LABEL[o] for o in ORDER])
    ax.invert_yaxis()
    ax.set_xlim(0, 1.12)
    ax.set_xlabel("NDCG@5 на тестовій вибірці, 95 % ДІ")
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "ch_e6_methods.png"))
    plt.close(fig)

    show = [("lgbm", "LightGBM", "#9ec5f4"), ("two_tower", "Двобаштова мережа", green), (G36, "Gemini 3.6 Flash", orange)]
    names = sorted(by_need, key=lambda n: by_need[n]["two_tower"])
    fig, ax = plt.subplots(figsize=(7.4, 5.6))
    ys = np.arange(len(names))
    for i, (k, lab, col) in enumerate(show):
        ax.barh(ys + (i - 1) * 0.27, [by_need[n][k] for n in names], height=0.25, color=col, label=lab)
    ax.set_yticks(ys)
    ax.set_yticklabels([f"{base.NEED_UK[n]} ({by_need[n]['n']})" for n in names])
    ax.set_xlim(0, 1.05)
    ax.set_xlabel("NDCG@5 (потреба в побажанні)")
    ax.legend(loc="lower center", bbox_to_anchor=(0.35, 1.0), ncol=3, fontsize=12, handlelength=1.2, columnspacing=1.0)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "ch_e6_needs.png"), bbox_inches="tight")
    plt.close(fig)

    # data split: three disjoint sets (proportional bar) and what each of them is used for
    from matplotlib.patches import Rectangle
    fig, ax = plt.subplots(figsize=(7.4, 4.6))
    ax.set_xlim(0, 760)
    ax.set_ylim(-0.6, 10)
    ax.axis("off")
    ax.text(0, 9.7, "760 побажань → 36 480 пар «побажання — авто»", fontsize=14, va="center", color=text, fontweight="bold")
    parts = [("Навчальна", 480, 0, blue, "навчання ваг мережі"),
             ("Валідаційна", 120, 480, green, "гіперпараметри, рання зупинка"),
             ("Тестова", 160, 600, orange, "один фінальний замір якості")]
    for name, n, x0, col, _ in parts:
        ax.add_patch(Rectangle((x0 + 2, 6.6), n - 4, 2.1, fc=col, ec="none"))
        ax.text(x0 + n / 2, 7.95, str(n), ha="center", va="center", color="white", fontsize=18, fontweight="bold")
        ax.text(x0 + n / 2, 7.1, name if n > 130 else name[:5] + ".", ha="center", va="center", color="white", fontsize=12.5)
    for i, (name, n, x0, col, use) in enumerate(parts):
        y = 5.1 - i * 1.3
        ax.add_patch(Rectangle((0, y - 0.35), 14, 0.7, fc=col, ec="none"))
        ax.text(28, y, f"{name} ({n}): {use}", va="center", fontsize=14, color=text)
    ax.text(0, 0.55, "Фінальну модель перенавчено на навчальній +\nвалідаційній (600) і оцінено на тестовій; у тестовій\n"
                     "немає формулювань і шаблонів перших двох.",
            va="center", fontsize=13, color=text2, linespacing=1.35)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "ch_e6_split.png"))
    plt.close(fig)


if __name__ == "__main__":
    main()
