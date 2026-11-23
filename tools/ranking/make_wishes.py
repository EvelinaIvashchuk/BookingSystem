#!/usr/bin/env python3
"""Generates the E5 dataset of free-text car wishes: BookingSystem.Evaluation/datasets/car_wishes.json.

Usage (from the repository root, after `dotnet run --project BookingSystem.Evaluation -- ranking-data`):
    tools/.venv/bin/python tools/ranking/make_wishes.py

Every wish is 1-3 needs (15 types, e.g. "luggage for 3 suitcases", "economy", "mountains") written in Ukrainian
(about 15 % in English). Each need has 9 Ukrainian and 3 English phrasings and there are several sentence templates.
Phrasings and templates are split in advance: every third phrasing and two of the templates are used ONLY in the
test split, so the test checks generalisation to wording the model has never seen. Train and validation wishes are
drawn from the remaining phrasings and split 80/20 at random.

A wish is kept only if at least one available car fits it well (label 3) and not every car does.
Labels are not stored here: tools/ranking/train.py computes them with rubric.py from the structured needs.
"""
import csv
import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
from rubric import car_attrs, label, relevance  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CARS = os.path.join(ROOT, "BookingSystem.Evaluation", "results", "ranking", "cars.csv")
OUT = os.path.join(ROOT, "BookingSystem.Evaluation", "datasets", "car_wishes.json")
SEED = 20261123

LUG_NOM = {1: "одна велика валіза", 2: "дві великі валізи", 3: "три великі валізи", 4: "чотири великі валізи"}
LUG_ACC = {1: "одну велику валізу", 2: "дві великі валізи", 3: "три великі валізи", 4: "чотири великі валізи"}
LUG_INS = {1: "з однією великою валізою", 2: "з двома великими валізами", 3: "з трьома великими валізами", 4: "з чотирма великими валізами"}
LUG_EN = {1: "one big suitcase", 2: "two big suitcases", 3: "three big suitcases", 4: "four big suitcases"}
COLL = {2: "двоє", 3: "троє", 4: "четверо", 5: "п'ятеро", 6: "шестеро", 7: "семеро", 8: "восьмеро"}


def places(n):
    return f"{n} місця" if n <= 4 else f"{n} місць"


def people(n):
    return f"{n} людини" if n <= 4 else f"{n} людей"


def passengers(n):
    return f"{n} пасажири" if n <= 4 else f"{n} пасажирів"


# need -> (parameter values or None, Ukrainian phrasings, English phrasings); a phrasing is a function of the parameter
NEEDS = {
    "luggage": ([1, 2, 3, 4], [
        lambda n: f"місце в багажнику під {LUG_ACC[n]}",
        lambda n: f"везу {LUG_ACC[n]} в аеропорт",
        lambda n: f"їдемо у відпустку {LUG_INS[n]}",
        lambda n: f"багаж: {LUG_NOM[n]} і пара сумок",
        lambda n: f"треба вмістити {LUG_ACC[n]}",
        lambda n: f"після аеропорту поїдемо {LUG_INS[n]}",
        lambda n: f"з собою {LUG_NOM[n]}, потрібен нормальний багажник",
        lambda n: f"в багажник треба покласти {LUG_ACC[n]}",
        lambda n: f"багато багажу — {LUG_NOM[n]}",
    ], [
        lambda n: f"room for {LUG_EN[n]}",
        lambda n: f"we travel with {LUG_EN[n]}",
        lambda n: f"the trunk must fit {LUG_EN[n]}",
    ]),
    "people": ([2, 3, 4, 5, 6, 7, 8], [
        lambda n: f"нас {COLL[n]}",
        lambda n: f"поїдемо компанією з {n} осіб",
        lambda n: f"треба {places(n)}",
        lambda n: f"щоб сіли {COLL[n]} дорослих",
        lambda n: f"везу {passengers(n)}",
        lambda n: f"уся родина — {people(n)}",
        lambda n: f"авто на {people(n)}",
        lambda n: f"компанія з {n} друзів",
        lambda n: f"{COLL[n]} пасажирів разом із водієм",
    ], [
        lambda n: f"for {n} people",
        lambda n: f"we are {n} adults",
        lambda n: f"seats for {n}",
    ]),
    "kids": (None, [
        "з двома дітьми й дитячим кріслом", "з малою дитиною, треба місце під візок", "сімейна поїздка з дітьми",
        "діти мають сидіти зручно ззаду", "з немовлям і коляскою", "треба поставити два дитячі крісла",
        "для сім'ї з дітьми", "їдемо з дітьми на море", "з дитиною та її речами",
    ], ["travelling with kids", "space for a child seat and a stroller", "family trip with children"]),
    "economy": (None, [
        "економне по пальному", "щоб мало їло пального", "з невеликою витратою", "щоб не розоритися на заправках",
        "мінімальна витрата пального", "економний двигун", "щоб багато не палило", "дешеве в утриманні на пальному",
        "з малим розходом",
    ], ["fuel efficient", "low fuel consumption", "cheap to fuel"]),
    "cheap": (None, [
        "якомога дешевше", "недорого", "бюджетний варіант", "найдешевше, що є", "щось недороге",
        "не хочу переплачувати", "економ-клас", "з мінімальною ціною оренди", "дешева оренда",
    ], ["as cheap as possible", "budget option", "low rental price"]),
    "budget": ([800, 1000, 1200, 1500, 2000, 2500, 3000], [
        lambda a: f"до {a} грн на день",
        lambda a: f"бюджет {a} гривень за добу",
        lambda a: f"не дорожче {a} грн за добу",
        lambda a: f"максимум {a} грн/добу",
        lambda a: f"в межах {a} гривень на день",
        lambda a: f"щоб доба коштувала до {a} грн",
        lambda a: f"можу платити до {a} гривень за день",
        lambda a: f"ціна до {a} ₴ на добу",
        lambda a: f"не більше {a} гривень за день",
    ], [
        lambda a: f"under {a} UAH per day",
        lambda a: f"max {a} UAH a day",
        lambda a: f"budget {a} UAH/day",
    ]),
    "city": (None, [
        "для міста", "щоб легко паркуватися", "по місту кататися", "компактне для центру",
        "маленьке, щоб влазило на будь-яку парковку", "для вузьких вуличок старого міста",
        "зручне в заторах і на парковці", "міська машинка", "невелике авто для щоденних поїздок містом",
    ], ["for city driving", "easy to park", "small car for the city"]),
    "highway": (None, [
        "для далекої поїздки трасою", "їдемо через усю країну", "довга дорога, хочеться комфорту",
        "поїздка на 800 км в один бік", "багато кілометрів по трасі", "з Києва до Ужгорода й назад",
        "щоб не втомитися в дальній дорозі", "тривала подорож по Європі", "комфортне для траси",
    ], ["long road trip", "a lot of highway driving", "comfortable for long distances"]),
    "offroad": (None, [
        "в Карпати гірськими дорогами", "погані дороги й ґрунтовка", "взимку в гори, буде сніг",
        "обов'язково повний привід", "на рибалку бездоріжжям", "на гірськолижний курорт",
        "щоб не застрягти в багнюці", "дорога в село розбита", "до кемпінгу в лісі",
    ], ["off-road capable", "mountain roads with snow", "all-wheel drive needed"]),
    "premium": (None, [
        "щось представницьке для ділової зустрічі", "на весілля, щоб гарно виглядало", "преміум",
        "хочу вразити партнерів", "статусне авто", "зустріти важливого клієнта", "солідне авто бізнес-класу",
        "щоб було розкішно", "престижна машина на подію",
    ], ["luxury car", "something premium to impress clients", "executive car for a business meeting"]),
    "sport": (None, [
        "щось швидке й драйвове", "спорткар на вихідні", "хочу відчути драйв", "потужне авто",
        "щоб розганялося з місця", "покататися з вітерцем", "на день народження щось яскраве й швидке",
        "емоції від водіння", "щоб гарчав мотор",
    ], ["fast and fun", "a sports car", "powerful car for the weekend"]),
    "eco": (None, [
        "електромобіль", "щоб без бензину", "екологічне авто", "хочу спробувати електрокар",
        "щоб заряджати, а не заправляти", "з нульовими викидами", "щось на електриці", "зелений транспорт",
        "без вихлопу",
    ], ["electric car", "zero emissions", "no petrol, EV please"]),
    "cargo": (None, [
        "перевезти речі при переїзді", "везти два велосипеди", "треба вантажний простір",
        "перевезти невеликі меблі", "багато речей на дачу", "щоб помістилися коробки з речами",
        "для будматеріалів", "великий об'єм для речей", "відвезти намет, човен і спорядження",
    ], ["moving boxes", "need a lot of cargo space", "to carry bikes and gear"]),
    "automatic": (None, [
        "тільки автомат", "не вмію їздити на механіці", "з автоматичною коробкою", "без механіки",
        "коробка автомат обов'язково", "щоб не перемикати передачі", "АКПП", "на механіці не їжджу",
        "автомат, механіку не люблю",
    ], ["automatic only", "I can't drive a manual", "automatic transmission"]),
    "pets": (None, [
        "з великим собакою", "везу пса", "собака поїде в багажнику", "з двома собаками",
        "тварини поїдуть з нами", "з вівчаркою", "з собакою і її кліткою", "везу собаку до ветеринара за місто",
        "з домашніми улюбленцями",
    ], ["with a big dog", "dog-friendly car", "travelling with our dog"]),
}

# (template, number of needs it takes); {A} = first phrase capitalised
TEMPLATES_UK = [
    ("Потрібне авто: {a}.", 1), ("{A}.", 1), ("Що порадите? {A}.", 1),
    ("Потрібне авто: {a}, {b}.", 2), ("Шукаю машину — {a}, {b}", 2), ("{A}. {B}.", 2), ("Порадьте авто: {a}, {b}", 2),
    ("Хочу орендувати авто, {a}, {b}", 2), ("{A}, а ще {b}", 2), ("Яке авто взяти? {A}, {b}", 2),
    ("Потрібне авто: {a}, {b}, {c}.", 3), ("{A}, {b} і {c}", 3), ("Шукаю машину: {a}; {b}; {c}", 3), ("{A}. {B}, {c}.", 3),
]
TEMPLATES_EN = [
    ("I need a car: {a}.", 1), ("{A}.", 1),
    ("I need a car: {a}, {b}.", 2), ("{A}, {b}", 2), ("Looking for a car — {a} and {b}", 2),
    ("I need a car: {a}, {b}, {c}.", 3), ("{A}; {b}; {c}", 3),
]
# Held out for the test split only.
TEST_TEMPLATES = {"Що порадите? {A}.", "{A}, а ще {b}", "Яке авто взяти? {A}, {b}", "{A}. {B}, {c}.",
                  "Looking for a car — {a} and {b}", "{A}; {b}; {c}"}


def is_test_phrase(lang, index):
    return index % 3 == 2 if lang == "uk" else index == 2


def phrase(need, index, lang, param):
    _, uk, en = NEEDS[need]
    p = (uk if lang == "uk" else en)[index]
    return p(param) if callable(p) else p


def render(template, parts):
    keys = "abc"
    values = {}
    for i, part in enumerate(parts):
        values[keys[i]] = part
        values[keys[i].upper()] = part[:1].upper() + part[1:]
    return template.format(**values)


def main():
    rng = random.Random(SEED)
    cars = [car_attrs(r) for r in csv.DictReader(open(CARS, encoding="utf-8"))]
    names = list(NEEDS)

    def sample(split, count, seen):
        wishes, attempts = [], 0
        while len(wishes) < count and attempts < count * 200:
            attempts += 1
            lang = "en" if rng.random() < 0.15 else "uk"
            k = rng.choices([1, 2, 3], weights=[0.25, 0.5, 0.25])[0]
            chosen = rng.sample(names, k)
            if "cheap" in chosen and "budget" in chosen:
                continue
            needs, parts, phrase_ids = [], [], []
            for need in chosen:
                params, uk, en = NEEDS[need]
                bank = uk if lang == "uk" else en
                allowed = [i for i in range(len(bank)) if is_test_phrase(lang, i) == (split == "test")]
                idx = rng.choice(allowed)
                param = rng.choice(params) if params else None
                needs.append({"need": need, "param": param})
                parts.append(phrase(need, idx, lang, param))
                phrase_ids.append(f"{need}/{lang}/{idx}")
            templates = [t for t, n in (TEMPLATES_UK if lang == "uk" else TEMPLATES_EN)
                         if n == k and (t in TEST_TEMPLATES) == (split == "test")]
            if not templates:
                templates = [t for t, n in (TEMPLATES_UK if lang == "uk" else TEMPLATES_EN) if n == k]
            text = render(rng.choice(templates), parts)
            if text in seen:
                continue
            labels = [label(relevance(needs, c)) for c in cars]
            if 3 not in labels or all(l >= 2 for l in labels):
                continue
            seen.add(text)
            wishes.append({"text": text, "lang": lang, "needs": needs, "phrases": phrase_ids})
        return wishes

    seen = set()
    train_pool = sample("train", 600, seen)
    test = sample("test", 160, seen)
    rng.shuffle(train_pool)
    n_val = len(train_pool) // 5
    split = [("val", w) for w in train_pool[:n_val]] + [("train", w) for w in train_pool[n_val:]] + [("test", w) for w in test]

    out = []
    for i, (s, w) in enumerate(split, 1):
        out.append({"id": f"W-{i:03d}", "split": s, **w})
    doc = {
        "about": "E5: free-text car wishes for the learning-to-rank model. Needs are structured ground truth for the "
                 "relevance rubric (tools/ranking/rubric.py); the model sees only `text`. Test wishes use phrasings and "
                 "templates that never occur in train/val. Generated by tools/ranking/make_wishes.py, seed %d." % SEED,
        "wishes": out,
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    counts = {s: sum(1 for w in out if w["split"] == s) for s in ("train", "val", "test")}
    print("wrote", OUT, counts, "en:", sum(1 for w in out if w["lang"] == "en"))


if __name__ == "__main__":
    main()
