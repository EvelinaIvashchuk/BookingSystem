#!/usr/bin/env python3
"""E6: two neural rankers for the wish -> car task, trained on the same split as the LightGBM model (E5).

Usage (repository root; needs the E5 data: results/ranking/cars.csv and cache/embeddings.jsonl):
    tools/.venv/bin/python tools/ranking/train_nn.py

Models (PyTorch, trained with a listwise softmax loss over the 48 candidate cars of one wish, i.e. ListNet):
  * mlp       - multilayer perceptron over the SAME feature vector as LightGBM (cosine(wish, car text), PCA components
                of the wish embedding, 19 car features); standardised with training statistics.
  * two_tower - two-tower network: the wish tower maps the full 3 072-d wish embedding to a d-dim vector, the car tower
                maps the car features to a d-dim vector, the score is their dot product. No hand-made PCA/cosine.

Protocol (identical to E5; the split is fixed in datasets/car_wishes.json: train 480 / val 120 / test 160):
  1. Hyper-parameters (grid below) and the number of epochs are chosen on VAL: every configuration is trained on TRAIN with
     early stopping on the NDCG@5 of VAL (3 seeds), the best mean VAL NDCG@5 wins.
  2. VAL result = ensemble of 5 seeds trained on TRAIN only (early stopping on VAL).
  3. TEST result = ensemble of 5 seeds REFITTED on TRAIN+VAL for the selected number of epochs, evaluated ONCE on TEST.
Outputs: results/ranking/nn_metrics.json, results/ranking/nn_test_per_wish.csv, results/ranking/nn_val_per_wish.csv
"""
import csv
import itertools
import json
import os
import sys
import time

import numpy as np
import torch
from torch import nn

sys.path.insert(0, os.path.dirname(__file__))
import train as base  # noqa: E402  (data loading, metrics, PCA are shared with E5)

RES = base.RES
SEEDS_SELECT = [0, 1, 2]
SEEDS_FINAL = [0, 1, 2, 3, 4]
MAX_EPOCHS = 120
PATIENCE = 15
BATCH = 16          # wishes per step (16 x 48 = 768 pairs)
torch.set_num_threads(4)


# ---------------------------------------------------------------- data tensors
class Data:
    """Wish-level tensors for one set of wishes. Standardisation / PCA are fitted on `fit_on` only."""

    def __init__(self, fit_on, car_x, car_emb, k_pca):
        self.car_x, self.car_emb, self.k = car_x, car_emb, k_pca
        # car features: median imputation + missing indicators, standardised over the 48 cars (shared by all splits)
        self.nan_cols = [j for j in range(car_x.shape[1]) if np.isnan(car_x[:, j]).any()]
        med = np.nanmedian(car_x, axis=0)
        cx = np.where(np.isnan(car_x), med, car_x)
        cx = np.hstack([cx] + [np.isnan(car_x[:, [j]]).astype(float) for j in self.nan_cols])
        self.cx_mu, self.cx_sd = cx.mean(0), cx.std(0) + 1e-9       # kept for export (tools/ranking/export_nn.py)
        self.car_feat = (cx - self.cx_mu) / self.cx_sd
        # wish embedding statistics from the fitting wishes only
        E = np.stack([w["emb"] for w in fit_on]).astype(np.float64)
        self.e_mu, self.e_sd = E.mean(0), E.std(0) + 1e-6
        self.pca = base.Pca(k_pca).fit(E) if k_pca else None
        # standardisation of the MLP input from the fitting wishes only
        X, _, _ = self._mlp_raw(fit_on)
        self.x_mu, self.x_sd = X.reshape(-1, X.shape[-1]).mean(0), X.reshape(-1, X.shape[-1]).std(0) + 1e-9

    def _mlp_raw(self, wishes):
        X, _, _ = base.build(wishes, np.where(np.isnan(self.car_x), np.nanmedian(self.car_x, axis=0), self.car_x),
                             self.car_emb, self.pca, use_pca=bool(self.k))
        n = len(wishes)
        return X.reshape(n, len(self.car_x), -1), None, None

    def mlp(self, wishes):
        X, _, _ = self._mlp_raw(wishes)
        return torch.tensor((X - self.x_mu) / self.x_sd, dtype=torch.float32)

    def wish_emb(self, wishes):
        E = np.stack([w["emb"] for w in wishes]).astype(np.float64)
        return torch.tensor((E - self.e_mu) / self.e_sd, dtype=torch.float32)

    @staticmethod
    def labels(wishes):
        return torch.tensor(np.stack([w["labels"] for w in wishes]), dtype=torch.float32)


# ---------------------------------------------------------------- networks
class Mlp(nn.Module):
    def __init__(self, d_in, hidden, dropout):
        super().__init__()
        layers, d = [], d_in
        for h in hidden:
            layers += [nn.Linear(d, h), nn.ReLU(), nn.Dropout(dropout)]
            d = h
        layers.append(nn.Linear(d, 1))
        self.net = nn.Sequential(*layers)

    def forward(self, x):                      # x: (n_wishes, n_cars, d_in) -> (n_wishes, n_cars)
        return self.net(x).squeeze(-1)


class TwoTower(nn.Module):
    def __init__(self, d_wish, d_car, hidden, dim, dropout):
        super().__init__()
        self.wish = nn.Sequential(nn.Dropout(dropout), nn.Linear(d_wish, hidden), nn.ReLU(), nn.Dropout(dropout),
                                  nn.Linear(hidden, dim))
        self.car = nn.Sequential(nn.Linear(d_car, hidden), nn.ReLU(), nn.Linear(hidden, dim))
        self.scale = dim ** -0.5

    def forward(self, w, car):                 # w: (n, d_wish), car: (n_cars, d_car) -> (n, n_cars)
        return (self.wish(w) @ self.car(car).T) * self.scale


def listnet_loss(scores, y):
    gains = 2.0 ** y - 1
    p = gains / gains.sum(1, keepdim=True).clamp_min(1e-9)
    return -(p * torch.log_softmax(scores, dim=1)).sum(1).mean()


# ---------------------------------------------------------------- training
def per_wish_metrics(wishes, scores, price):
    return [base.wish_metrics(w["labels"], s, price) for w, s in zip(wishes, scores)]


def ndcg(wishes, scores, price):
    return float(np.mean([m["ndcg5"] for m in per_wish_metrics(wishes, scores, price)]))


def train_one(kind, cfg, data, train, price, seed, epochs=None, val=None):
    """Trains one network. With `val` it early-stops on val NDCG@5 and returns (model scorer, best epoch, best val);
    with fixed `epochs` it trains exactly that many epochs."""
    torch.manual_seed(seed)
    rng = np.random.default_rng(seed)
    y = data.labels(train)
    if kind == "mlp":
        X = data.mlp(train)
        model = Mlp(X.shape[-1], cfg["hidden"], cfg["dropout"])
        fwd = lambda idx: model(X[idx])
        car_t = None
    else:
        W = data.wish_emb(train)
        car_t = torch.tensor(data.car_feat, dtype=torch.float32)
        model = TwoTower(W.shape[1], car_t.shape[1], cfg["hidden"], cfg["dim"], cfg["dropout"])
        fwd = lambda idx: model(W[idx], car_t)
    opt = torch.optim.AdamW(model.parameters(), lr=cfg["lr"], weight_decay=cfg["wd"])

    def scorer(wishes):
        model.eval()
        with torch.no_grad():
            if kind == "mlp":
                return model(data.mlp(wishes)).numpy()
            return model(data.wish_emb(wishes), car_t).numpy()

    best, best_ep, best_state, bad = -1.0, 0, None, 0
    total = epochs or MAX_EPOCHS
    for ep in range(1, total + 1):
        model.train()
        order = rng.permutation(len(train))
        for i in range(0, len(order), BATCH):
            idx = torch.tensor(order[i:i + BATCH])
            loss = listnet_loss(fwd(idx), y[idx])
            opt.zero_grad()
            loss.backward()
            opt.step()
        if val is not None and epochs is None:
            v = ndcg(val, scorer(val), price)
            if v > best + 1e-5:
                best, best_ep, bad = v, ep, 0
                best_state = {k: t.clone() for k, t in model.state_dict().items()}
            else:
                bad += 1
                if bad >= PATIENCE:
                    break
    if best_state is not None:
        model.load_state_dict(best_state)
    scorer.model = model                          # the trained network, for export
    return scorer, best_ep, best


def grid(kind):
    if kind == "mlp":
        return [dict(k_pca=k, hidden=h, dropout=d, wd=wd, lr=1e-3)
                for k, h, d, wd in itertools.product([16, 32], [(128, 64), (256, 128)], [0.1, 0.3], [1e-4, 1e-2])]
    return [dict(k_pca=0, hidden=h, dim=dim, dropout=d, wd=wd, lr=1e-3)
            for h, dim, d, wd in itertools.product([256, 512], [32, 64], [0.2, 0.5], [1e-3, 1e-1])]


def fit_ensemble(kind, cfg, fit_wishes, car_x, car_emb, price, seeds, epochs=None, val=None):
    data = Data(fit_wishes, car_x, car_emb, cfg["k_pca"])
    scorers, eps = [], []
    for s in seeds:
        sc, ep, _ = train_one(kind, cfg, data, fit_wishes, price, s, epochs=epochs, val=val)
        scorers.append(sc)
        eps.append(ep)
    return (lambda wishes: np.mean([sc(wishes) for sc in scorers], axis=0)), eps, scorers


def main():
    t0 = time.time()
    cars, car_x, car_names, car_texts, car_emb, wishes, model_id = base.load()
    price = [c["price"] for c in cars]
    split = {s: [w for w in wishes if w["split"] == s] for s in ("train", "val", "test")}
    print({s: len(v) for s, v in split.items()}, "cars", len(cars))
    rng = np.random.default_rng(base.SEED)

    result, per_val, per_test, selection = {}, {}, {}, {}
    for kind in ("mlp", "two_tower"):
        print(f"\n== {kind}: grid search on VAL", flush=True)
        rows = []
        for cfg in grid(kind):
            data = Data(split["train"], car_x, car_emb, cfg["k_pca"])
            vals, eps = [], []
            for s in SEEDS_SELECT:
                _, ep, v = train_one(kind, cfg, data, split["train"], price, s, val=split["val"])
                vals.append(v)
                eps.append(ep)
            rows.append((cfg, float(np.mean(vals)), int(round(np.mean(eps)))))
            print(f"  {json.dumps(cfg)}  val ndcg@5 {np.mean(vals):.4f}  epochs {eps}", flush=True)
        cfg, v_sel, ep_sel = max(rows, key=lambda r: r[1])
        selection[kind] = {"config": cfg, "select_val_ndcg5": v_sel, "epochs": ep_sel,
                           "grid_size": len(rows), "grid": [{"config": c, "val_ndcg5": v, "epochs": e} for c, v, e in rows]}
        print(f"  selected {cfg}  val {v_sel:.4f}  epochs {ep_sel}", flush=True)

        # VAL: train only, early stopping on val, 5 seeds
        scorer_v, eps_v, _ = fit_ensemble(kind, cfg, split["train"], car_x, car_emb, price, SEEDS_FINAL, val=split["val"])
        per_val[kind] = per_wish_metrics(split["val"], scorer_v(split["val"]), price)
        # TEST: refit on train+val for the selected number of epochs, 5 seeds, evaluated once
        scorer_t, _, scorers = fit_ensemble(kind, cfg, split["train"] + split["val"], car_x, car_emb, price,
                                            SEEDS_FINAL, epochs=ep_sel)
        data_tv = Data(split["train"] + split["val"], car_x, car_emb, cfg["k_pca"])
        per_test[kind] = per_wish_metrics(split["test"], scorer_t(split["test"]), price)
        # single-seed spread (what one network gives, without ensembling)
        seed_ndcg = []
        for s in SEEDS_FINAL:
            sc, _, _ = train_one(kind, cfg, data_tv, split["train"] + split["val"], price, s, epochs=ep_sel)
            seed_ndcg.append(ndcg(split["test"], sc(split["test"]), price))
        params = None
        if kind == "mlp":
            params = sum(p.numel() for p in Mlp(1 + cfg["k_pca"] + data_tv.car_x.shape[1], cfg["hidden"], 0).parameters())
        else:
            params = sum(p.numel() for p in TwoTower(3072, data_tv.car_feat.shape[1], cfg["hidden"], cfg["dim"], 0).parameters())
        result[kind] = {
            "params": params,
            "val": {m: float(np.mean([x[m] for x in per_val[kind]])) for m in base.METRICS},
            "test": {m: {"mean": float(np.mean([x[m] for x in per_test[kind]])),
                         "ci": base.bootstrap_ci([x[m] for x in per_test[kind]], rng)} for m in base.METRICS},
            "test_single_seed_ndcg5": {"mean": float(np.mean(seed_ndcg)), "sd": float(np.std(seed_ndcg)), "values": seed_ndcg},
            "epochs_val_runs": eps_v,
        }
        print(f"  VAL  {kind}: " + "  ".join(f"{m} {result[kind]['val'][m]:.3f}" for m in base.METRICS))
        print(f"  TEST {kind}: " + "  ".join(f"{m} {result[kind]['test'][m]['mean']:.3f}" for m in base.METRICS)
              + f"  | single seed ndcg5 {np.mean(seed_ndcg):.3f} ± {np.std(seed_ndcg):.3f}", flush=True)

    for name, per, ws in (("nn_test_per_wish.csv", per_test, split["test"]), ("nn_val_per_wish.csv", per_val, split["val"])):
        with open(os.path.join(RES, name), "w", newline="", encoding="utf-8") as f:
            wr = csv.writer(f)
            wr.writerow(["wish_id"] + [f"{k}_{m}" for k in per for m in base.METRICS])
            for i, w in enumerate(ws):
                wr.writerow([w["id"]] + [f"{per[k][i][m]:.4f}" for k in per for m in base.METRICS])
    with open(os.path.join(RES, "nn_metrics.json"), "w", encoding="utf-8") as f:
        json.dump({"models": result, "selection": selection, "seeds_select": SEEDS_SELECT, "seeds_final": SEEDS_FINAL,
                   "max_epochs": MAX_EPOCHS, "patience": PATIENCE, "batch": BATCH, "seconds": round(time.time() - t0, 1)},
                  f, ensure_ascii=False, indent=1)
    print(f"\ndone in {time.time() - t0:.0f}s")


if __name__ == "__main__":
    main()
