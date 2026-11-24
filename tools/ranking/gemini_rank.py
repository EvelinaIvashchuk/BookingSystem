#!/usr/bin/env python3
"""E6: Gemini as a zero-shot car ranker on the same test wishes (the "LLM baseline" for the trained models).

Usage (repository root):
    tools/.venv/bin/python tools/ranking/gemini_rank.py [--models gemini-3.6-flash,gemini-3.5-flash-lite] [--limit N] [--split test]

For every wish the model gets the wish and the descriptions of all 48 cars (the very text the embedding model sees: name,
body, seats, gearbox, fuel, boot, consumption, drive, length, power, price) and returns the 10 best cars, best first, as JSON.
Cars not named are ranked below the named ones, cheapest first (the same tie-break as for the other methods). The ranking is
scored with the rubric exactly like the trained models (NDCG@5, P@5, Hit@1, MRR). The API key is read from the .NET user
secrets of BookingSystem.Evaluation (never printed). Replies are cached in results/ranking/cache/gemini_rank_<model>.jsonl,
so re-running costs nothing.

Outputs: results/ranking/gemini_<split>_per_wish.csv, results/ranking/gemini_metrics.json
"""
import argparse
import csv
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import train as base  # noqa: E402

RES = base.RES
CACHE = os.path.join(RES, "cache")
URL = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"
PRICE = {"gemini-3.6-flash": (0.75, 3.75), "gemini-3.5-flash-lite": (0.30, 2.50)}   # USD per 1M tokens (appsettings.json)
TOP = 10


def api_key():
    out = subprocess.run(["dotnet", "user-secrets", "list", "--project", os.path.join(base.ROOT, "BookingSystem.Evaluation")],
                         capture_output=True, text=True, check=True).stdout
    m = re.search(r"Ai:Providers:gemini:ApiKey\s*=\s*(\S+)", out)
    if not m:
        sys.exit("Gemini API key not found in user secrets")
    return m.group(1)


def prompt(wish, cars_text):
    lines = "\n".join(f"{i + 1}. {t}" for i, t in enumerate(cars_text))
    return ("You are an assistant of a car-rental service. A customer described what they need. From the 48 cars below choose "
            f"the {TOP} that fit the customer's wish best and order them from best to worst. Consider every requirement in the "
            "wish (space, passengers, budget, fuel economy, transmission, terrain, comfort, etc.).\n\n"
            f"Customer wish: \"{wish}\"\n\nCars:\n{lines}\n\n"
            f"Reply with JSON only: {{\"ranking\": [car numbers, best first, exactly {TOP} distinct numbers from 1 to 48]}}")


def call(key, model, text):
    body = json.dumps({"model": model, "temperature": 0, "response_format": {"type": "json_object"},
                       "messages": [{"role": "user", "content": text}]}).encode()
    for attempt in range(6):
        req = urllib.request.Request(URL, body, {"Content-Type": "application/json", "Authorization": f"Bearer {key}"})
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504):
                time.sleep(2 ** attempt * 2)
                continue
            raise RuntimeError(f"HTTP {e.code}: {e.read().decode()[:300]}")
        except (urllib.error.URLError, TimeoutError):
            time.sleep(2 ** attempt * 2)
    raise RuntimeError("Gemini unavailable")


def parse(content):
    try:
        ids = json.loads(content)["ranking"]
    except Exception:
        ids = [int(x) for x in re.findall(r"\d+", content)]
    seen, out = set(), []
    for x in ids:
        x = int(x)
        if 1 <= x <= 48 and x not in seen:
            seen.add(x)
            out.append(x - 1)
    return out[:TOP]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--models", default="gemini-3.6-flash,gemini-3.5-flash-lite")
    ap.add_argument("--split", default="test")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--workers", type=int, default=6)
    args = ap.parse_args()

    cars, car_x, _, car_texts, car_emb, wishes, _ = base.load()
    price = [c["price"] for c in cars]
    ws = [w for w in wishes if w["split"] == args.split]
    if args.limit:
        ws = ws[: args.limit]
    key = api_key()
    os.makedirs(CACHE, exist_ok=True)

    per, summary = {}, {}
    for model in args.models.split(","):
        path = os.path.join(CACHE, f"gemini_rank_{model}.jsonl")
        cache = {}
        if os.path.exists(path):
            for line in open(path, encoding="utf-8"):
                d = json.loads(line)
                cache[d["wish"]] = d
        todo = [w for w in ws if w["id"] not in cache]
        print(f"{model}: {len(ws)} wishes, {len(todo)} to request", flush=True)

        def run(w):
            t = time.time()
            resp = call(key, model, prompt(w["text"], car_texts))
            u = resp.get("usage", {})
            return {"wish": w["id"], "content": resp["choices"][0]["message"]["content"],
                    "prompt_tokens": u.get("prompt_tokens", 0), "completion_tokens": u.get("completion_tokens", 0),
                    "total_tokens": u.get("total_tokens", 0), "seconds": round(time.time() - t, 2)}

        with ThreadPoolExecutor(args.workers) as ex, open(path, "a", encoding="utf-8") as f:
            for d in ex.map(run, todo):
                cache[d["wish"]] = d
                f.write(json.dumps(d, ensure_ascii=False) + "\n")
                f.flush()

        rows, bad = [], 0
        for w in ws:
            ranked = parse(cache[w["id"]]["content"])
            if len(ranked) < TOP:
                bad += 1
            scores = np.full(len(cars), -1000.0)
            for pos, ci in enumerate(ranked):
                scores[ci] = -pos
            rows.append(base.wish_metrics(w["labels"], scores, price))
        per[model] = rows
        used = [cache[w["id"]] for w in ws]
        pin, pout = sum(d["prompt_tokens"] for d in used), sum(d["completion_tokens"] for d in used)
        # total_tokens includes the thinking tokens that completion_tokens omits for Gemini 3 (see the report, section 4)
        think = sum(max(0, d["total_tokens"] - d["prompt_tokens"] - d["completion_tokens"]) for d in used)
        cin, cout = PRICE.get(model, (0, 0))
        summary[model] = {
            "wishes": len(ws), "incomplete_rankings": bad,
            "metrics": {m: float(np.mean([r[m] for r in rows])) for m in base.METRICS},
            "prompt_tokens": pin, "completion_tokens": pout, "thinking_tokens": think,
            "cost_usd": round((pin * cin + (pout + think) * cout) / 1e6, 4),
            "latency_s_mean": float(np.mean([d["seconds"] for d in used])),
            "latency_s_p95": float(np.percentile([d["seconds"] for d in used], 95)),
        }
        print(f"  {model}: " + "  ".join(f"{m} {summary[model]['metrics'][m]:.3f}" for m in base.METRICS)
              + f"  | incomplete {bad}  cost ${summary[model]['cost_usd']}", flush=True)

    if args.limit:
        return
    with open(os.path.join(RES, f"gemini_{args.split}_per_wish.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["wish_id"] + [f"{k}_{m}" for k in per for m in base.METRICS])
        for i, w in enumerate(ws):
            wr.writerow([w["id"]] + [f"{per[k][i][m]:.4f}" for k in per for m in base.METRICS])
    with open(os.path.join(RES, "gemini_metrics.json"), "w", encoding="utf-8") as f:
        json.dump({"split": args.split, "models": summary, "top": TOP}, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
