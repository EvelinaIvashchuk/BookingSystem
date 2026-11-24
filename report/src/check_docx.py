#!/usr/bin/env python3
"""Checks the supervisor's formatting requirements on the finished report.

Usage: tools/.venv/bin/python report/src/check_docx.py [path.docx]
  1. 14 pt: every text run after the task sheet must have an effective size of 14 pt (28 half-points). The effective size is the
     run's own w:sz, else the paragraph style's (following basedOn), else the document default.
  2. Table captions: every caption «Таблиця N.M – ...» must stand directly above its table (no other non-empty paragraph in
     between) and no caption may stand directly below a table.
"""
import re
import sys
import zipfile
from collections import Counter

path = sys.argv[1] if len(sys.argv) > 1 else "report/02_Звіт_Іващук_ЕВ_444_нейромережі.docx"
z = zipfile.ZipFile(path)
x = z.read("word/document.xml").decode("utf8")
st = z.read("word/styles.xml").decode("utf8")

# ---- styles: size per style id (with basedOn)
styles = {}
for m in re.finditer(r'<w:style [^>]*?w:styleId="([^"]+)"[^>]*>(.*?)</w:style>', st, re.S):
    body = m.group(2)
    sz = re.search(r'<w:rPr>.*?<w:sz w:val="(\d+)"', body, re.S)
    based = re.search(r'<w:basedOn w:val="([^"]+)"', body)
    styles[m.group(1)] = (int(sz.group(1)) if sz else None, based.group(1) if based else None)
default_sz = int(re.search(r"<w:docDefaults>.*?<w:sz w:val=\"(\d+)\"", st, re.S).group(1))


def style_size(sid):
    seen = set()
    while sid and sid not in seen and sid in styles:
        seen.add(sid)
        sz, sid = styles[sid]
        if sz:
            return sz
    return default_sz


cut = x.index("<w:sectPr")                       # end of the task sheet
body = x[cut:]
bad, total = Counter(), 0
examples = {}
for pm in re.finditer(r"<w:p[ >].*?</w:p>", body, re.S):
    p = pm.group(0)
    ps = re.search(r'<w:pStyle w:val="([^"]+)"', p)
    base = style_size(ps.group(1) if ps else None)
    for rm in re.finditer(r"<w:r[ >].*?</w:r>", p, re.S):
        r = rm.group(0)
        t = "".join(re.findall(r"<w:t[^>]*>([^<]*)</w:t>", r))
        if not t.strip():
            continue
        sz = re.search(r"<w:sz w:val=\"(\d+)\"", r)
        eff = int(sz.group(1)) if sz else base
        total += 1
        if eff != 28:
            bad[eff] += 1
            examples.setdefault(eff, []).append(t[:50])
print(f"text runs after the task sheet: {total}; not 14 pt: {sum(bad.values())} {dict(bad)}")
for k, v in examples.items():
    print(f"  {k / 2} pt, e.g.: {v[:6]}")

# ---- captions
seq = []
for m in re.finditer(r"<w:tbl>.*?</w:tbl>|<w:p[ >].*?</w:p>", x, re.S):
    t = m.group(0)
    if t.startswith("<w:tbl>"):
        seq.append(("TBL", ""))
    else:
        txt = "".join(re.findall(r"<w:t[^>]*>([^<]*)</w:t>", t)).strip()
        if txt or "<w:drawing>" in t:
            seq.append(("P", txt))
cap = re.compile(r"^Таблиця \d+\.\d+ –")
issues = 0
n_caps = 0
for i, (k, t) in enumerate(seq):
    if k == "P" and cap.match(t):
        n_caps += 1
        if not (i + 1 < len(seq) and seq[i + 1][0] == "TBL"):
            issues += 1
            print("caption without a table right below:", t[:70])
        if i > 0 and seq[i - 1][0] == "TBL":
            issues += 1
            print("caption right below a table:", t[:70])
n_tbl_with_cap = sum(1 for i, (k, t) in enumerate(seq) if k == "TBL" and i > 0 and cap.match(seq[i - 1][1]))
print(f"table captions: {n_caps}, tables with a caption directly above: {n_tbl_with_cap}, issues: {issues}")
nums = [re.match(r"^Таблиця (\d+\.\d+)", t).group(1) for k, t in seq if k == "P" and cap.match(t)]
print("caption numbers:", nums)
print("duplicates:", [n for n, c in Counter(nums).items() if c > 1])
