#!/usr/bin/env python3
"""E5: trains and evaluates the wish-based car ranking model (learning to rank), exports it for the web application.

Usage (repository root; needs `ranking-data` output and datasets/car_wishes.json):
    tools/.venv/bin/python tools/ranking/train.py

Protocol
  * One query group = one wish; candidates = the 48 rentable cars; graded labels 0-3 from rubric.py.
  * Splits fixed in car_wishes.json: train 480 / val 120 (same phrasings) / test 160 (held-out phrasings & templates).
  * Model selection: grouped 5-fold cross-validation on TRAIN only (PCA is refitted inside every fold).
  * Validation: model fitted on train with the selected hyper-parameters, evaluated on VAL.
  * Test: model refitted on train+val with the same hyper-parameters, evaluated ONCE on TEST; this model is exported.
  * Metrics per wish: NDCG@5, P@5 (label >= 2), Hit@1, MRR (first label >= 2); 95 % CIs and paired differences by
    bootstrap over wishes (10 000 resamples).

Methods: price (current behaviour: cheapest first), random, cosine (embedding similarity wish↔car text),
BM25 over car texts (own implementation), logistic regression (pointwise), LightGBM LambdaRank without and with the
wish PCA components (the model used on the site).

Outputs: BookingSystem/CarRanking/car_ranker.json (model), BookingSystem.Tests/TestData/car_ranker_parity.json,
BookingSystem.Evaluation/results/ranking/{metrics.json, cv_grid.csv, test_per_wish.csv}, report/figures/ch_e5_*.png.
"""
import base64
import csv
import itertools
import json
import math
import os
import re
import sys
import time
import warnings
import zlib
from collections import Counter, defaultdict

import lightgbm as lgb
import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import GroupKFold

# numpy 2.0 with Apple Accelerate prints spurious "overflow/divide by zero encountered in matmul" warnings (also inside
# scikit-learn); results are finite and were checked against einsum.
warnings.filterwarnings("ignore", message=".*encountered in matmul", category=RuntimeWarning)

sys.path.insert(0, os.path.dirname(__file__))
from rubric import car_attrs, label, relevance  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "BookingSystem.Evaluation", "results", "ranking")
WISHES = os.path.join(ROOT, "BookingSystem.Evaluation", "datasets", "car_wishes.json")
MODEL_OUT = os.path.join(ROOT, "BookingSystem", "CarRanking", "car_ranker.json")
PARITY_OUT = os.path.join(ROOT, "BookingSystem.Tests", "TestData", "car_ranker_parity.json")
FIG = os.path.join(ROOT, "report", "figures")
SEED = 42
K = 5
N_BOOT = 10000


# ---------------------------------------------------------------- data
def load():
    rows = list(csv.DictReader(open(os.path.join(RES, "cars.csv"), encoding="utf-8")))
    car_feature_names = [c[2:] for c in rows[0] if c.startswith("f_")]
    car_x = np.array([[float(r["f_" + n]) if r["f_" + n] != "" else np.nan for n in car_feature_names] for r in rows])
    cars = [car_attrs(r) for r in rows]
    car_texts = [r["text"] for r in rows]

    emb, model_id = {}, None
    for line in open(os.path.join(RES, "cache", "embeddings.jsonl"), encoding="utf-8"):
        d = json.loads(line)
        emb[d["text"].strip()] = np.frombuffer(base64.b64decode(d["vector"]), dtype=np.float32)
        model_id = d["model"]

    wishes = json.load(open(WISHES, encoding="utf-8"))["wishes"]
    for w in wishes:
        w["labels"] = np.array([label(relevance(w["needs"], c)) for c in cars])
        w["emb"] = emb[w["text"].strip()]
    car_emb = np.stack([emb[t.strip()] for t in car_texts])
    return cars, car_x, car_feature_names, car_texts, car_emb, wishes, model_id


def cosines(w_emb, car_emb):
    a = w_emb.astype(np.float64)
    b = car_emb.astype(np.float64)
    return np.einsum("ij,j->i", b, a) / (np.linalg.norm(b, axis=1) * np.linalg.norm(a))


class Pca:
    """PCA fitted on wish embeddings; mean and components rounded to float32 so that the C# runtime gets the same values."""

    def __init__(self, k):
        self.k = k

    def fit(self, X):
        X = X.astype(np.float64)
        self.mean = X.mean(axis=0).astype(np.float32).astype(np.float64)
        _, s, vt = np.linalg.svd(X - self.mean, full_matrices=False)
        self.components = vt[: self.k].astype(np.float32).astype(np.float64)
        self.explained = float((s[: self.k] ** 2).sum() / (s ** 2).sum())
        return self

    def transform(self, X):
        # einsum instead of @: numpy 2.0 with Apple Accelerate prints spurious overflow warnings for this matmul
        return np.einsum("ij,kj->ik", X.astype(np.float64) - self.mean, self.components)


def build(wishes, car_x, car_emb, pca, use_pca=True):
    """Feature matrix (wish × car rows), labels and group sizes."""
    blocks, labels, groups = [], [], []
    for w in wishes:
        cos = cosines(w["emb"], car_emb)[:, None]
        parts = [cos]
        if use_pca:
            pcs = pca.transform(w["emb"][None, :])
            parts.append(np.repeat(pcs, len(car_x), axis=0))
        parts.append(car_x)
        blocks.append(np.hstack(parts))
        labels.append(w["labels"])
        groups.append(len(car_x))
    return np.vstack(blocks), np.concatenate(labels), groups


def feature_names(k, car_feature_names, use_pca=True):
    return ["cos_wish_car"] + ([f"wish_pc{i}" for i in range(k)] if use_pca else []) + list(car_feature_names)


# ---------------------------------------------------------------- metrics
def dcg(labels, k=5):
    gains = (2.0 ** np.asarray(labels[:k], dtype=float) - 1)
    return float((gains / np.log2(np.arange(2, 2 + len(gains)))).sum())


def wish_metrics(labels, scores, price):
    # ties broken by price (cheaper first), then by index — exactly as on the site
    order = sorted(range(len(scores)), key=lambda i: (-scores[i], price[i], i))
    ranked = labels[order]
    ideal = dcg(np.sort(labels)[::-1])
    rel = ranked >= 2
    first = int(np.argmax(rel)) + 1 if rel.any() else None
    return {
        "ndcg5": dcg(ranked) / ideal if ideal > 0 else 0.0,
        "p5": float(rel[:5].mean()),
        "hit1": float(rel[0]),
        "mrr": 1.0 / first if first else 0.0,
    }


METRICS = ["ndcg5", "p5", "hit1", "mrr"]


def evaluate(wishes, score_fn, price):
    return [wish_metrics(w["labels"], score_fn(w), price) for w in wishes]


def mean(per_wish, m):
    return float(np.mean([x[m] for x in per_wish]))


def bootstrap_ci(values, rng, n=N_BOOT):
    values = np.asarray(values)
    idx = rng.integers(0, len(values), size=(n, len(values)))
    means = values[idx].mean(axis=1)
    return [float(np.percentile(means, 2.5)), float(np.percentile(means, 97.5))]


# ---------------------------------------------------------------- BM25 (own implementation)
def tokens(text):
    return [t[:5] for t in re.findall(r"[a-zа-яіїєґ0-9']+", text.lower()) if len(t) > 1]


class Bm25:
    def __init__(self, docs, k1=1.2, b=0.75):
        self.docs = [Counter(tokens(d)) for d in docs]
        self.len = np.array([sum(d.values()) for d in self.docs], dtype=float)
        self.avg = self.len.mean()
        df = Counter(t for d in self.docs for t in d)
        n = len(docs)
        self.idf = {t: math.log(1 + (n - f + 0.5) / (f + 0.5)) for t, f in df.items()}
        self.k1, self.b = k1, b

    def scores(self, query):
        q = tokens(query)
        out = np.zeros(len(self.docs))
        for i, d in enumerate(self.docs):
            for t in q:
                tf = d.get(t, 0)
                if tf:
                    out[i] += self.idf[t] * tf * (self.k1 + 1) / (tf + self.k1 * (1 - self.b + self.b * self.len[i] / self.avg))
        return out


# ---------------------------------------------------------------- models
def lgbm_params(p):
    return dict(objective="lambdarank", n_estimators=p["n_estimators"], learning_rate=0.05, num_leaves=p["num_leaves"],
                min_child_samples=p["min_child_samples"], colsample_bytree=0.8, subsample=0.8, subsample_freq=1,
                lambdarank_truncation_level=10, random_state=SEED, verbose=-1, n_jobs=4,
                deterministic=True, force_row_wise=True)


def fit_lgbm(train, car_x, car_emb, p, use_pca=True):
    pca = Pca(p["pca_k"]).fit(np.stack([w["emb"] for w in train])) if use_pca else None
    X, y, g = build(train, car_x, car_emb, pca, use_pca)
    model = lgb.LGBMRanker(**lgbm_params(p))
    model.fit(X, y, group=g)
    return model, pca


def lgbm_scorer(model, pca, car_x, car_emb, use_pca=True):
    def score(w):
        X, _, _ = build([w], car_x, car_emb, pca, use_pca)
        return model.predict(X)
    return score


def fit_logreg(train, car_x, car_emb, p):
    pca = Pca(p["pca_k"]).fit(np.stack([w["emb"] for w in train]))
    X, y, _ = build(train, car_x, car_emb, pca)
    med = np.nanmedian(X, axis=0)
    X = np.where(np.isnan(X), med, X)
    mu, sd = X.mean(axis=0), X.std(axis=0) + 1e-9
    clf = LogisticRegression(C=p["C"], max_iter=3000)
    clf.fit((X - mu) / sd, (y >= 2).astype(int))

    def score(w):
        Xw, _, _ = build([w], car_x, car_emb, pca)
        Xw = np.where(np.isnan(Xw), med, Xw)
        return clf.decision_function((Xw - mu) / sd)
    return score


def cv(train, car_x, car_emb, price, grid, fit_score):
    """Grouped K-fold on train wishes; returns mean NDCG@5 per parameter set."""
    folds = list(GroupKFold(n_splits=K).split(np.zeros(len(train)), groups=np.arange(len(train))))
    results = []
    for p in grid:
        vals = []
        for tr, te in folds:
            score = fit_score([train[i] for i in tr], p)
            vals.append(mean(evaluate([train[i] for i in te], score, price), "ndcg5"))
        results.append((p, float(np.mean(vals)), float(np.std(vals))))
        print(f"  cv {p}: ndcg@5 {np.mean(vals):.4f} ± {np.std(vals):.4f}", flush=True)
    return results


# ---------------------------------------------------------------- export
def export_model(model, pca, names, model_id, meta):
    dump = model.booster_.dump_model()
    trees = []
    for t in dump["tree_info"]:
        nodes = []

        def walk(n):
            i = len(nodes)
            nodes.append(None)
            if "leaf_value" in n:
                nodes[i] = {"leaf": True, "feature": 0, "threshold": 0, "defaultLeft": False, "missing": None,
                            "left": 0, "right": 0, "value": n["leaf_value"]}
                return i
            assert n["decision_type"] == "<=", n["decision_type"]
            left = walk(n["left_child"])
            right = walk(n["right_child"])
            nodes[i] = {"leaf": False, "feature": n["split_feature"], "threshold": n["threshold"],
                        "defaultLeft": n["default_left"], "missing": n["missing_type"].lower(),
                        "left": left, "right": right, "value": 0}
            return i

        walk(t["tree_structure"])
        trees.append({"nodes": nodes})
    doc = {"format": "car-ranker/1", "embeddingModel": model_id, "featureNames": names,
           "pca": {"mean": [float(v) for v in pca.mean.astype(np.float32)],
                   "components": [[float(v) for v in row.astype(np.float32)] for row in pca.components]},
           "trees": trees, "training": meta}
    os.makedirs(os.path.dirname(MODEL_OUT), exist_ok=True)
    with open(MODEL_OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, separators=(",", ":"))
    return len(trees)


def export_parity(model, pca, X, rng):
    pick = rng.choice(len(X), size=300, replace=False)
    rows = X[pick].copy()
    rows[rng.random(rows.shape) < 0.05] = np.nan          # also exercise the missing-value branches
    expected = model.predict(rows)
    probe = np.sin(np.arange(pca.mean.shape[0]) * 0.37).astype(np.float32)
    doc = {"vectors": [[None if np.isnan(v) else float(v) for v in r] for r in rows],
           "scores": [float(s) for s in expected],
           "pcaProbe": {"embedding": "sin(0.37*i), float32", "projection": [float(v) for v in pca.transform(probe[None, :])[0]]}}
    os.makedirs(os.path.dirname(PARITY_OUT), exist_ok=True)
    with open(PARITY_OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, separators=(",", ":"))


# ---------------------------------------------------------------- charts
def charts(summary, by_need, importance):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    surface, text, text2, grid = "#fcfcfb", "#0b0b0b", "#52514e", "#e4e3df"
    series = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6"]
    plt.rcParams.update({
        "figure.facecolor": surface, "axes.facecolor": surface, "savefig.facecolor": surface, "axes.edgecolor": grid,
        "axes.labelcolor": text2, "text.color": text, "xtick.color": text2, "ytick.color": text2, "font.size": 10,
        "axes.spines.top": False, "axes.spines.right": False, "axes.spines.left": False, "axes.grid": True,
        "axes.grid.axis": "x", "grid.color": grid, "grid.linewidth": 0.8, "legend.frameon": False, "figure.dpi": 150,
    })
    labels = {"price": "За ціною (поточний)", "random": "Випадковий", "bm25": "BM25 за описом", "cosine": "Схожість ембедингів",
              "logreg": "Логістична регресія", "lgbm_nopca": "LightGBM без ознак побажання", "lgbm": "LightGBM LambdaRank"}
    order = ["random", "price", "bm25", "cosine", "logreg", "lgbm_nopca", "lgbm"]

    def dc(x):
        return f"{x:.3f}".replace(".", ",")

    fig, axes = plt.subplots(1, 2, figsize=(9.6, 3.6), sharey=True)
    for ax, m, title in zip(axes, ["ndcg5", "p5"], ["NDCG@5", "Precision@5 (мітка ≥ 2)"]):
        ys = np.arange(len(order))
        vals = [summary[o][m]["mean"] for o in order]
        lo = [summary[o][m]["mean"] - summary[o][m]["ci"][0] for o in order]
        hi = [summary[o][m]["ci"][1] - summary[o][m]["mean"] for o in order]
        colors = [series[0] if o == "lgbm" else "#9ec5f4" if o.startswith("lgbm") or o == "logreg" else "#c9c7c1" for o in order]
        ax.barh(ys, vals, height=0.6, color=colors, xerr=[lo, hi], error_kw={"ecolor": text2, "elinewidth": 1, "capsize": 2})
        for y, v, h in zip(ys, vals, hi):
            ax.text(v + h + 0.01, y, dc(v), va="center", fontsize=8.5, color=text)
        ax.set_xlim(0, 1.12)
        ax.set_title(title, fontsize=10, loc="left")
        ax.set_yticks(ys)
        ax.set_yticklabels([labels[o] for o in order])
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "ch_e5_methods.png"))
    plt.close(fig)

    needs = sorted(by_need, key=lambda n: by_need[n]["lgbm"])
    fig, ax = plt.subplots(figsize=(9.6, 4.6))
    ys = np.arange(len(needs))
    for i, (o, c) in enumerate([("price", "#c9c7c1"), ("cosine", series[1]), ("lgbm", series[0])]):
        ax.barh(ys + (i - 1) * 0.26, [by_need[n][o] for n in needs], height=0.24, color=c, label=labels[o])
    ax.set_yticks(ys)
    ax.set_yticklabels([f"{NEED_UK[n]} ({by_need[n]['n']})" for n in needs])
    ax.set_xlim(0, 1.05)
    ax.set_xlabel("NDCG@5 на тестових побажаннях, що містять потребу")
    ax.legend(loc="lower right", fontsize=8.5)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "ch_e5_needs.png"))
    plt.close(fig)

    top = importance[:15][::-1]
    fig, ax = plt.subplots(figsize=(7.6, 4.2))
    ax.barh(np.arange(len(top)), [v for _, v in top], height=0.6, color=series[0])
    ax.set_yticks(np.arange(len(top)))
    ax.set_yticklabels([FEATURE_UK.get(n, n) for n, _ in top])
    ax.set_xlabel("Частка сумарного приросту (gain), %")
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "ch_e5_importance.png"))
    plt.close(fig)


NEED_UK = {"luggage": "багаж", "people": "кількість людей", "kids": "діти", "economy": "економність", "cheap": "дешевше",
           "budget": "бюджет на добу", "city": "місто, паркування", "highway": "траса, комфорт", "offroad": "гори, бездоріжжя",
           "premium": "статус, преміум", "sport": "драйв, спорт", "eco": "електро, екологія", "cargo": "вантаж, переїзд",
           "automatic": "лише автомат", "pets": "з собакою"}
FEATURE_UK = {"cos_wish_car": "схожість побажання й опису", "log_price": "ціна (log)", "seats": "місць", "trunk_l": "багажник, л",
              "consumption": "витрата, л/100 км", "ev_range_km": "запас ходу EV", "is_electric": "електро", "is_hybrid": "гібрид",
              "is_diesel": "дизель", "awd": "повний привід", "length_mm": "довжина", "power_hp": "потужність", "automatic": "автомат",
              "cat_sedan": "седан", "cat_suv": "SUV", "cat_hatchback": "хетчбек", "cat_minivan": "мінівен", "cat_coupe": "купе",
              "premium": "преміум-бренд", "year": "рік"}
for _i in range(64):
    FEATURE_UK[f"wish_pc{_i}"] = f"побажання: компонента {_i + 1}"


# ---------------------------------------------------------------- main
def main():
    t0 = time.time()
    rng = np.random.default_rng(SEED)
    cars, car_x, car_names, car_texts, car_emb, wishes, model_id = load()
    price = [c["price"] for c in cars]
    split = {s: [w for w in wishes if w["split"] == s] for s in ("train", "val", "test")}
    print({s: len(v) for s, v in split.items()}, "cars", len(cars), "embedding", model_id)

    label_dist = Counter(int(l) for w in wishes for l in w["labels"])
    rel_per_wish = [int((w["labels"] >= 2).sum()) for w in wishes]

    # ---- model selection on train only
    grid = [dict(pca_k=k, num_leaves=nl, min_child_samples=mc, n_estimators=ne)
            for k, nl, mc, ne in itertools.product([8, 16, 32], [15, 31], [20, 50], [200, 500])]
    print("LightGBM grid:", len(grid))
    cv_lgbm = cv(split["train"], car_x, car_emb, price, grid,
                 lambda tr, p: lgbm_scorer(*fit_lgbm(tr, car_x, car_emb, p), car_x, car_emb))
    best = max(cv_lgbm, key=lambda r: r[1])[0]

    grid_nopca = [dict(pca_k=0, num_leaves=nl, min_child_samples=mc, n_estimators=ne)
                  for nl, mc, ne in itertools.product([15, 31], [20, 50], [200, 500])]
    print("LightGBM without wish components:")
    cv_nopca = cv(split["train"], car_x, car_emb, price, grid_nopca,
                  lambda tr, p: lgbm_scorer(*fit_lgbm(tr, car_x, car_emb, p, use_pca=False), car_x, car_emb, use_pca=False))
    best_nopca = max(cv_nopca, key=lambda r: r[1])[0]

    grid_lr = [dict(pca_k=k, C=c) for k in [8, 16, 32] for c in [0.1, 1.0, 10.0]]
    print("Logistic regression:")
    cv_lr = cv(split["train"], car_x, car_emb, price, grid_lr, lambda tr, p: fit_logreg(tr, car_x, car_emb, p))
    best_lr = max(cv_lr, key=lambda r: r[1])[0]

    with open(os.path.join(RES, "cv_grid.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["method", "params", "cv_ndcg5_mean", "cv_ndcg5_std"])
        for name, res in (("lgbm", cv_lgbm), ("lgbm_nopca", cv_nopca), ("logreg", cv_lr)):
            for p, m, s in res:
                wr.writerow([name, json.dumps(p), f"{m:.4f}", f"{s:.4f}"])

    bm25 = Bm25(car_texts)

    def static_methods():
        return {
            "price": lambda w: -np.asarray(price),
            "random": lambda w: np.random.default_rng(zlib.crc32(w["id"].encode())).random(len(price)),
            "cosine": lambda w: cosines(w["emb"], car_emb),
            "bm25": lambda w: bm25.scores(w["text"]),
        }

    def learned(train):
        m, pca = fit_lgbm(train, car_x, car_emb, best)
        m0, _ = fit_lgbm(train, car_x, car_emb, best_nopca, use_pca=False)
        return {
            "logreg": fit_logreg(train, car_x, car_emb, best_lr),
            "lgbm_nopca": lgbm_scorer(m0, None, car_x, car_emb, use_pca=False),
            "lgbm": lgbm_scorer(m, pca, car_x, car_emb),
        }, m, pca

    # ---- validation: fitted on train
    val_methods, _, _ = learned(split["train"])
    val_methods.update(static_methods())
    val = {name: {m: mean(evaluate(split["val"], fn, price), m) for m in METRICS} for name, fn in val_methods.items()}

    # ---- test: refitted on train + val, evaluated once
    test_methods, final_model, final_pca = learned(split["train"] + split["val"])
    test_methods.update(static_methods())
    per_wish = {name: evaluate(split["test"], fn, price) for name, fn in test_methods.items()}

    summary = {}
    for name, rows in per_wish.items():
        summary[name] = {m: {"mean": mean(rows, m), "ci": bootstrap_ci([r[m] for r in rows], rng)} for m in METRICS}
    diffs = {}
    for a, b in [("lgbm", "price"), ("lgbm", "cosine"), ("lgbm", "bm25"), ("lgbm", "logreg"), ("lgbm", "lgbm_nopca"), ("cosine", "price")]:
        d = np.array([x["ndcg5"] - y["ndcg5"] for x, y in zip(per_wish[a], per_wish[b])])
        idx = rng.integers(0, len(d), size=(N_BOOT, len(d)))
        means = d[idx].mean(axis=1)
        diffs[f"{a}-{b}"] = {"mean": float(d.mean()), "ci": [float(np.percentile(means, 2.5)), float(np.percentile(means, 97.5))],
                             "p_le_0": float((means <= 0).mean()), "wins": int((d > 1e-9).sum()), "losses": int((d < -1e-9).sum())}

    by_need = {}
    for need in NEED_UK:
        idx = [i for i, w in enumerate(split["test"]) if any(n["need"] == need for n in w["needs"])]
        if idx:
            by_need[need] = {"n": len(idx), **{o: float(np.mean([per_wish[o][i]["ndcg5"] for i in idx])) for o in ("price", "cosine", "bm25", "lgbm")}}
    by_lang = {}
    for lang in ("uk", "en"):
        idx = [i for i, w in enumerate(split["test"]) if w["lang"] == lang]
        by_lang[lang] = {"n": len(idx), **{o: float(np.mean([per_wish[o][i]["ndcg5"] for i in idx])) for o in ("price", "cosine", "lgbm")}}

    names = feature_names(best["pca_k"], car_names)
    gain = final_model.booster_.feature_importance(importance_type="gain")
    importance = sorted(((n, float(100 * g / gain.sum())) for n, g in zip(names, gain)), key=lambda x: -x[1])

    # ---- export
    meta = {"trainedOn": "train+val", "wishes": len(split["train"]) + len(split["val"]), "cars": len(cars),
            "params": best, "pcaExplained": final_pca.explained, "testNdcg5": summary["lgbm"]["ndcg5"]["mean"]}
    n_trees = export_model(final_model, final_pca, names, model_id, meta)
    X_test, _, _ = build(split["test"], car_x, car_emb, final_pca)
    export_parity(final_model, final_pca, X_test, rng)

    with open(os.path.join(RES, "test_per_wish.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["wish_id", "lang", "needs", "text", "relevant_cars"] + [f"{o}_{m}" for o in per_wish for m in METRICS])
        for i, w in enumerate(split["test"]):
            wr.writerow([w["id"], w["lang"], " ".join(n["need"] for n in w["needs"]), w["text"], int((w["labels"] >= 2).sum())]
                        + [f"{per_wish[o][i][m]:.4f}" for o in per_wish for m in METRICS])

    metrics = {
        "data": {"wishes": {s: len(v) for s, v in split.items()}, "cars": len(cars), "pairs": len(wishes) * len(cars),
                 "label_distribution": {str(k): v for k, v in sorted(label_dist.items())},
                 "relevant_per_wish": {"mean": float(np.mean(rel_per_wish)), "min": int(min(rel_per_wish)), "max": int(max(rel_per_wish))},
                 "english": sum(1 for w in wishes if w["lang"] == "en"), "embedding": model_id},
        "selected": {"lgbm": best, "lgbm_nopca": best_nopca, "logreg": best_lr},
        "cv_best": {"lgbm": max(r[1] for r in cv_lgbm), "lgbm_nopca": max(r[1] for r in cv_nopca), "logreg": max(r[1] for r in cv_lr)},
        "val": val, "test": summary, "test_diffs_ndcg5": diffs, "test_by_need": by_need, "test_by_lang": by_lang,
        "importance": importance[:20], "trees": n_trees, "pca_explained": final_pca.explained, "seconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(RES, "metrics.json"), "w", encoding="utf-8") as f:
        json.dump(metrics, f, ensure_ascii=False, indent=1)

    charts(summary, by_need, importance)
    print("\nselected", best, "| nopca", best_nopca, "| logreg", best_lr)
    print("VAL  ndcg@5:", {k: round(v["ndcg5"], 3) for k, v in val.items()})
    for name in ["random", "price", "bm25", "cosine", "logreg", "lgbm_nopca", "lgbm"]:
        s = summary[name]
        print(f"TEST {name:11s} " + "  ".join(f"{m} {s[m]['mean']:.3f} [{s[m]['ci'][0]:.3f}; {s[m]['ci'][1]:.3f}]" for m in METRICS))
    for k, d in diffs.items():
        print(f"diff {k}: {d['mean']:+.3f} [{d['ci'][0]:+.3f}; {d['ci'][1]:+.3f}] wins {d['wins']} losses {d['losses']}")
    print("by lang", by_lang)
    print("importance", [(n, round(v, 1)) for n, v in importance[:10]])
    print("exported", MODEL_OUT, "trees", n_trees, f"{time.time() - t0:.0f}s")


if __name__ == "__main__":
    main()
