#!/usr/bin/env python3
"""E5-agent: does the learned ranking change what the agent recommends?

Usage: tools/.venv/bin/python tools/ranking/agent_eval.py
Reads BookingSystem.Evaluation/results/ranking/agent_wishes.csv (two runs of datasets/wish_scenarios.json with
gemini-3.6-flash: E5-ranked = ranking on, E5-price = Ai:CarRanking:Enabled=false, i.e. the current order by price)
and scores the cars NAMED in each final reply with the rubric: Hit@1 (first named car has label >= 2) and
Precision (share of named cars with label >= 2). Scenarios are paired; differences use the exact McNemar test.
Writes results/ranking/agent_eval.json and agent_eval.csv.
"""
import csv
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from rubric import car_attrs, label, relevance  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "BookingSystem.Evaluation", "results", "ranking")


def mcnemar(b, c):
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    return min(1.0, 2 * sum(math.comb(n, i) for i in range(k + 1)) / 2 ** n)


def wilson(k, n, z=1.959964):
    if n == 0:
        return [math.nan, math.nan]
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return [max(0.0, c - h), min(1.0, c + h)]


def main():
    cars = [car_attrs(r) for r in csv.DictReader(open(os.path.join(RES, "cars.csv"), encoding="utf-8"))]
    for c in cars:
        c["short"] = c["name"].rsplit(" (", 1)[0]          # "Toyota Camry"
    scenarios = {s["id"]: s for s in json.load(open(os.path.join(ROOT, "BookingSystem.Evaluation", "datasets", "wish_scenarios.json"), encoding="utf-8"))["scenarios"]}
    runs = list(csv.DictReader(open(os.path.join(RES, "agent_wishes.csv"), encoding="utf-8")))

    rows, by = [], {}
    for r in runs:
        s = scenarios[r["scenario_id"]]
        reply = r["final_reply"]
        named = sorted((reply.find(c["short"]), c) for c in cars if c["short"] in reply)
        named = [c for pos, c in named if pos >= 0]
        labels = [label(relevance(s["needs"], c)) for c in named]
        calls = [c for turn in json.loads(r["tool_calls_json"]) for c in turn if c["Name"] == "search_available_cars"]
        args = json.loads(calls[-1]["ArgumentsJson"]) if calls else {}
        best_possible = max(label(relevance(s["needs"], c)) for c in cars)
        row = {
            "experiment": r["experiment"], "scenario": r["scenario_id"], "searched": bool(calls),
            "wishes_arg": args.get("wishes", ""), "filters": ",".join(k for k in args if k not in ("pickupDate", "returnDate", "wishes")),
            "named": len(named), "names": "; ".join(c["short"] for c in named), "labels": " ".join(map(str, labels)),
            "hit1": int(bool(labels) and labels[0] >= 2),
            "precision": (sum(l >= 2 for l in labels) / len(labels)) if labels else 0.0,
            "best_possible": best_possible,
        }
        rows.append(row)
        by[(r["experiment"], r["scenario_id"])] = row

    ids = sorted(scenarios)
    summary = {}
    for exp in ("E5-price", "E5-ranked"):
        rs = [by[(exp, i)] for i in ids if (exp, i) in by]
        hits = sum(r["hit1"] for r in rs)
        summary[exp] = {
            "scenarios": len(rs), "searched": sum(r["searched"] for r in rs),
            "passed_wishes": sum(bool(r["wishes_arg"]) for r in rs),
            "used_filters": sum(bool(r["filters"]) for r in rs),
            "hit1": hits / len(rs), "hit1_ci": wilson(hits, len(rs)),
            "precision": sum(r["precision"] for r in rs) / len(rs),
        }
    better = sum(1 for i in ids if by[("E5-ranked", i)]["hit1"] > by[("E5-price", i)]["hit1"])
    worse = sum(1 for i in ids if by[("E5-ranked", i)]["hit1"] < by[("E5-price", i)]["hit1"])
    summary["hit1_paired"] = {"better": better, "worse": worse, "mcnemar_p": mcnemar(better, worse)}
    pb = sum(1 for i in ids if by[("E5-ranked", i)]["precision"] > by[("E5-price", i)]["precision"] + 1e-9)
    pw = sum(1 for i in ids if by[("E5-ranked", i)]["precision"] < by[("E5-price", i)]["precision"] - 1e-9)
    summary["precision_paired"] = {"better": pb, "worse": pw, "sign_p": mcnemar(pb, pw)}

    with open(os.path.join(RES, "agent_eval.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.DictWriter(f, fieldnames=list(rows[0]))
        wr.writeheader()
        wr.writerows(rows)
    with open(os.path.join(RES, "agent_eval.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=1)

    for i in ids:
        a, b = by[("E5-price", i)], by[("E5-ranked", i)]
        print(f"{i}  price: {a['labels']:<12} [{a['names'][:60]}]\n       ranked: {b['labels']:<12} [{b['names'][:60]}]  wishes='{b['wishes_arg'][:50]}'")
    print(json.dumps(summary, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
