"""Relevance rubric for E5: how well a car satisfies a structured need.

A wish is a set of needs (luggage for 3 suitcases, economy, mountains...). The rubric turns the car's attributes
(BookingSystem.Evaluation/results/ranking/cars.csv) into a utility u in [0, 1] for every need:

    hard needs (seats, luggage, budget, automatic):  multiply  — a car that cannot carry 7 people is irrelevant;
    soft needs (economy, city, comfort, style...):   average.

    relevance = prod(hard) * mean(soft)  ->  graded label 3 (>= 0.8), 2 (>= 0.55), 1 (>= 0.3), 0.

The rubric plays the role of a simulated customer: it knows the structured need, the ranking model sees only the
free text of the wish. In production the labels would come from bookings and clicks instead.
"""
import math

HARD = {"luggage", "people", "budget", "automatic"}


def _f(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return math.nan


def car_attrs(row):
    """Car row of cars.csv -> dict of typed attributes."""
    return {
        "id": int(row["car_id"]),
        "name": row["name"],
        "category": row["category"],
        "seats": int(row["seats"]),
        "auto": row["transmission"] == "Automatic",
        "fuel": row["fuel"],
        "price": float(row["price"]),
        "trunk": _f(row["trunk_l"]),
        "cons": _f(row["consumption"]),
        "range": _f(row["ev_range_km"]),
        "awd": row["awd"] == "1",
        "length": _f(row["length_mm"]),
        "hp": _f(row["power_hp"]),
        "premium": row["premium"] == "1",
    }


def _steps(x, steps, default=0.0):
    """First (threshold, value) with x >= threshold (steps sorted descending)."""
    if math.isnan(x):
        return default
    for threshold, value in steps:
        if x >= threshold:
            return value
    return default


def utility(need, param, c):
    ev = c["fuel"] == "Electric"
    cat = c["category"]
    if need == "luggage":
        thr = {1: 300, 2: 420, 3: 520, 4: 640}[param]
        return 1.0 if c["trunk"] >= thr else 0.4 if c["trunk"] >= 0.85 * thr else 0.0
    if need == "people":
        return 1.0 if c["seats"] >= param else 0.0
    if need == "budget":
        return 1.0 if c["price"] <= param else 0.3 if c["price"] <= 1.1 * param else 0.0
    if need == "automatic":
        return 1.0 if c["auto"] else 0.0
    if need == "kids":
        if cat == "coupe" or c["seats"] < 5:
            return 0.0
        return 0.5 + (0.25 if c["trunk"] >= 450 else 0) + (0.25 if cat in ("minivan", "suv") else 0)
    if need == "economy":
        if ev:
            return 1.0
        return 1.0 - _steps(c["cons"], [(8.5, 1.0), (7.0, 0.9), (6.2, 0.7), (5.4, 0.45), (4.6, 0.2)], 0.0)
    if need == "cheap":
        return 1.0 - _steps(c["price"], [(3200, 1.0), (2300, 0.95), (1700, 0.8), (1300, 0.6), (1000, 0.4), (800, 0.15)], 0.0)
    if need == "city":
        return 1.0 - _steps(c["length"], [(4750, 0.95), (4550, 0.75), (4350, 0.5), (4100, 0.2)], 0.0)
    if need == "highway":
        u = _steps(c["length"], [(4800, 1.0), (4600, 0.85), (4400, 0.55), (4200, 0.3)], 0.15)
        if ev:
            u *= 1.0 if c["range"] >= 500 else 0.7 if c["range"] >= 420 else 0.3
        if c["hp"] < 100:
            u *= 0.7
        return u
    if need == "offroad":
        if c["awd"] and cat == "suv":
            return 1.0
        if c["awd"]:
            return 0.3
        return 0.35 if cat == "suv" else 0.1 if cat == "minivan" else 0.0
    if need == "premium":
        if c["premium"]:
            return 0.9 if cat == "coupe" else 1.0
        if cat == "sedan" and c["length"] >= 4850:
            return 0.5
        return 0.4 if c["hp"] >= 300 else 0.05
    if need == "sport":
        if c["hp"] >= 400:
            return 1.0
        if c["hp"] >= 300:
            return 0.9
        if cat == "coupe" and c["hp"] >= 240:
            return 0.8
        return 0.6 if c["hp"] >= 250 else 0.35 if c["hp"] >= 200 else 0.0
    if need == "eco":
        return 1.0 if ev else 0.45 if c["fuel"] == "Hybrid" else 0.0
    if need == "cargo":
        return _steps(c["trunk"], [(900, 1.0), (740, 0.85), (640, 0.6), (580, 0.4), (500, 0.2)], 0.0)
    if need == "pets":
        if cat == "coupe":
            return 0.0
        if cat in ("suv", "minivan"):
            return 1.0 if c["trunk"] >= 550 else 0.75 if c["trunk"] >= 450 else 0.4
        return 0.4 if c["trunk"] >= 480 else 0.15
    raise ValueError(need)


def relevance(needs, c):
    """needs: list of {"need": str, "param": int|None} -> score in [0, 1]."""
    hard = 1.0
    soft = []
    for n in needs:
        u = utility(n["need"], n.get("param"), c)
        if n["need"] in HARD:
            hard *= u
        else:
            soft.append(u)
    return hard * (sum(soft) / len(soft) if soft else 1.0)


def label(score):
    return 3 if score >= 0.8 else 2 if score >= 0.55 else 1 if score >= 0.3 else 0
