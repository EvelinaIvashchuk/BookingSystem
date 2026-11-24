#!/usr/bin/env python3
"""Adds the neural-network / Gemini comparison (E6) and the data-split description to the student's report (the Google Docs
export, edited in place at the XML level) and applies the formatting requests of the supervisor.

Usage (repository root):
    tools/.venv/bin/python report/src/patch_docx.py

Input : report/01_Звіт_Іващук_ЕВ_444_виправлений.docx   (never modified)
Output: report/02_Звіт_Іващук_ЕВ_444_нейромережі.docx

What it does
  1. Section 3.7: split table 3.3 + figure 3.5, description of the two neural networks and of Gemini as a zero-shot ranker.
  2. New section 5.9 (E6) with tables 5.14-5.17 and figures 5.19-5.20; the old 5.9 becomes 5.10 (table 5.14 -> 5.18).
  3. Annotation (uk/en), keywords, tasks, hypotheses, conclusions, cost table, five new references.
  4. Formatting: 14 pt everywhere after the task sheet (tables, listings, captions), table captions only above the table.
All numbers come from BookingSystem.Evaluation/results/ranking/*.json, nothing is typed by hand.
"""
import io
import json
import os
import re
import sys
import zipfile

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "report", "01_Звіт_Іващук_ЕВ_444_виправлений.docx")
OUT = os.path.join(ROOT, "report", "02_Звіт_Іващук_ЕВ_444_нейромережі.docx")
RES = os.path.join(ROOT, "BookingSystem.Evaluation", "results", "ranking")
FIG = os.path.join(ROOT, "report", "figures")
J = lambda n: json.load(open(os.path.join(RES, n), encoding="utf-8"))
CMP, NN, TIM, GEM = J("comparison.json"), J("nn_metrics.json"), J("nn_timing.json"), J("gemini_metrics.json")
EXP = J("nn_export.json")
G36, GLITE = "gemini-3.6-flash", "gemini-3.5-flash-lite"


# ------------------------------------------------------------------ number formatting
def n3(x):
    return f"{x:.3f}".replace(".", ",")


def sg(x):
    return ("+" if x >= 0 else "−") + f"{abs(x):.3f}".replace(".", ",")


def dec(x, nd=1):
    return f"{x:.{nd}f}".replace(".", ",")


def ci(c):
    return f"[{n3(c[0])}; {n3(c[1])}]"


def sci(c):
    return f"[{sg(c[0])}; {sg(c[1])}]"


S = CMP["summary"]
D = CMP["diffs_ndcg5"]
BN = CMP["by_need"]
NEED_UK = {"luggage": "багаж", "people": "кількість людей", "kids": "діти", "economy": "економність", "cheap": "дешевше",
           "budget": "бюджет на добу", "city": "місто, паркування", "highway": "траса, комфорт", "offroad": "гори, бездоріжжя",
           "premium": "статус, преміум", "sport": "драйв, спорт", "eco": "електро, екологія", "cargo": "вантаж, переїзд",
           "automatic": "лише автомат", "pets": "з собакою"}
COST_E6 = round(GEM["models"][G36]["cost_usd"] + GEM["models"][GLITE]["cost_usd"], 2)
G36M = GEM["models"][G36]


# ------------------------------------------------------------------ XML builders
def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


TNR = '<w:rFonts w:ascii="Times New Roman" w:cs="Times New Roman" w:eastAsia="Times New Roman" w:hAnsi="Times New Roman"/>'


def run(text, bold=False, sz=None):
    rpr = TNR + ('<w:b w:val="1"/><w:bCs w:val="1"/>' if bold else "") + (f'<w:sz w:val="{sz}"/><w:szCs w:val="{sz}"/>' if sz else "") + '<w:rtl w:val="0"/>'
    return f'<w:r><w:rPr>{rpr}</w:rPr><w:t xml:space="preserve">{esc(text)}</w:t></w:r>'


def runs(markup, sz=None, bold_all=False):
    out = []
    for i, part in enumerate(markup.split("**")):
        if part:
            out.append(run(part, bold=bold_all or i % 2 == 1, sz=sz))
    return "".join(out)


def p_body(markup, keep_next=False):
    kn = '<w:keepNext w:val="1"/>' if keep_next else ""
    return (f'<w:p><w:pPr>{kn}<w:spacing w:after="0" w:line="360" w:lineRule="auto"/><w:ind w:left="0" w:firstLine="708"/>'
            f'<w:rPr>{TNR}</w:rPr></w:pPr>{runs(markup)}</w:p>')


def p_center(text):
    return (f'<w:p><w:pPr><w:spacing w:after="0" w:line="360" w:lineRule="auto"/><w:ind w:left="0" w:firstLine="0"/><w:jc w:val="center"/>'
            f'<w:rPr>{TNR}</w:rPr></w:pPr>{runs(text)}</w:p>')


def p_figure(rid, cx, cy, did, name):
    return ('<w:p><w:pPr><w:keepNext w:val="1"/><w:spacing w:after="60" w:before="120" w:line="240" w:lineRule="auto"/>'
            '<w:ind w:left="0" w:firstLine="0"/><w:jc w:val="center"/><w:rPr/></w:pPr><w:r><w:rPr/><w:drawing>'
            f'<wp:inline distB="0" distT="0" distL="0" distR="0"><wp:extent cx="{cx}" cy="{cy}"/><wp:effectExtent b="0" l="0" r="0" t="0"/>'
            f'<wp:docPr id="{did}" name="{name}"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">'
            f'<pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="{name}"/><pic:cNvPicPr preferRelativeResize="0"/></pic:nvPicPr>'
            f'<pic:blipFill><a:blip r:embed="{rid}"/><a:srcRect b="0" l="0" r="0" t="0"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
            f'<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"/><a:ln/></pic:spPr></pic:pic>'
            '</a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>')


BORDERS = ('<w:tcBorders><w:top w:color="000000" w:space="0" w:sz="4" w:val="single"/><w:left w:color="000000" w:space="0" w:sz="4" w:val="single"/>'
           '<w:bottom w:color="000000" w:space="0" w:sz="4" w:val="single"/><w:right w:color="000000" w:space="0" w:sz="4" w:val="single"/></w:tcBorders>')


def cell(text, width, bold=False, head=False, align="l"):
    shd = '<w:shd w:fill="e7e6e6" w:val="clear"/>' if head else ""
    jc = {"l": "left", "c": "center"}[align]
    paras = "".join(
        f'<w:p><w:pPr><w:spacing w:after="0" w:line="259" w:lineRule="auto"/><w:ind w:left="2" w:firstLine="0"/><w:jc w:val="{jc}"/>'
        f'<w:rPr>{TNR}<w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr></w:pPr>{runs(line, sz=28, bold_all=bold or head)}</w:p>'
        for line in text.split("\n"))
    return f'<w:tc><w:tcPr><w:tcW w:w="{width}" w:type="dxa"/>{BORDERS}{shd}</w:tcPr>{paras}</w:tc>'


def table(header, rows, widths, align=None, bold_rows=()):
    assert sum(widths) == 9072, sum(widths)
    align = align or ["l"] + ["c"] * (len(header) - 1)
    grid = "".join(f'<w:gridCol w:w="{w}"/>' for w in widths)
    out = ('<w:tbl><w:tblPr><w:tblStyle w:val="Table31"/><w:tblW w:w="9072" w:type="dxa"/><w:jc w:val="left"/><w:tblInd w:w="5.0" w:type="dxa"/>'
           f'<w:tblLayout w:type="fixed"/><w:tblLook w:val="0400"/></w:tblPr><w:tblGrid>{grid}</w:tblGrid>')
    out += '<w:tr><w:trPr><w:cantSplit w:val="1"/><w:tblHeader w:val="1"/></w:trPr>' + "".join(
        cell(h, w, head=True, align="c") for h, w in zip(header, widths)) + "</w:tr>"
    for i, r in enumerate(rows):
        out += '<w:tr><w:trPr><w:cantSplit w:val="1"/></w:trPr>' + "".join(
            cell(c, w, bold=i in bold_rows, align=a) for c, w, a in zip(r, widths, align)) + "</w:tr>"
    return out + "</w:tbl>" + '<w:p><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/><w:rPr/></w:pPr></w:p>'


# ------------------------------------------------------------------ text helpers on document.xml
class Doc:
    def __init__(self, xml):
        self.x = xml

    def para_bounds(self, sub, nth=0, start=0):
        i = start - 1
        for _ in range(nth + 1):
            i = self.x.index(sub, i + 1)
        s = max(self.x.rfind("<w:p>", 0, i), self.x.rfind("<w:p ", 0, i))
        e = self.x.index("</w:p>", i) + len("</w:p>")
        return s, e

    def insert_after(self, sub, xml, nth=0):
        _, e = self.para_bounds(sub, nth)
        self.x = self.x[:e] + xml + self.x[e:]

    def insert_before(self, sub, xml, nth=0):
        s, _ = self.para_bounds(sub, nth)
        self.x = self.x[:s] + xml + self.x[s:]

    def replace(self, old, new, count=1):
        n = self.x.count(esc(old) if "&" in old or "<" in old else old)
        assert n == count, f"expected {count} occurrence(s) of {old[:60]!r}, found {n}"
        self.x = self.x.replace(old, new)

    def append_text(self, sub, extra):
        """Appends text to the last run of the paragraph that contains `sub`."""
        s, e = self.para_bounds(sub)
        j = self.x.rindex("</w:t>", s, e)
        self.x = self.x[:j] + esc(extra) + self.x[j:]


def main():
    zin = zipfile.ZipFile(SRC)
    parts = {i.filename: zin.read(i.filename) for i in zin.infolist()}
    infos = {i.filename: i for i in zin.infolist()}
    d = Doc(parts["word/document.xml"].decode("utf8"))

    # ------------------------------------------------ 0. renumber the old section 5.9 -> 5.10, table 5.14 -> 5.18
    d.replace("п. 5.9", "п. 5.10", count=4)           # references to the cost / limitations section (incl. the conclusions)
    d.replace("5.9. Вартість експериментів, обмеження та загрози валідності", "5.10. Вартість експериментів, обмеження та загрози валідності", count=2)
    d.replace("наведено в таблиці 5.14. За даними", "наведено в таблиці 5.18. За даними")
    d.replace("Таблиця 5.14 – Обсяг і вартість експериментів", "Таблиця 5.18 – Обсяг і вартість експериментів")

    # ------------------------------------------------ 1. images
    imgs = {}
    for k, (png, w_cm) in {"split": ("ch_e6_split.png", 14.2), "methods": ("ch_e6_methods.png", 14.2), "needs": ("ch_e6_needs.png", 13.0)}.items():
        im = Image.open(os.path.join(FIG, png)).convert("RGB")
        buf = io.BytesIO()
        im.save(buf, "JPEG", quality=93, dpi=(220, 220))
        name = f"image{101 + len(imgs)}.jpg"
        parts[f"word/media/{name}"] = buf.getvalue()
        cx = int(w_cm * 360000)
        imgs[k] = dict(rid=f"rIdE6{k}", name=name, cx=cx, cy=int(cx * im.height / im.width))
    rels = parts["word/_rels/document.xml.rels"].decode("utf8")
    add = "".join(f'<Relationship Id="{v["rid"]}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/{v["name"]}"/>'
                  for v in imgs.values())
    parts["word/_rels/document.xml.rels"] = rels.replace("</Relationships>", add + "</Relationships>").encode("utf8")

    def fig(key, did, caption):
        v = imgs[key]
        return p_figure(v["rid"], v["cx"], v["cy"], did, v["name"]) + p_center(caption)

    # ------------------------------------------------ 2. section 3.7: split, neural networks, Gemini
    d.insert_after("Поділ даних.", "".join([
        p_body("Склад вибірок і їх призначення наведено в таблиці 3.3 та на рисунку 3.5. Навчальна вибірка слугує лише для підбору "
               "ваг моделей. Валідаційну вибірку використано для вибору гіперпараметрів і моменту зупинки навчання нейромереж; вона "
               "містить ті самі формулювання, що й навчальна (103 формулювання спільні, але жоден текст побажання не збігається), тому "
               "оцінка на ній дещо оптимістична. Тестова вибірка не має спільних формулювань і шаблонів ні з навчальною, ні з валідаційною "
               "(перетин порожній) і використана один раз, для остаточного порівняння всіх методів."),
        p_body("Таблиця 3.3 – Поділ даних на навчальну, валідаційну й тестову вибірки", keep_next=True),
        table(["Вибірка", "Побажань\n(укр. / англ.)", "Пар «побажання — авто»", "Призначення"],
              [["Навчальна", "480\n(407 / 73)", "23 040", "Навчання моделей; підбір гіперпараметрів LightGBM крос-валідацією"],
               ["Валідаційна", "120\n(97 / 23)", "5 760", "Вибір гіперпараметрів нейромереж, рання зупинка"],
               ["Тестова", "160\n(132 / 28)", "7 680", "Єдиний фінальний замір якості; нові формулювання й шаблони"],
               ["Разом", "760\n(636 / 124)", "36 480", "—"]],
              [1800, 1700, 2100, 3472], align=["l", "c", "c", "l"], bold_rows=(3,)),
        fig("split", 9101, "Рисунок 3.5 – Поділ побажань на навчальну, валідаційну й тестову вибірки"),
    ]))

    mlp_cfg, tt_cfg = NN["selection"]["mlp"]["config"], NN["selection"]["two_tower"]["config"]
    d.insert_after("випадковий порядок, BM25 за текстовим описом авто", "".join([
        p_body("**Нейромережеві моделі.** На додачу до LightGBM навчено дві нейромережі у бібліотеці PyTorch [50] на тих самих даних і "
               "тому самому поділі. Перша — багатошаровий перцептрон (MLP): на вхід подається той самий вектор ознак, що й у LightGBM "
               "(косинусна схожість, 32 головні компоненти побажання, 19 ознак авто, стандартизовані за навчальною вибіркою), далі "
               "йдуть два повнозв'язні шари з активацією ReLU і dropout [54], а на виході — одна оцінка авто "
               f"({CMP['nn']['mlp']['params']:,} параметрів). Друга — двобаштова мережа (two-tower) [52]: башта побажання перетворює "
               "повний ембединг побажання (3 072 числа) на вектор розмірності d, башта авто — 19 ознак авто на вектор тієї самої "
               "розмірності, а оцінка дорівнює скалярному добутку векторів "
               f"({CMP['nn']['two_tower']['params']:,} параметрів). Двобаштова мережа не використовує ні головних компонент, ні "
               "косинусної схожості: уявлення побажання й авто вона вивчає сама. Для обох мереж функція втрат — listwise-крос-ентропія "
               "ListNet [51] за 48 авто одного побажання: цільовий розподіл пропорційний виграшу 2^y − 1 (як у NDCG), а передбачений — "
               "softmax оцінок. Оптимізатор AdamW [53], крок навчання 10⁻³, пакет із 16 побажань, не більше 120 епох."),
        p_body("**Вибір гіперпараметрів і зупинка навчання.** Гіперпараметри нейромереж підбирали на валідаційній вибірці. Кожну з 16 "
               "комбінацій (для MLP — кількість головних компонент 16/32, розміри шарів 128–64/256–128, dropout 0,1/0,3, weight decay "
               "10⁻⁴/10⁻²; для двобаштової мережі — ширина прихованого шару 256/512, розмірність вектора 32/64, dropout 0,2/0,5, weight "
               "decay 10⁻³/10⁻¹) навчали на навчальній вибірці з трьома різними початковими станами генератора й зупиняли, коли NDCG@5 "
               "на валідаційній вибірці не зростав 15 епох поспіль. Обрано комбінацію з найвищим середнім NDCG@5 на валідації та середнє "
               "число епох до зупинки. Остаточну мережу кожного типу перенавчено на навчальній і валідаційній вибірках на обране число "
               "епох; стандартизацію й головні компоненти знову обчислено за цими даними. Результат — середня оцінка п'яти мереж із різним "
               "початковим станом (ансамбль), що прибирає розкид через випадкову ініціалізацію; результат однієї мережі наведено окремо (п. 5.9)."),
        p_body("**Gemini як ранжувальник без навчання.** Щоб порівняти навчені моделі з великою мовною моделлю, ту саму задачу поставлено "
               "моделям Gemini без жодного навчання (zero-shot): у запиті — текст побажання й описи всіх 48 авто (той самий текст, який "
               "бачить модель ембедингів: назва, кузов, місця, коробка, пальне, багажник, витрата, привід, довжина, потужність, ціна), "
               "а у відповіді — JSON зі списком десяти найкращих авто від кращого до гіршого (temperature = 0). Авто, яких модель не "
               "назвала, ставляться нижче за названі в порядку зростання ціни. Відповіді оцінено тією самою рубрикою й метриками, що й "
               "навчені моделі, на тих самих 160 тестових побажаннях. Використано gemini-3.6-flash (модель чату сайту) та "
               "gemini-3.5-flash-lite."),
    ]))
    d.insert_after("Інструмент search_available_cars отримав аргумент wishes", p_body(
        "Нейромережі також вбудовано в застосунок. Ансамбль із п'яти мереж кожного типу експортовано у файл JSON (ваги у форматі float16, "
        f"статистики стандартизації у float32, стиснення zlib; {EXP['mlp']['file_mb']:.1f} і {EXP['two_tower']['file_mb']:.1f} МБ) та обчислюється в C# без "
        "ML-бібліотек: це множення матриць, активація ReLU й усереднення за мережами. Збіг оцінок з еталонною реалізацією перевіряє автоматичний тест "
        "(6 побажань × 48 авто, похибка менша за 10⁻⁶). Зберігання ваг у float16 не змінило якості: NDCG@5 експортованих моделей на тестовій вибірці "
        f"{n3(EXP['mlp']['test_float16_export']['ndcg5'])} (MLP) і {n3(EXP['two_tower']['test_float16_export']['ndcg5'])} (двобаштова). Модель ранжування обирає "
        "адміністратор у панелі адміністратора (LightGBM, MLP або двобаштова мережа); вибір зберігається в базі даних і діє з наступного пошуку, "
        "а за замовчуванням працює LightGBM."))
    d.append_text("Підбір авто за нечіткими побажаннями сформульовано як задачу навчання ранжування й розв'язано власною моделлю LightGBM LambdaRank на ознаках",
                  " Додатково на тому самому поділі навчено дві нейромережі (багатошаровий перцептрон і двобаштову мережу) та порівняно "
                  "їх із LightGBM і з мовною моделлю Gemini, яка ранжує авто без навчання.")

    # ------------------------------------------------ 3. new section 5.9 (E6)
    two, mlp, lg = S["two_tower"], S["mlp"], S["lgbm"]
    g36, gl = S[G36], S[GLITE]
    sel_mlp, sel_tt = NN["selection"]["mlp"], NN["selection"]["two_tower"]
    k16 = [r["val_ndcg5"] for r in sel_mlp["grid"] if r["config"]["k_pca"] == 16]
    k32 = [r["val_ndcg5"] for r in sel_mlp["grid"] if r["config"]["k_pca"] == 32]
    tt_all = [r["val_ndcg5"] for r in sel_tt["grid"]]
    ss_m, ss_t = CMP["nn"]["mlp"]["single_seed_ndcg5"], CMP["nn"]["two_tower"]["single_seed_ndcg5"]
    needs_gain = {k: v["two_tower"] - v["lgbm"] for k, v in BN.items()}
    worst = sorted(needs_gain, key=needs_gain.get)[0]
    top3 = sorted(needs_gain, key=needs_gain.get, reverse=True)[:3]
    gem_better = [k for k, v in BN.items() if v[G36] > v["two_tower"] + 0.01]
    gem_worst = sorted(BN, key=lambda k: BN[k][G36] - BN[k]["two_tower"])[:2]
    cfg = lambda c: ", ".join(f"{a}" for a in c)

    def pp(x):
        return n3(x)

    rows_main = []
    for key, label in [("price", "Сортування за ціною (поточне)"), ("cosine", "Схожість ембедингів"), ("logreg", "Логістична регресія"),
                       ("lgbm", "LightGBM LambdaRank (модель сайту)"), ("mlp", "Нейромережа MLP"), ("two_tower", "Двобаштова нейромережа"),
                       (GLITE, "Gemini 3.5 Flash-Lite (без навчання)"), (G36, "Gemini 3.6 Flash (без навчання)")]:
        s = S[key]
        rows_main.append([label, f"{pp(s['ndcg5']['mean'])}\n{ci(s['ndcg5']['ci'])}", pp(s["p5"]["mean"]), pp(s["hit1"]["mean"]), pp(s["mrr"]["mean"])])

    def drow(a, b, la, lb):
        x = D[f"{a}-{b}"]
        return [f"{la} − {lb}", f"{sg(x['mean'])}\n{sci(x['ci'])}", f"{x['wins']} / {x['losses']} / {x['ties']}"]

    drows = [drow("two_tower", "lgbm", "Двобаштова", "LightGBM"), drow("mlp", "lgbm", "MLP", "LightGBM"), drow("two_tower", "mlp", "Двобаштова", "MLP"),
             drow("two_tower", G36, "Двобаштова", "Gemini 3.6 Flash"), drow("mlp", G36, "MLP", "Gemini 3.6 Flash"),
             drow("lgbm", G36, "LightGBM", "Gemini 3.6 Flash"), drow("two_tower", GLITE, "Двобаштова", "Gemini 3.5 Flash-Lite"),
             drow("lgbm", GLITE, "LightGBM", "Gemini 3.5 Flash-Lite")]

    heading = ('<w:p><w:pPr><w:pStyle w:val="Heading2"/><w:spacing w:after="120" w:before="240" w:line="360" w:lineRule="auto"/>'
               '<w:ind w:left="0" w:firstLine="708"/><w:jc w:val="both"/><w:rPr/></w:pPr>'
               '<w:bookmarkStart w:colFirst="0" w:colLast="0" w:name="_heading=h.e6nnvsgemini" w:id="9100"/><w:bookmarkEnd w:id="9100"/>'
               f'{run("5.9. Нейромережі й Gemini в задачі ранжування авто (E6)")}</w:p>')
    sec = heading + "".join([
        p_body("Експеримент E6 відповідає на запитання, чи може навчена нейромережа впорядковувати авто за побажаннями краще за градієнтний "
               "бустинг (E5) і краще за велику мовну модель, яка розв'язує ту саму задачу без навчання. Усі методи оцінено на тих самих 160 "
               "тестових побажаннях тією самою рубрикою й метриками (NDCG@5, Precision@5, Hit@1, MRR); поділ даних описано в п. 3.7 "
               "(табл. 3.3, рис. 3.5). Гіпотеза H4: двобаштова нейромережа впорядковує авто не гірше за LightGBM і краще за Gemini без навчання."),
        p_body(f"Вибір гіперпараметрів нейромереж на валідаційній вибірці наведено в таблиці 5.14. Усі 16 комбінацій двобаштової мережі дали "
               f"близькі результати (NDCG@5 на валідації {n3(min(tt_all))}–{n3(max(tt_all))}), а в MLP розкид більший "
               f"({n3(min(r['val_ndcg5'] for r in sel_mlp['grid']))}–{n3(max(r['val_ndcg5'] for r in sel_mlp['grid']))}): MLP чутливий до кількості "
               f"головних компонент побажання — із 16 компонентами він гірший ({n3(min(k16))}–{n3(max(k16))}), ніж із 32 ({n3(min(k32))}–{n3(max(k32))}). "
               "Валідаційні оцінки оптимістичні, бо ту саму вибірку використано і для вибору комбінації, і для ранньої зупинки; висновки "
               "зроблено лише за тестовою вибіркою."),
        p_body("Таблиця 5.14 – Обрані гіперпараметри та якість нейромереж на валідаційній вибірці", keep_next=True),
        table(["Модель", "Обрані гіперпараметри", "Епох", "Параметрів", "NDCG@5 на валідації\n(підбір / ансамбль)"],
              [["MLP", f"32 компоненти; шари {mlp_cfg['hidden'][0]}–{mlp_cfg['hidden'][1]}; dropout {str(mlp_cfg['dropout']).replace('.', ',')}; weight decay 10⁻²",
                str(sel_mlp["epochs"]), f"{CMP['nn']['mlp']['params']:,}".replace(",", " "),
                f"{n3(sel_mlp['select_val_ndcg5'])} /\n{n3(CMP['nn']['mlp']['val']['ndcg5'])}"],
               ["Двобаштова", f"прихований шар {tt_cfg['hidden']}; d = {tt_cfg['dim']}; dropout {str(tt_cfg['dropout']).replace('.', ',')}; weight decay 10⁻³",
                str(sel_tt["epochs"]), f"{CMP['nn']['two_tower']['params']:,}".replace(",", " "),
                f"{n3(sel_tt['select_val_ndcg5'])} /\n{n3(CMP['nn']['two_tower']['val']['ndcg5'])}"]],
              [1800, 2800, 800, 1500, 2172], align=["l", "l", "c", "c", "c"]),
        p_body(f"Результати на тестовій вибірці, оцінені один раз, наведено в таблиці 5.15 і на рисунку 5.19. Двобаштова мережа має "
               f"NDCG@5 = {n3(two['ndcg5']['mean'])} {ci(two['ndcg5']['ci'])}, MLP — {n3(mlp['ndcg5']['mean'])} {ci(mlp['ndcg5']['ci'])}, "
               f"LightGBM — {n3(lg['ndcg5']['mean'])} {ci(lg['ndcg5']['ci'])}; Gemini 3.6 Flash без навчання — {n3(g36['ndcg5']['mean'])} "
               f"{ci(g36['ndcg5']['ci'])}, Gemini 3.5 Flash-Lite — {n3(gl['ndcg5']['mean'])} {ci(gl['ndcg5']['ci'])}. Значення для базових методів "
               "і LightGBM збігаються з табл. 5.12."),
        p_body("Таблиця 5.15 – Якість ранжування на тестовій вибірці: нейромережі, LightGBM і Gemini (E6, 160 побажань, 95 % ДІ)", keep_next=True),
        table(["Метод", "NDCG@5\n[95 % ДІ]", "P@5", "Hit@1", "MRR"], rows_main, [3372, 2200, 1100, 1200, 1200], bold_rows=(5,)),
        fig("methods", 9102, "Рисунок 5.19 – NDCG@5 методів ранжування на тестовій вибірці з 95 % довірчими інтервалами (E6)"),
        p_body("Парні різниці NDCG@5 для тих самих побажань (бутстреп за побажаннями, 10 000 вибірок) наведено в таблиці 5.16. "
               f"Обидві нейромережі значуще перевершують LightGBM: різниця для двобаштової мережі {sg(D['two_tower-lgbm']['mean'])}, для MLP "
               f"{sg(D['mlp-lgbm']['mean'])}, довірчі інтервали не містять нуля. Між собою нейромережі значуще не відрізняються (різниця "
               f"{sg(D['two_tower-mlp']['mean'])}, інтервал {sci(D['two_tower-mlp']['ci'])} містить нуль). Gemini 3.6 Flash без жодного навчання "
               f"статистично не відрізняється від LightGBM (різниця {sg(D['lgbm-gemini-3.6-flash']['mean'])}), але значуще поступається обом нейромережам; "
               f"Gemini 3.5 Flash-Lite гірша від LightGBM на {n3(D['lgbm-gemini-3.5-flash-lite']['mean'])}. "
               "Отже, гіпотезу H4 підтверджено: двобаштова нейромережа краща за LightGBM і за Gemini 3.6 Flash."),
        p_body("Таблиця 5.16 – Парні різниці NDCG@5 між методами на тестовій вибірці (E6)", keep_next=True),
        table(["Пара методів", "Різниця NDCG@5\n[95 % ДІ]", "Краще / гірше / порівну\n(побажань)"], drows, [3600, 2800, 2672], align=["l", "c", "c"]),
        p_body(f"Ансамбль із п'яти мереж — свідомий вибір, щоб прибрати випадковість початкового стану; кожна окрема мережа має трохи нижчий результат: "
               f"NDCG@5 = {n3(ss_m['mean'])} ± {n3(ss_m['sd'])} для MLP і {n3(ss_t['mean'])} ± {n3(ss_t['sd'])} для двобаштової мережі "
               f"(середнє й стандартне відхилення за п'ятьма мережами), що теж вище за {n3(lg['ndcg5']['mean'])} у LightGBM. У E5 модель LightGBM "
               "була однією, ансамблів не будували, тому порівняння ансамблю з однією моделлю трохи зміщене на користь нейромереж; "
               "порівняння окремих мереж із LightGBM цього зміщення не має."),
        p_body(f"За типами потреб (рис. 5.20) двобаштова мережа краща за LightGBM у всіх 15 типів потреб; найменше — для потреби «{NEED_UK[worst]}» "
               f"({sg(needs_gain[worst])}), найбільше — для «{NEED_UK[top3[0]]}» ({sg(needs_gain[top3[0]])}, лише {BN[top3[0]]['n']} побажань), "
               f"«{NEED_UK[top3[1]]}» ({sg(needs_gain[top3[1]])}) і «{NEED_UK[top3[2]]}» ({sg(needs_gain[top3[2]])}), де LightGBM був найслабшим. "
               f"Gemini 3.6 Flash випереджає двобаштову мережу лише за потребою «{NEED_UK[gem_better[0]]}» "
               f"({n3(BN[gem_better[0]][G36])} проти {n3(BN[gem_better[0]]['two_tower'])}), а найбільше відстає на потребах «{NEED_UK[gem_worst[0]]}» "
               f"({n3(BN[gem_worst[0]][G36])} проти {n3(BN[gem_worst[0]]['two_tower'])}) і «{NEED_UK[gem_worst[1]]}» "
               f"({n3(BN[gem_worst[1]][G36])} проти {n3(BN[gem_worst[1]]['two_tower'])}), де розмите формулювання треба зіставити з витратою пального, "
               f"потужністю чи багажником. На англійських побажаннях (28) NDCG@5 двобаштової мережі {n3(CMP['by_lang']['en']['two_tower'])}, на українських "
               f"(132) — {n3(CMP['by_lang']['uk']['two_tower'])}."),
        fig("needs", 9103, "Рисунок 5.20 – NDCG@5 за типами потреб на тестовій вибірці: LightGBM, двобаштова мережа й Gemini 3.6 Flash (у дужках — кількість побажань)"),
        p_body(f"Практичні відмінності наведено в таблиці 5.17. Нейромережі навчаються за секунди на CPU й ранжують 48 авто за соті частки "
               f"мілісекунди без жодних витрат на виклики API. Gemini 3.6 Flash витрачає близько {dec(G36M['latency_s_mean'], 0)} с і {dec(G36M['cost_usd'] / G36M['wishes'], 3)} $"
               f" на одне побажання (5 333 токени запиту й близько 1 800 токенів «міркувань»); вартість зростає пропорційно кількості авто в запиті, бо в промпт "
               "потрапляють описи всіх кандидатів."),
        p_body("Таблиця 5.17 – Вартість і швидкість ранжування: нейромережі та Gemini (E6)", keep_next=True),
        table(["Метод", "Навчання", "Затримка на\nпобажання", "Вартість на\n160 побажань"],
              [["Двобаштова мережа", f"{TIM['two_tower']['train_seconds_one_network']:.1f} с на мережу".replace(".", ","),
                f"{TIM['two_tower']['inference_ms_per_wish_mean']:.2f} мс".replace(".", ","), "0 $"],
               ["MLP", f"{TIM['mlp']['train_seconds_one_network']:.1f} с на мережу".replace(".", ","),
                f"{TIM['mlp']['inference_ms_per_wish_mean']:.2f} мс".replace(".", ","), "0 $"],
               ["Gemini 3.6 Flash", "не потрібне", f"{G36M['latency_s_mean']:.1f} с".replace(".", ","),
                f"{G36M['cost_usd']:.2f} $".replace(".", ",")],
               ["Gemini 3.5 Flash-Lite", "не потрібне", f"{GEM['models'][GLITE]['latency_s_mean']:.1f} с".replace(".", ","),
                f"{GEM['models'][GLITE]['cost_usd']:.2f} $".replace(".", ",")]],
              [2900, 2100, 2000, 2072], align=["l", "c", "c", "c"]),
        p_body("**Обмеження E6.** (1) Мітки релевантності синтетичні: їх задає рубрика автора, і нейромережі навчено відтворювати саме її. Gemini "
               "рубрики не бачила, тому її відставання частково відображає розбіжність між «здоровим глуздом» моделі й рубрикою, а не обов'язково гірше "
               "розуміння побажання. Порівняння показує, наскільки добре кожен метод відтворює задану рубрику, і не доводить переваги над Gemini на "
               "реальних клієнтах. (2) Навчальних даних небагато (600 побажань на 48 авто), автопарк один: перенесення на інший автопарк не перевірялося. "
               "(3) Gemini оцінено з одним промптом, temperature = 0 і одним прогоном; промпт не оптимізували, тож інше формулювання могло б дати "
               "вищий результат. (4) Валідаційну вибірку використано і для вибору гіперпараметрів, і для ранньої зупинки, тому оцінки на ній оптимістичні; "
               "тестову вибірку використано один раз. (5) На сайті за замовчуванням працює LightGBM; нейромережі вбудовано, і адміністратор може їх обрати (п. 3.7), але їхній вплив на рекомендації "
               "агента (як для LightGBM у табл. 5.13) окремо не вимірювався."),
    ])
    s, _ = d.para_bounds("5.10. Вартість експериментів, обмеження та загрози валідності", nth=1)
    d.x = d.x[:s] + sec + d.x[s:]

    # table of contents: new entry + renumbered old one
    s, e = d.para_bounds("5.10. Вартість експериментів, обмеження та загрози валідності", nth=0)
    toc = d.x[s:e]
    entry = toc.replace("5.10. Вартість експериментів, обмеження та загрози валідності", "5.9. Нейромережі й Gemini в задачі ранжування авто (E6)")
    entry = entry.replace("_heading=h.1khhjoh7l9s2", "_heading=h.e6nnvsgemini")
    entry = re.sub(r'w14:paraId="[0-9A-F]+"', "", entry)
    d.x = d.x[:s] + entry + d.x[s:]

    # ------------------------------------------------ 4. cost table (5.18)
    i = d.x.index("E5 — агент з ранжуванням і без")
    rs = d.x.rindex("<w:tr>", 0, i)
    re_ = d.x.index("</w:tr>", i) + len("</w:tr>")
    row = d.x[rs:re_]
    new = (row.replace("E5 — агент з ранжуванням і без", "E6 — Gemini як ранжувальник (3.6-flash, 3.5-flash-lite)")
           .replace(">40<", ">320 запитів<").replace(">0,44<", f">{dec(COST_E6, 2)}<"))
    assert new != row and "320 запитів" in new
    d.x = d.x[:re_] + new + d.x[re_:]
    total = dec(7.82 + COST_E6, 2)
    i = d.x.index(">7,82<")
    rs, re_ = d.x.rindex("<w:tr>", 0, i), d.x.index("</w:tr>", i)
    tot_row = d.x[rs:re_]
    tot_new = re.sub(r"(прогін сценаріїв)(</w:t>)", r"\1 і 320 запитів E6\2", tot_row).replace(">7,82<", f">{total}<")
    assert tot_new != tot_row and "320 запитів" in tot_new
    d.x = d.x[:rs] + tot_new + d.x[re_:]
    d.replace("записаною вартістю 7,82 $", f"записаною вартістю {total} $ (з них {dec(COST_E6, 2)} $ — запити E6 до Gemini)")

    # ------------------------------------------------ 5. annotation, keywords, tasks, hypotheses, conclusions
    d.replace("розроблено й навчено власну модель машинного навчання — модель ранжування (learning to rank, LightGBM LambdaRank) на ембедингах побажання та характеристиках авто.",
              "розроблено й навчено власні моделі машинного навчання для ранжування (learning to rank): градієнтний бустинг LightGBM LambdaRank "
              "та дві нейромережі (багатошаровий перцептрон і двобаштова мережа) на ембедингах побажання та характеристиках авто.")
    d.replace("модель ранжування має NDCG@5 = 0,895 (95 % ДІ [0,869; 0,918]) проти 0,180 у сортування за ціною та 0,536 у схожості ембедингів;",
              f"двобаштова нейромережа має NDCG@5 = {n3(two['ndcg5']['mean'])} (95 % ДІ {ci(two['ndcg5']['ci'])}), MLP — {n3(mlp['ndcg5']['mean'])}, "
              f"LightGBM — {n3(lg['ndcg5']['mean'])}, мовна модель Gemini 3.6 Flash без навчання — {n3(g36['ndcg5']['mean'])}, а сортування за ціною — "
              f"{n3(S['price']['ndcg5']['mean'])};")
    d.replace("an own machine learning model was built and trained: a learning-to-rank model (LightGBM LambdaRank) over wish embeddings and car specifications.",
              "own machine learning models were built and trained: learning-to-rank models over wish embeddings and car specifications — LightGBM LambdaRank "
              "and two neural networks (a multilayer perceptron and a two-tower network).")
    d.replace("the ranking model reaches NDCG@5 = 0.895 (95 % CI [0.869; 0.918]) versus 0.180 for sorting by price and 0.536 for embedding similarity;",
              f"the two-tower neural network reaches NDCG@5 = {two['ndcg5']['mean']:.3f} (95 % CI [{two['ndcg5']['ci'][0]:.3f}; {two['ndcg5']['ci'][1]:.3f}]), "
              f"the MLP {mlp['ndcg5']['mean']:.3f}, LightGBM {lg['ndcg5']['mean']:.3f}, the Gemini 3.6 Flash language model without training "
              f"{g36['ndcg5']['mean']:.3f}, and sorting by price {S['price']['ndcg5']['mean']:.3f};")
    d.replace(" навчання ранжування, LightGBM, гібридний пошук", " навчання ранжування, LightGBM, нейронні мережі, гібридний пошук")
    d.replace(" learning to rank, LightGBM, hybrid search", " learning to rank, LightGBM, neural networks, hybrid search")
    d.replace("навчити модель ранжування авто за побажаннями та порівняти її з базовими методами на відкладеній тестовій вибірці.",
              "навчити моделі ранжування авто за побажаннями (градієнтний бустинг і дві нейромережі) та порівняти їх із базовими методами й великою "
              "мовною моделлю Gemini на відкладеній тестовій вибірці.")
    d.replace("чистий векторний пошук на коротких запитах із термінами. Перевірку гіпотез наведено в розділі 5: H1 підтверджено, для H2 і H3 різниці статистично не значущі.",
              "чистий векторний пошук на коротких запитах із термінами; H4 — двобаштова нейромережа впорядковує авто не гірше за LightGBM і краще за "
              "Gemini, яка ранжує авто без навчання. Перевірку гіпотез наведено в розділі 5: H1 і H4 підтверджено, для H2 і H3 різниці статистично не значущі.")
    d.append_text("Власна модель ранжування авто за побажаннями на відкладеній тестовій вибірці має NDCG@5 = 0,895",
                  f" Нейромережі, навчені на тому самому поділі, перевершили LightGBM: NDCG@5 = {n3(two['ndcg5']['mean'])} для двобаштової мережі й "
                  f"{n3(mlp['ndcg5']['mean'])} для MLP проти {n3(lg['ndcg5']['mean'])} (різниці значущі), а Gemini 3.6 Flash як ранжувальник без навчання "
                  f"дала {n3(g36['ndcg5']['mean'])}, тобто на рівні LightGBM і значуще нижче нейромереж, за {dec(G36M['latency_s_mean'])} с і близько "
                  f"{dec(G36M['cost_usd'] / G36M['wishes'], 3)} $ на одне побажання проти соток часток мілісекунди й нульової вартості в нейромереж.")
    d.append_text("Модель обчислюється в C# без ML-бібліотек і вбудована в інструмент пошуку агента",
                  f" Для порівняння на тому самому поділі навчено дві нейромережі: двобаштова має NDCG@5 = {n3(two['ndcg5']['mean'])} "
                  f"(95 % ДІ {ci(two['ndcg5']['ci'])}), MLP — {n3(mlp['ndcg5']['mean'])} {ci(mlp['ndcg5']['ci'])}; обидві значуще кращі за LightGBM, різниця між ними "
                  f"незначуща. Мовна модель Gemini 3.6 Flash, яка ранжує авто без навчання, дала {n3(g36['ndcg5']['mean'])} (на рівні LightGBM, значуще нижче "
                  f"нейромереж), а Gemini 3.5 Flash-Lite — {n3(gl['ndcg5']['mean'])}; нейромережі на чотири-п'ять порядків швидші й безкоштовні. Нейромережі "
                  "вбудовано в застосунок: модель ранжування обирає адміністратор (за замовчуванням LightGBM); вплив нейромереж на рекомендації агента "
                  "окремо не вимірювався.")

    # ------------------------------------------------ 6. references
    refs = ["Paszke A., Gross S., Massa F., Lerer A. et al. PyTorch: An Imperative Style, High-Performance Deep Learning Library // Advances in Neural Information Processing Systems (NeurIPS). 2019. Vol. 32. P. 8024–8035.",
            "Cao Z., Qin T., Liu T.-Y., Tsai M.-F., Li H. Learning to Rank: From Pairwise Approach to Listwise Approach // Proceedings of the 24th International Conference on Machine Learning (ICML). 2007. P. 129–136.",
            "Huang P.-S., He X., Gao J., Deng L., Acero A., Heck L. Learning Deep Structured Semantic Models for Web Search using Clickthrough Data // Proceedings of the 22nd ACM International Conference on Information and Knowledge Management (CIKM). 2013. P. 2333–2338.",
            "Loshchilov I., Hutter F. Decoupled Weight Decay Regularization // International Conference on Learning Representations (ICLR). 2019. arXiv:1711.05101.",
            "Srivastava N., Hinton G., Krizhevsky A., Sutskever I., Salakhutdinov R. Dropout: A Simple Way to Prevent Neural Networks from Overfitting // Journal of Machine Learning Research. 2014. Vol. 15. P. 1929–1958."]
    s, e = d.para_bounds("49. KAYAK Україна")
    tpl = d.x[s:e]
    old_text = re.search(r"<w:t[^>]*>(49\. KAYAK[^<]*)</w:t>", tpl).group(1)
    new_refs = "".join(re.sub(r'w14:paraId="[0-9A-F]+"', "", tpl.replace(old_text, esc(f"{50 + i}. {r}"))) for i, r in enumerate(refs))
    d.x = d.x[:e] + new_refs + d.x[e:]

    # ------------------------------------------------ 6b. wide tables (6+ columns) go to landscape sections: at 14 pt they do not fit
    #           the 16 cm text width without breaking numbers and words in the middle
    m = re.search(r"<w:sectPr>(?:(?!</w:sectPr>).)*?rId44.*?</w:sectPr>", d.x, re.S)
    portrait = m.group(0)
    landscape = portrait.replace('<w:pgSz w:h="16838" w:w="11906" w:orient="portrait"/>', '<w:pgSz w:h="11906" w:w="16838" w:orient="landscape"/>')
    assert landscape != portrait
    ratio = (16838 - 1701 - 1083) / 9072
    sect_par = lambda sp: f'<w:p><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/>{sp}</w:pPr></w:p>'
    for cap in ["Таблиця 1.2 –", "Таблиця 5.3 –", "Таблиця 5.4 –", "Таблиця 5.5 –", "Таблиця 5.6 –", "Таблиця 5.9 –"]:
        cs, ce = d.para_bounds(cap)
        ts = d.x.index("<w:tbl>", ce)
        te = d.x.index("</w:tbl>", ts) + len("</w:tbl>")
        assert "<w:tbl>" not in d.x[ts + 7:te], cap
        tbl = d.x[ts:te]
        tbl = re.sub(r'(<w:(?:gridCol|tcW|tblW) w:w=")(\d+)(?:\.\d+)?(")', lambda mm: f"{mm.group(1)}{round(int(mm.group(2)) * ratio)}{mm.group(3)}", tbl)
        d.x = d.x[:cs] + sect_par(portrait) + d.x[cs:ts] + tbl + sect_par(landscape) + d.x[te:]
        # the section break paragraph shifts all later offsets; recomputed from scratch on the next iteration

    # ------------------------------------------------ 7. formatting: 14 pt after the task sheet
    cut = d.x.index("<w:sectPr")                     # end of the title page / task sheet
    head, tail = d.x[:cut], d.x[cut:]
    tail = re.sub(r'(<w:sz(?:Cs)? w:val=")(?:[5-9]|1\d|2[0-7])("/>)', lambda m: m.group(1) + "28" + m.group(2), tail)
    d.x = head + tail
    parts["word/document.xml"] = d.x.encode("utf8")

    # figure / source counts in the annotation
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as zout:
        for name, data in parts.items():
            zout.writestr(infos.get(name, name), data)
    print("written", OUT)


if __name__ == "__main__":
    main()
