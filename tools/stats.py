#!/usr/bin/env python3
"""Scenario-level statistics for E1, E1-v2 and E4 (confidence intervals, paired tests, latency medians).

Usage:
    python3 tools/stats.py [--results BookingSystem.Evaluation/results]

Why scenario level: Gemini runs use temperature 0.2 and the three repeats of a scenario almost always give
the same outcome (std = 0), so repeats are not independent observations. Each (model, scenario) pair is
therefore collapsed into one outcome = majority over its repeats, and the effective sample size is the number
of scenarios (74 in E1, 24 shared scenarios in E1 vs E1-v2, 25 in E4), not the number of runs.

- Confidence intervals for proportions: Wilson score interval, 95 %.
- Paired comparison of two conditions on the same scenarios: exact McNemar test (two-sided binomial test on
  discordant scenarios) plus a scenario bootstrap (10 000 resamples) of the difference in success rate.
- Latency: per-turn median, mean and p95 over all runs (median is what requirement 3 in table 1.3 uses).

Writes results/stats_scenario_level.json and prints Markdown tables. Pure Python, no SciPy needed.
"""
import argparse
import csv
import json
import math
import os
import random
import statistics
from collections import defaultdict

Z = 1.959963984540054


def wilson(k, n, z=Z):
    if n == 0:
        return (float("nan"), float("nan"))
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return (max(0.0, c - h), min(1.0, c + h))


def binom_two_sided(k, n):
    """Exact two-sided binomial test p-value for k successes of n with p = 0.5 (McNemar exact)."""
    if n == 0:
        return 1.0
    probs = [math.comb(n, i) / 2 ** n for i in range(n + 1)]
    pk = probs[k]
    return min(1.0, sum(p for p in probs if p <= pk + 1e-12))


def percentile(xs, q):
    xs = sorted(xs)
    if not xs:
        return float("nan")
    i = (len(xs) - 1) * q
    lo, hi = math.floor(i), math.ceil(i)
    return xs[lo] + (xs[hi] - xs[lo]) * (i - lo)


def short(model):
    return model.split(":", 1)[1].split("/")[-1]


def scenario_outcomes(rows):
    """(model, scenario) -> majority success over repeats (ties count as failure)."""
    acc = defaultdict(list)
    for r in rows:
        acc[(r["model"], r["scenario_id"])].append(int(r["task_success"]))
    return {k: int(sum(v) * 2 > len(v)) for k, v in acc.items()}


def paired(before, after, keys, rng, n_boot=10000):
    b = [before[k] for k in keys]
    a = [after[k] for k in keys]
    n = len(keys)
    improved = sum(1 for x, y in zip(b, a) if x == 0 and y == 1)
    worsened = sum(1 for x, y in zip(b, a) if x == 1 and y == 0)
    diffs = [y - x for x, y in zip(b, a)]
    boots = []
    for _ in range(n_boot):
        s = [diffs[rng.randrange(n)] for _ in range(n)]
        boots.append(sum(s) / n)
    return {
        "n": n, "before": sum(b) / n, "after": sum(a) / n,
        "before_ci": wilson(sum(b), n), "after_ci": wilson(sum(a), n),
        "improved": improved, "worsened": worsened,
        "mcnemar_p": binom_two_sided(min(improved, worsened), improved + worsened),
        "diff": sum(diffs) / n, "diff_ci": (percentile(boots, 0.025), percentile(boots, 0.975)),
    }


def fmt(x, nd=3):
    return f"{x:.{nd}f}".replace(".", ",")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", default=os.path.join("BookingSystem.Evaluation", "results"))
    args = ap.parse_args()
    rng = random.Random(42)
    out = {}

    rows = list(csv.DictReader(open(os.path.join(args.results, "agent_runs.csv"), encoding="utf-8")))

    # ---------------------------------------------------------------- E1: scenario-level success + latency
    e1 = [r for r in rows if r["experiment"] == "E1"]
    e1_sc = scenario_outcomes(e1)
    models = sorted({r["model"] for r in e1}, key=lambda m: (not m.startswith("gemini"), m))
    out["E1"] = {}
    print("## E1 (scenario level, majority over repeats)\n")
    print("| Модель | Повторів | Сценаріїв | Успішних | Task success | 95% ДІ (Wilson) | Затримка ходу, с: медіана / сер. / p95 |")
    print("|---|---|---|---|---|---|---|")
    for m in models:
        keys = [k for k in e1_sc if k[0] == m]
        k = sum(e1_sc[x] for x in keys)
        n = len(keys)
        lat = [int(t) for r in e1 if r["model"] == m for t in r["latency_ms_turns"].split("|") if t]
        reps = max(int(r["repeat"]) for r in e1 if r["model"] == m)
        lo, hi = wilson(k, n)
        med, mean, p95 = statistics.median(lat) / 1000, statistics.mean(lat) / 1000, percentile(lat, 0.95) / 1000
        out["E1"][short(m)] = {"repeats": reps, "scenarios": n, "success": k, "rate": k / n, "ci": [lo, hi],
                               "latency_s": {"median": med, "mean": mean, "p95": p95, "turns": len(lat)}}
        print(f"| {short(m)} | {reps} | {n} | {k} | {fmt(k / n)} | [{fmt(lo)}; {fmt(hi)}] | {fmt(med, 1)} / {fmt(mean, 1)} / {fmt(p95, 1)} |")

    # 3.8-flash vs 3.6-flash (same scenarios)
    a, b = "gemini:gemini-3.8-flash", "gemini:gemini-3.6-flash"
    keys = sorted({s for (m, s) in e1_sc if m == a} & {s for (m, s) in e1_sc if m == b})
    p = paired({s: e1_sc[(a, s)] for s in keys}, {s: e1_sc[(b, s)] for s in keys}, keys, rng)
    out["E1"]["3.8_vs_3.6"] = p
    print(f"\n3.8-flash vs 3.6-flash: discordant {p['improved'] + p['worsened']}, McNemar p = {p['mcnemar_p']:.3f}")

    # ---------------------------------------------------------------- E1 vs E1-v2 on shared scenarios
    v2 = [r for r in rows if r["experiment"] == "E1-v2"]
    v2_sc = scenario_outcomes(v2)
    out["E1v2"] = {}
    print("\n## E1 → E1-v2 on shared scenarios (scenario level, exact McNemar, scenario bootstrap)\n")
    print("| Модель | n | E1 [95% ДІ] | E1-v2 [95% ДІ] | краще / гірше | p (McNemar) | Δ [95% ДІ] |")
    print("|---|---|---|---|---|---|---|")
    pooled_b, pooled_a, pooled_keys = {}, {}, []
    booking_b, booking_a, booking_keys = {}, {}, []
    for m in sorted({r["model"] for r in v2}, key=lambda m: (not m.startswith("gemini"), m)):
        shared = sorted({s for (mm, s) in v2_sc if mm == m} & {s for (mm, s) in e1_sc if mm == m})
        bb = {s: e1_sc[(m, s)] for s in shared}
        aa = {s: v2_sc[(m, s)] for s in shared}
        p = paired(bb, aa, shared, rng)
        out["E1v2"][short(m)] = p
        for s in shared:
            pooled_b[(m, s)], pooled_a[(m, s)] = bb[s], aa[s]
            pooled_keys.append((m, s))
            if s.startswith("B-"):
                booking_b[(m, s)], booking_a[(m, s)] = bb[s], aa[s]
                booking_keys.append((m, s))
        pb = paired({s: bb[s] for s in shared if s.startswith("B-")}, {s: aa[s] for s in shared if s.startswith("B-")},
                    [s for s in shared if s.startswith("B-")], rng)
        out["E1v2"][short(m) + "_booking"] = pb
        print(f"| {short(m)} | {p['n']} | {fmt(p['before'])} [{fmt(p['before_ci'][0])}; {fmt(p['before_ci'][1])}] | "
              f"{fmt(p['after'])} [{fmt(p['after_ci'][0])}; {fmt(p['after_ci'][1])}] | {p['improved']} / {p['worsened']} | "
              f"{p['mcnemar_p']:.2f} | {fmt(p['diff'])} [{fmt(p['diff_ci'][0])}; {fmt(p['diff_ci'][1])}] |")

    # pooled: bootstrap over scenarios (all models of a scenario move together), McNemar over (model, scenario)
    def pooled_stats(bmap, amap, keys):
        scen = sorted({s for (_, s) in keys})
        by_s = defaultdict(list)
        for k in keys:
            by_s[k[1]].append(amap[k] - bmap[k])
        boots = []
        for _ in range(10000):
            pick = [scen[rng.randrange(len(scen))] for _ in scen]
            d = [x for s in pick for x in by_s[s]]
            boots.append(sum(d) / len(d))
        imp = sum(1 for k in keys if bmap[k] == 0 and amap[k] == 1)
        wor = sum(1 for k in keys if bmap[k] == 1 and amap[k] == 0)
        return {"pairs": len(keys), "scenarios": len(scen),
                "before": sum(bmap[k] for k in keys) / len(keys), "after": sum(amap[k] for k in keys) / len(keys),
                "improved": imp, "worsened": wor, "mcnemar_p_unclustered": binom_two_sided(min(imp, wor), imp + wor),
                "diff": sum(amap[k] - bmap[k] for k in keys) / len(keys),
                "diff_ci_cluster_bootstrap": (percentile(boots, 0.025), percentile(boots, 0.975))}

    out["E1v2"]["pooled"] = pooled_stats(pooled_b, pooled_a, pooled_keys)
    out["E1v2"]["pooled_booking"] = pooled_stats(booking_b, booking_a, booking_keys)
    for name in ("pooled", "pooled_booking"):
        p = out["E1v2"][name]
        print(f"\n{name}: {p['pairs']} (model, scenario) pairs over {p['scenarios']} scenarios: "
              f"{fmt(p['before'])} → {fmt(p['after'])}, better {p['improved']} / worse {p['worsened']}, "
              f"McNemar p = {p['mcnemar_p_unclustered']:.3f} (ignores clustering by scenario), "
              f"Δ = {fmt(p['diff'])}, 95% cluster-bootstrap CI [{fmt(p['diff_ci_cluster_bootstrap'][0])}; {fmt(p['diff_ci_cluster_bootstrap'][1])}]")

    # new booking scenarios (written after the E1 errors were seen)
    print("\nNew scenarios in E1-v2 only (B-07…B-15, written after looking at E1 errors):")
    for m in sorted({r["model"] for r in v2}, key=lambda m: (not m.startswith("gemini"), m)):
        new = sorted(s for (mm, s) in v2_sc if mm == m and (m, s) not in e1_sc)
        k = sum(v2_sc[(m, s)] for s in new)
        lo, hi = wilson(k, len(new))
        out["E1v2"][short(m) + "_new"] = {"n": len(new), "success": k, "ci": [lo, hi]}
        print(f"  {short(m)}: {k}/{len(new)} [{fmt(lo)}; {fmt(hi)}]")

    # ---------------------------------------------------------------- E4: ASR with CIs (scenario level)
    sec_path = os.path.join(args.results, "security_runs.csv")
    if os.path.exists(sec_path):
        sec = list(csv.DictReader(open(sec_path, encoding="utf-8")))
        out["E4"] = {}
        acc = defaultdict(list)
        for r in sec:
            if r["type"] == "benign":  # legitimate look-alike requests measure over-refusal, not ASR
                continue
            acc[(r["model"], r["scenario_id"])].append(1 - int(r["task_success"]))  # attack succeeded
        print("\n## E4 ASR (scenario level)\n")
        for m in sorted({k[0] for k in acc}, key=lambda m: (not m.startswith("gemini"), m)):
            keys = [k for k in acc if k[0] == m]
            k = sum(int(sum(acc[x]) * 2 > len(acc[x])) for x in keys)
            lo, hi = wilson(k, len(keys))
            out["E4"][short(m)] = {"attacks": len(keys), "succeeded": k, "ci": [lo, hi]}
            print(f"  {short(m)}: {k}/{len(keys)}  95% CI [{fmt(lo)}; {fmt(hi)}]")

    path = os.path.join(args.results, "stats_scenario_level.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print("\nwrote", path)


if __name__ == "__main__":
    main()
