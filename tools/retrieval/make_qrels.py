#!/usr/bin/env python3
"""Builds BookingSystem.Evaluation/datasets/retrieval_qrels.json for E3 (knowledge-base search).

Usage: python3 tools/retrieval/make_qrels.py

Keeps the 56 original queries (Q-01…Q-56, written before the first E3 run) and adds new ones written for the
expanded knowledge base (24 documents). A query's relevant fragments are given as (document, start of the section
title) and resolved to chunk keys "document#index" exactly as MarkdownChunker numbers sections, so labels cannot
drift when documents change. Every query gets a fold 1..5 for k-fold cross-validation, stratified by kind and language.
"""
import json
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
KB = os.path.join(ROOT, "BookingSystem", "KnowledgeBase")
OUT = os.path.join(ROOT, "BookingSystem.Evaluation", "datasets", "retrieval_qrels.json")
K = 5

# (query, [(document, section title prefix), ...], kind, lang)
NEW = [
    # booking
    ("статус бронювання очікує", [("booking", "Статуси")], "keyword", "uk"),
    ("скільки часу адміністратор підтверджує заявку", [("booking", "Статуси")], "descriptive", "uk"),
    ("максимальний строк оренди", [("booking", "Мінімальний і максимальний")], "keyword", "uk"),
    ("чи можна взяти машину на півтора місяця одним бронюванням", [("booking", "Мінімальний і максимальний"), ("long-term", "Оренда понад 30")], "descriptive", "uk"),
    ("бронювання за 60 днів", [("booking", "Як далеко наперед")], "keyword", "uk"),
    ("скільки замовлень можна тримати на одному акаунті одночасно", [("booking", "Скільки бронювань")], "descriptive", "uk"),
    ("Can I change the dates of my booking?", [("booking", "Зміна дат"), ("cancellation", "Зміна дат")], "descriptive", "en"),
    ("промокод", [("booking", "Знижки та промокоди")], "keyword", "uk"),
    ("чи потрібна реєстрація, щоб забронювати", [("booking", "Бронювання без реєстрації")], "descriptive", "uk"),
    # deposit
    ("депозит готівкою", [("deposit", "Депозит готівкою")], "keyword", "uk"),
    ("коли мені повернуть заблоковані гроші після оренди", [("deposit", "Коли блокується")], "descriptive", "uk"),
    ("віртуальна картка для депозиту", [("deposit", "Які картки")], "keyword", "uk"),
    ("як платити, якщо оренду оформлює компанія", [("deposit", "Оплата для компаній"), ("long-term", "Корпоративні клієнти")], "descriptive", "uk"),
    ("deposit amount SUV", [("deposit", "Розмір депозиту")], "keyword", "en"),
    ("оплата Apple Pay", [("deposit", "Способи оплати")], "keyword", "uk"),
    ("чи треба вносити передоплату під час бронювання", [("deposit", "Коли потрібно оплатити")], "descriptive", "uk"),
    # documents
    ("мінімальний вік водія", [("documents", "Мінімальний вік")], "keyword", "uk"),
    ("мені 23 роки, чи буде додаткова плата", [("documents", "Молодий водій")], "descriptive", "uk"),
    ("I am a foreign citizen, what documents do I need?", [("documents", "Іноземні громадяни")], "descriptive", "en"),
    ("посвідчення в Дії", [("documents", "Цифрові документи")], "keyword", "uk"),
    ("чи можна орендувати, якщо мене позбавили прав", [("documents", "Позбавлення права")], "descriptive", "uk"),
    ("водійський стаж купе", [("documents", "Вимоги до водійського стажу")], "keyword", "uk"),
    # insurance
    ("пакет Стандарт+ ціна", [("insurance", "Пакет «Стандарт+»")], "keyword", "uk"),
    ("що буде, якщо пошкоджу лобове скло", [("insurance", "Шини, диски та скло")], "descriptive", "uk"),
    ("страхування пасажирів", [("insurance", "Страхування водія")], "keyword", "uk"),
    ("у яких випадках страховка не діє", [("insurance", "Що не покриває")], "descriptive", "uk"),
    ("full coverage insurance price", [("insurance", "Пакет «Повний захист»")], "keyword", "en"),
    ("якщо машину вкрадуть, хто платить", [("insurance", "Викрадення")], "descriptive", "uk"),
    # cancellation
    ("скасування менше ніж за добу", [("cancellation", "Скасування менш як за 24")], "keyword", "uk"),
    ("я не прийшов забрати машину, що буде", [("cancellation", "Неявка")], "descriptive", "uk"),
    ("How long does a refund take?", [("cancellation", "Повернення коштів")], "descriptive", "en"),
    ("що, якщо компанія не може надати заброньоване авто", [("cancellation", "Скасування з боку компанії"), ("cars", "Конкретна модель")], "descriptive", "uk"),
    ("відхилена заявка", [("cancellation", "Відхилення заявки")], "keyword", "uk"),
    # fuel
    ("бензин А-92", [("fuel", "Яке пальне")], "keyword", "uk"),
    ("заправив дизель у бензинову машину, що робити", [("fuel", "Заправка неправильним"), ("roadside", "Евакуація")], "descriptive", "uk"),
    ("передоплачений бак", [("fuel", "Опція «Передоплачений бак»")], "keyword", "uk"),
    ("What happens if I return the car with less fuel?", [("fuel", "Якщо повернути авто з неповним")], "descriptive", "en"),
    ("чек АЗС", [("fuel", "Чек про заправку")], "keyword", "uk"),
    # late return
    ("пільговий період повернення", [("late-return", "Пільговий")], "keyword", "uk"),
    ("запізнююсь на дві години, скільки доплатити", [("late-return", "Запізнення від 1 до 3")], "descriptive", "uk"),
    ("extend rental", [("late-return", "Як продовжити")], "keyword", "en"),
    ("хочу повернути машину на три дні раніше, чи повернуть гроші", [("late-return", "Дострокове")], "descriptive", "uk"),
    ("повернення вночі", [("late-return", "Повернення поза робочим"), ("pickup", "Видача та повернення поза графіком")], "keyword", "uk"),
    # mileage
    ("перевищення пробігу ціна за км", [("mileage", "Оплата перевищення")], "keyword", "uk"),
    ("скільки кілометрів можна проїхати за тиждень оренди", [("mileage", "Безлімітний")], "descriptive", "uk"),
    ("пакет далека поїздка", [("mileage", "Пакет «Далека поїздка»")], "keyword", "uk"),
    ("Is mileage limited for a coupe?", [("mileage", "Ліміт пробігу"), ("mileage", "Безлімітний")], "descriptive", "en"),
    # damage
    ("подряпина 2 см", [("damage", "Дрібні пошкодження")], "keyword", "uk"),
    ("в мене сталася аварія, що робити першим", [("damage", "Що робити при ДТП")], "descriptive", "uk"),
    ("європротокол", [("damage", "Європротокол")], "keyword", "uk"),
    ("скільки коштує загубити ключ", [("damage", "Втрата ключів")], "descriptive", "uk"),
    ("interior stain cleaning cost", [("damage", "Пошкодження та забруднення салону")], "keyword", "en"),
    # borders
    ("зелена карта ціна", [("borders", "Вартість виїзду")], "keyword", "uk"),
    ("чи можна поїхати в Німеччину", [("borders", "Дозволені країни")], "descriptive", "uk"),
    ("Can I drive the electric car to Poland?", [("borders", "Які авто не можна"), ("ev", "Обмеження")], "descriptive", "en"),
    ("дозвіл на виїзд за кордон", [("borders", "Як отримати дозвіл")], "keyword", "uk"),
    ("що потрібно мати з собою на кордоні", [("borders", "Документи для перетину")], "descriptive", "uk"),
    # extras
    ("GPS навігатор оренда", [("extras", "GPS")], "keyword", "uk"),
    ("багажний бокс", [("extras", "Багажний бокс")], "keyword", "uk"),
    ("чи буде інтернет у машині", [("extras", "Мобільний Wi-Fi")], "descriptive", "uk"),
    ("second driver fee", [("extras", "Другий водій")], "keyword", "en"),
    # pickup
    ("офіс Дніпро адреса", [("pickup", "Адреси офісів")], "keyword", "uk"),
    ("скільки триває видача авто", [("pickup", "Як відбувається видача")], "descriptive", "uk"),
    ("штраф за паління", [("pickup", "Паління")], "keyword", "uk"),
    ("можна взяти собаку в машину", [("pickup", "Перевезення тварин")], "descriptive", "uk"),
    ("Can I return the car in another city?", [("pickup", "Повернення в іншому")], "descriptive", "en"),
    ("де документи на авто", [("pickup", "Документи в автомобілі")], "keyword", "uk"),
    # children
    ("автокрісло до 12 років", [("children", "Коли потрібне")], "keyword", "uk"),
    ("яке крісло потрібне дитині на 20 кілограмів", [("children", "Яке крісло обрати"), ("extras", "Бустер")], "descriptive", "uk"),
    ("кріплення isofix", [("children", "Кріплення Isofix")], "keyword", "uk"),
    ("скільки дитячих крісел поміститься в седан", [("children", "Скільки крісел")], "descriptive", "uk"),
    ("можна взяти своє крісло для дитини", [("children", "Власне дитяче")], "descriptive", "uk"),
    ("візок у багажник", [("children", "Дитячий візок")], "keyword", "uk"),
    ("Can I put the baby seat in the front?", [("children", "Крісло на передньому")], "descriptive", "en"),
    ("чи треба наперед замовляти крісло", [("children", "Замовлення крісел")], "descriptive", "uk"),
    # ev
    ("які у вас є електромобілі", [("ev", "Які електромобілі")], "descriptive", "uk"),
    ("запас ходу взимку", [("ev", "Запас ходу"), ("ev", "Поради для зимових")], "keyword", "uk"),
    ("charging stations CCS", [("ev", "Де заряджати")], "keyword", "en"),
    ("швидка зарядка до 80%", [("ev", "Швидка зарядка")], "keyword", "uk"),
    ("з яким зарядом треба повернути електрокар", [("ev", "Заряд при видачі"), ("fuel", "Зарядка електромобілів")], "descriptive", "uk"),
    ("чи можна заряджати від звичайної розетки", [("ev", "Зарядка від побутової")], "descriptive", "uk"),
    ("чи можна на теслі поїхати в Польщу", [("ev", "Обмеження"), ("borders", "Які авто не можна")], "descriptive", "uk"),
    # winter
    ("зимова гума", [("winter", "Зимова гума"), ("extras", "Зимові ланцюги")], "keyword", "uk"),
    ("які машини краще для поїздки в Карпати взимку", [("winter", "Яке авто обрати")], "descriptive", "uk"),
    ("ланцюги на колеса", [("winter", "Ланцюги"), ("extras", "Зимові ланцюги")], "keyword", "uk"),
    ("як їздити по льоду", [("winter", "Керування на ожеледиці")], "descriptive", "uk"),
    ("The car won't start in the cold, what should I do?", [("winter", "Авто не заводиться"), ("roadside", "Розряджений акумулятор")], "descriptive", "en"),
    ("ручник у мороз", [("winter", "Паркування в мороз")], "keyword", "uk"),
    ("чи доставляєте авто в Буковель", [("winter", "Гірськолижні курорти")], "descriptive", "uk"),
    ("незамерзайка і щітка", [("winter", "Підготовка авто до зими")], "keyword", "uk"),
    # roadside
    ("евакуатор безкоштовно", [("roadside", "Евакуація"), ("damage", "Технічна несправність")], "keyword", "uk"),
    ("пробив колесо в дорозі, хто платить", [("roadside", "Прокол колеса")], "descriptive", "uk"),
    ("сів акумулятор", [("roadside", "Розряджений акумулятор")], "keyword", "uk"),
    ("зачинив ключі в машині", [("roadside", "Ключі зачинені")], "descriptive", "uk"),
    ("red warning light dashboard", [("roadside", "Контрольні лампи")], "keyword", "en"),
    ("чи дадуть іншу машину, якщо ця зламалася", [("roadside", "Заміна автомобіля")], "descriptive", "uk"),
    ("можна самому відремонтувати, а потім ви компенсуєте", [("roadside", "Ремонт власними")], "descriptive", "uk"),
    ("цілодобова допомога", [("roadside", "Цілодобова допомога")], "keyword", "uk"),
    # fines and parking
    ("штраф з камери швидкості", [("fines-parking", "Штрафи камер"), ("damage", "Штрафи за порушення")], "keyword", "uk"),
    ("прийшов штраф через місяць після оренди", [("fines-parking", "Штрафи після завершення")], "descriptive", "uk"),
    ("машину забрав евакуатор через паркування", [("fines-parking", "Неправильне паркування")], "descriptive", "uk"),
    ("оплата паркування", [("fines-parking", "Платне паркування")], "keyword", "uk"),
    ("Do I need a vignette in Hungary?", [("fines-parking", "Платні дороги")], "descriptive", "en"),
    ("як я дізнаюся про штраф", [("fines-parking", "Як ми повідомляємо")], "descriptive", "uk"),
    ("штраф за кордоном", [("fines-parking", "Порушення правил за кордоном")], "keyword", "uk"),
    # long-term
    ("хочу орендувати машину на пів року", [("long-term", "Оренда понад 30")], "descriptive", "uk"),
    ("знижка за місяць оренди", [("long-term", "Тарифи на тривалу")], "keyword", "uk"),
    ("хто робить ТО, якщо я беру авто надовго", [("long-term", "Технічне обслуговування")], "descriptive", "uk"),
    ("corporate rental invoice VAT", [("long-term", "Корпоративні клієнти"), ("deposit", "Оплата для компаній")], "keyword", "en"),
    ("можна вписати кількох співробітників як водіїв", [("long-term", "Кілька водіїв")], "descriptive", "uk"),
    ("гарантійний лист замість депозиту", [("long-term", "Гарантія замість")], "keyword", "uk"),
    # support
    ("email підтримки", [("support", "Як зв'язатися")], "keyword", "uk"),
    ("чи може чат-бот дати знижку", [("support", "Що вміє AI-помічник")], "descriptive", "uk"),
    ("скарга термін розгляду", [("support", "Скарги")], "keyword", "uk"),
    ("не згоден з тим, що утримали з депозиту", [("support", "Оскарження утримань"), ("deposit", "Утримання з депозиту")], "descriptive", "uk"),
    ("забув телефон у машині", [("support", "Забуті речі")], "descriptive", "uk"),
    ("Do your managers speak English?", [("support", "Мови обслуговування")], "descriptive", "en"),
    # vehicle use
    ("робота в таксі Uber", [("vehicle-use", "Заборонене використання")], "keyword", "uk"),
    ("скільки людей можна посадити в машину", [("vehicle-use", "Кількість пасажирів")], "descriptive", "uk"),
    ("можна перевезти холодильник", [("vehicle-use", "Перевезення вантажів")], "descriptive", "uk"),
    ("бездоріжжя", [("vehicle-use", "Дороги та бездоріжжя")], "keyword", "uk"),
    ("Can my friend drive the car for a while?", [("vehicle-use", "Хто може керувати"), ("documents", "Вимоги до другого водія")], "descriptive", "en"),
    ("наліпки на авто", [("vehicle-use", "Зміни в автомобілі")], "keyword", "uk"),
    ("чи можна їхати в зону бойових дій", [("vehicle-use", "Небезпечні території"), ("borders", "Заборонені напрямки")], "descriptive", "uk"),
    # cars
    ("чим відрізняється SUV від мінівена", [("cars", "Категорії автомобілів")], "descriptive", "uk"),
    ("я отримаю саме ту машину, що на фото, чи схожу", [("cars", "Конкретна модель"), ("cars", "Фото в каталозі")], "descriptive", "uk"),
    ("авто з механічною коробкою", [("cars", "Коробка передач")], "keyword", "uk"),
    ("гібрид зарядка", [("cars", "Типи пального")], "keyword", "uk"),
    ("Which car fits a lot of luggage?", [("cars", "Місткість салону")], "descriptive", "en"),
    ("рік випуску авто", [("cars", "Вік автомобілів")], "keyword", "uk"),
    # technical
    ("як ви перевіряєте машину перед видачею", [("technical", "Підготовка авто")], "descriptive", "uk"),
    ("тиск у шинах", [("technical", "Тиск у шинах")], "keyword", "uk"),
    ("можна долити мастило самому", [("technical", "Доливання рідин")], "descriptive", "uk"),
    ("камера заднього огляду", [("technical", "Камери й датчики")], "keyword", "uk"),
    ("Apple CarPlay", [("technical", "Мультимедіа")], "keyword", "en"),
    ("чи є адаптивний круїз", [("technical", "Круїз-контроль")], "descriptive", "uk"),
    ("чи треба мені робити ТО під час оренди на тиждень", [("technical", "Планове обслуговування")], "descriptive", "uk"),
    # privacy
    ("ви стежите за моєю машиною через GPS", [("privacy", "GPS-трекери")], "descriptive", "uk"),
    ("камери в салоні", [("privacy", "Камери в салоні")], "keyword", "uk"),
    ("скільки ви зберігаєте мої дані", [("privacy", "Строк зберігання")], "descriptive", "uk"),
    ("видалити акаунт", [("privacy", "Видалення облікового")], "keyword", "uk"),
    ("Is my chat with the assistant shared with anyone?", [("privacy", "Розмови з AI")], "descriptive", "en"),
    ("ви зберігаєте дані моєї картки", [("privacy", "Які дані ми збираємо")], "descriptive", "uk"),
    # airport
    ("доставка Бориспіль ціна", [("airport", "Доставка авто в аеропорт"), ("extras", "Доставка та повернення")], "keyword", "uk"),
    ("рейс затримується, ви дочекаєтесь", [("airport", "Затримка рейсу")], "descriptive", "uk"),
    ("нічний рейс видача", [("airport", "Нічні рейси"), ("pickup", "Видача та повернення поза графіком")], "keyword", "uk"),
    ("де мене зустрінуть в аеропорту", [("airport", "Зустріч в аеропорту")], "descriptive", "uk"),
    ("доставка на вокзал", [("airport", "Вокзали")], "keyword", "uk"),
    ("Who pays for airport parking when I return the car?", [("airport", "Паркування в аеропорту")], "descriptive", "en"),
]


def sections(doc):
    text = open(os.path.join(KB, doc + ".md"), encoding="utf-8").read()
    return [m.group(1).strip() for m in re.finditer(r"^## (.+)$", text, flags=re.M)]


def resolve(doc, prefix):
    hits = [i for i, t in enumerate(sections(doc)) if t.startswith(prefix)]
    assert len(hits) == 1, (doc, prefix, hits, sections(doc))
    return f"{doc}#{hits[0] + 1}"  # MarkdownChunker numbers sections from 1


def main():
    old = json.load(open(OUT, encoding="utf-8"))["queries"][:56]
    assert old[-1]["id"] == "Q-56", old[-1]["id"]
    queries = [dict(q, origin="original") for q in old]
    for k, (text, rel, kind, lang) in enumerate(NEW, 57):
        queries.append({"id": f"Q-{k:03d}", "query": text, "relevant": [resolve(d, p) for d, p in rel],
                        "split": "cv", "kind": kind, "lang": lang, "origin": "expanded-kb"})
    # stratified folds by (kind, lang), deterministic
    for group in sorted({(q["kind"], q["lang"]) for q in queries}):
        members = [q for q in queries if (q["kind"], q["lang"]) == group]
        for i, q in enumerate(members):
            q["fold"] = i % K + 1
    doc = {
        "description": "Relevance judgments for E3. 'relevant' lists knowledge-base chunk keys (document#index, see "
                       "BookingSystem/KnowledgeBase). kind: keyword (short, term-heavy) / descriptive (natural question). "
                       "fold: 1..5 for k-fold cross-validation (stratified by kind and language). split: dev/test of the "
                       "first E3 run for the original 56 queries; 'cv' for the queries added with the expanded knowledge "
                       "base. Generated by tools/retrieval/make_qrels.py.",
        "queries": queries,
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    from collections import Counter
    print(len(queries), Counter((q["kind"], q["lang"]) for q in queries), Counter(q["fold"] for q in queries),
          "multi-relevant", sum(1 for q in queries if len(q["relevant"]) > 1))


if __name__ == "__main__":
    main()
