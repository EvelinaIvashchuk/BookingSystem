#!/usr/bin/env python3
"""E6: practical cost of the neural rankers - training time and inference latency (CPU, one wish = 48 cars).

Usage (repository root; after train_nn.py):
    tools/.venv/bin/python tools/ranking/bench_nn.py
Output: results/ranking/nn_timing.json
"""
import json
import os
import sys
import time

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(__file__))
import train as base  # noqa: E402
import train_nn as nn_  # noqa: E402


def main():
    cars, car_x, _, _, car_emb, wishes, _ = base.load()
    price = [c["price"] for c in cars]
    split = {s: [w for w in wishes if w["split"] == s] for s in ("train", "val", "test")}
    sel = json.load(open(os.path.join(base.RES, "nn_metrics.json"), encoding="utf-8"))["selection"]
    fit = split["train"] + split["val"]
    out = {}
    for kind in ("mlp", "two_tower"):
        cfg = dict(sel[kind]["config"])
        if "hidden" in cfg and isinstance(cfg["hidden"], list):
            cfg["hidden"] = tuple(cfg["hidden"])
        ep = sel[kind]["epochs"]
        data = nn_.Data(fit, car_x, car_emb, cfg["k_pca"])
        t = time.perf_counter()
        scorer, _, _ = nn_.train_one(kind, cfg, data, fit, price, 0, epochs=ep)
        train_s = time.perf_counter() - t
        one = split["test"][:1]
        for _ in range(20):
            scorer(one)
        times = []
        for w in split["test"][:100]:
            t = time.perf_counter()
            scorer([w])
            times.append((time.perf_counter() - t) * 1000)
        out[kind] = {"epochs": ep, "train_seconds_one_network": round(train_s, 1),
                     "inference_ms_per_wish_mean": round(float(np.mean(times)), 2),
                     "inference_ms_per_wish_p95": round(float(np.percentile(times, 95)), 2)}
        print(kind, out[kind])
    out["device"] = f"CPU, torch {torch.__version__}, {torch.get_num_threads()} threads"
    json.dump(out, open(os.path.join(base.RES, "nn_timing.json"), "w", encoding="utf-8"), indent=1)


if __name__ == "__main__":
    main()
