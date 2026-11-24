#!/usr/bin/env python3
"""E6: exports the two neural rankers (the final ensembles of 5 networks trained on train+val) for the web application.

Usage (repository root; after train_nn.py):
    tools/.venv/bin/python tools/ranking/export_nn.py

The networks are retrained with exactly the seeds / epochs / hyper-parameters selected by train_nn.py (results/ranking/nn_metrics.json)
and the test metrics are compared with the stored ones. The weights are written as float16 (statistics as float32), concatenated into
one blob, zlib-compressed and base64-encoded into a JSON file, so that the C# side (BookingSystem.Infrastructure/Ai/Ranking) can evaluate
the networks without any ML runtime. The exported file is then read back by an independent numpy implementation (the reference for the
C# parity test) and the test metrics of the exported (float16) model are written to results/ranking/nn_export.json.

Outputs: BookingSystem/CarRanking/car_ranker_mlp.json, car_ranker_two_tower.json,
         BookingSystem.Tests/TestData/car_ranker_nn_parity.json, BookingSystem.Evaluation/results/ranking/nn_export.json
"""
import base64
import json
import os
import sys
import zlib

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(__file__))
import train as base  # noqa: E402
import train_nn as nn_  # noqa: E402

OUT_DIR = os.path.join(base.ROOT, "BookingSystem", "CarRanking")
PARITY = os.path.join(base.ROOT, "BookingSystem.Tests", "TestData", "car_ranker_nn_parity.json")
SUMMARY = os.path.join(base.RES, "nn_export.json")


# ---------------------------------------------------------------- blob container
class Blob:
    """Named tensors -> one byte blob (f16 or f32, little endian) + manifest."""

    def __init__(self):
        self.parts, self.manifest, self.offset = [], [], 0

    def add(self, name, arr, dtype):
        a = np.ascontiguousarray(np.asarray(arr), dtype="<f2" if dtype == "f16" else "<f4")
        raw = a.tobytes()
        self.manifest.append({"name": name, "dtype": dtype, "shape": list(a.shape), "offset": self.offset, "length": len(raw)})
        self.parts.append(raw)
        self.offset += len(raw)

    def encode(self):
        packed = zlib.compress(b"".join(self.parts), 9)
        return base64.b64encode(packed).decode("ascii"), self.offset, len(packed)


def decode(doc):
    """Independent reader of the file format: returns {name: float64 array} (the reference for the C# parity test)."""
    raw = zlib.decompress(base64.b64decode(doc["data"]))
    out = {}
    for t in doc["tensors"]:
        dt = "<f2" if t["dtype"] == "f16" else "<f4"
        out[t["name"]] = np.frombuffer(raw[t["offset"]:t["offset"] + t["length"]], dtype=dt).astype(np.float64).reshape(t["shape"])
    return out


# ---------------------------------------------------------------- reference scorers from the exported tensors (float64)
def ref_mlp(doc, T, w_emb, cos, car_vec):
    cfg = doc["config"]
    pcs = (w_emb.astype(np.float64) - T["pca_mean"]) @ T["pca_components"].T
    car = np.where(np.isnan(car_vec), T["car_median"], car_vec)
    X = np.hstack([cos[:, None], np.tile(pcs, (len(car), 1)), car])
    X = (X - T["x_mu"]) / T["x_sd"]
    total = np.zeros(len(car))
    for n in range(cfg["networks"]):
        h = X
        for j in range(len(cfg["layers"]) - 1):
            h = h @ T[f"n{n}.l{j}.w"].T + T[f"n{n}.l{j}.b"]
            if j < len(cfg["layers"]) - 2:
                h = np.maximum(h, 0)
        total += h[:, 0]
    return total / cfg["networks"]


def ref_two_tower(doc, T, w_emb, cos, car_vec):
    cfg = doc["config"]
    w = (w_emb.astype(np.float64) - T["e_mu"]) / T["e_sd"]
    car = np.where(np.isnan(car_vec), T["car_median"], car_vec)
    ind = np.isnan(car_vec)[:, cfg["nanColumns"]].astype(np.float64)
    car = (np.hstack([car, ind]) - T["car_mu"]) / T["car_sd"]
    total = np.zeros(len(car))
    for n in range(cfg["networks"]):
        wv = np.maximum(w @ T[f"n{n}.wish1.w"].T + T[f"n{n}.wish1.b"], 0) @ T[f"n{n}.wish2.w"].T + T[f"n{n}.wish2.b"]
        cv = np.maximum(car @ T[f"n{n}.car1.w"].T + T[f"n{n}.car1.b"], 0) @ T[f"n{n}.car2.w"].T + T[f"n{n}.car2.b"]
        total += (cv @ wv) * cfg["dim"] ** -0.5
    return total / cfg["networks"]


def main():
    cars, car_x, _, _, car_emb, wishes, model_id = base.load()
    price = [c["price"] for c in cars]
    split = {s: [w for w in wishes if w["split"] == s] for s in ("train", "val", "test")}
    fit = split["train"] + split["val"]
    stored = json.load(open(os.path.join(base.RES, "nn_metrics.json"), encoding="utf-8"))
    os.makedirs(OUT_DIR, exist_ok=True)

    summary, parity_models = {}, {}
    rng = np.random.default_rng(7)
    probe = [split["test"][i] for i in sorted(rng.choice(len(split["test"]), 6, replace=False))]
    probe_in = [(w, base.cosines(w["emb"], car_emb), car_x) for w in probe]

    for kind in ("mlp", "two_tower"):
        sel = stored["selection"][kind]
        cfg = dict(sel["config"])
        if isinstance(cfg.get("hidden"), list):
            cfg["hidden"] = tuple(cfg["hidden"])
        data = nn_.Data(fit, car_x, car_emb, cfg["k_pca"])
        scorers = [nn_.train_one(kind, cfg, data, fit, price, s, epochs=sel["epochs"])[0] for s in stored["seeds_final"]]
        nets = [s.model.eval() for s in scorers]

        # 1. the retrained ensemble must reproduce the numbers of the report
        ens = np.mean([s(split["test"]) for s in scorers], axis=0)
        m32 = np.mean([base.wish_metrics(w["labels"], sc, price)["ndcg5"] for w, sc in zip(split["test"], ens)])
        print(f"{kind}: retrained ensemble test NDCG@5 {m32:.4f} (stored {stored['models'][kind]['test']['ndcg5']['mean']:.4f})")

        # 2. blob
        blob = Blob()
        median = np.nanmedian(car_x, axis=0)
        if kind == "mlp":
            blob.add("pca_mean", data.pca.mean, "f32")
            blob.add("pca_components", data.pca.components, "f32")
            blob.add("x_mu", data.x_mu, "f32")
            blob.add("x_sd", data.x_sd, "f32")
            blob.add("car_median", median, "f32")
            dims = None
            for n, net in enumerate(nets):
                lins = [m for m in net.net if isinstance(m, torch.nn.Linear)]
                dims = [lins[0].in_features] + [l.out_features for l in lins]
                for j, l in enumerate(lins):
                    blob.add(f"n{n}.l{j}.w", l.weight.detach().numpy(), "f16")
                    blob.add(f"n{n}.l{j}.b", l.bias.detach().numpy(), "f16")
            config = {"networks": len(nets), "layers": dims, "pcaDimensions": cfg["k_pca"], "carFeatures": car_x.shape[1]}
            params = sum(p.numel() for p in nets[0].parameters())
            ref = ref_mlp
        else:
            blob.add("e_mu", data.e_mu, "f32")
            blob.add("e_sd", data.e_sd, "f32")
            blob.add("car_median", median, "f32")
            blob.add("car_mu", data.cx_mu, "f32")
            blob.add("car_sd", data.cx_sd, "f32")
            for n, net in enumerate(nets):
                for name, layer in (("wish1", net.wish[1]), ("wish2", net.wish[4]), ("car1", net.car[0]), ("car2", net.car[2])):
                    blob.add(f"n{n}.{name}.w", layer.weight.detach().numpy(), "f16")
                    blob.add(f"n{n}.{name}.b", layer.bias.detach().numpy(), "f16")
            config = {"networks": len(nets), "hidden": cfg["hidden"], "dim": cfg["dim"], "nanColumns": list(map(int, data.nan_cols)),
                      "carFeatures": car_x.shape[1], "embeddingSize": int(car_emb.shape[1])}
            params = sum(p.numel() for p in nets[0].parameters())
            ref = ref_two_tower
        text, raw_len, packed_len = blob.encode()
        doc = {"format": f"car-ranker-{kind.replace('_', '-')}/1", "embeddingModel": model_id, "config": config,
               "training": {"trainedOn": "train+val", "wishes": len(fit), "cars": len(cars), "ensemble": len(nets), "epochs": sel["epochs"],
                            "params": params, "hyperparameters": {k: v for k, v in sel["config"].items()},
                            "testNdcg5": float(stored["models"][kind]["test"]["ndcg5"]["mean"])},
               "tensors": blob.manifest, "data": text}
        path = os.path.join(OUT_DIR, f"car_ranker_{kind}.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False, separators=(",", ":"))
        size = os.path.getsize(path)

        # 3. reference scorer reads the written file back
        doc2 = json.load(open(path, encoding="utf-8"))
        T = decode(doc2)
        ref_scores = [ref(doc2, T, w["emb"], cos, car_x) for w, cos, _ in probe_in]
        torch_scores = [np.mean([s([w])[0] for s in scorers], axis=0) for w in probe]
        gap_f16 = max(float(np.max(np.abs(a - b))) for a, b in zip(ref_scores, torch_scores))
        # exported (float16) model on the whole test set
        ens16 = [ref(doc2, T, w["emb"], base.cosines(w["emb"], car_emb), car_x) for w in split["test"]]
        res = {m: float(np.mean([base.wish_metrics(w["labels"], sc, price)[m] for w, sc in zip(split["test"], ens16)])) for m in base.METRICS}
        print(f"{kind}: exported float16 ensemble test {res}; max |ref - torch float32| on probe wishes {gap_f16:.2e}; "
              f"file {size / 1e6:.1f} MB (raw {raw_len / 1e6:.1f} MB, zlib {packed_len / 1e6:.1f} MB)")
        summary[kind] = {"file": os.path.relpath(path, base.ROOT), "file_mb": round(size / 1e6, 2), "raw_blob_mb": round(raw_len / 1e6, 2),
                         "params_per_network": params, "networks": len(nets), "test_float32": float(m32), "test_float16_export": res,
                         "max_abs_diff_vs_torch_float32_probe": gap_f16}
        parity_models[kind] = [s.tolist() for s in ref_scores]

    # 4. parity data for the C# test: inputs + reference scores
    pdoc = {"note": "Inputs: wish embedding (float32, base64), cosine(wish, car) per car, 19 car features per car (null = unknown). "
                    "Expected: per-model ensemble scores from tools/ranking/export_nn.py (numpy reference reading the exported files).",
            "wishes": [{"id": w["id"], "embedding": base64.b64encode(w["emb"].astype("<f4").tobytes()).decode("ascii"),
                        "cosines": cos.tolist()} for w, cos, _ in probe_in],
            "cars": [[None if np.isnan(v) else float(v) for v in row] for row in car_x],
            "scores": parity_models}
    json.dump(pdoc, open(PARITY, "w", encoding="utf-8"), separators=(",", ":"))
    json.dump(summary, open(SUMMARY, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("written", OUT_DIR, PARITY)


if __name__ == "__main__":
    main()
