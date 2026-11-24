#!/usr/bin/env python3
"""Refreshes the page numbers in the (static) table of contents of the Google Docs export and the page / figure / source counts
of the annotation, using a PDF rendering of the same file.

Usage:
    soffice --headless --convert-to pdf --outdir <dir> report/02_...docx
    tools/.venv/bin/python report/src/toc_pages.py report/02_...docx <dir>/02_....pdf [--write]

Without --write it only compares the numbers found in the PDF with those stored in the document (used to validate the method on
the original file). PDF page = displayed page + 1 (the title page is not numbered). Word may paginate slightly differently from
LibreOffice, so after opening the file in Word update the numbers once more by hand if a heading moved.
"""
import re
import subprocess
import sys
import zipfile

doc, pdf = sys.argv[1], sys.argv[2]
write = "--write" in sys.argv
z = zipfile.ZipFile(doc)
x = z.read("word/document.xml").decode("utf8")

n_pages = int(re.search(r"Pages:\s+(\d+)", subprocess.run(["pdfinfo", pdf], capture_output=True, text=True).stdout).group(1))
pages = [subprocess.run(["pdftotext", "-f", str(p), "-l", str(p), pdf, "-"], capture_output=True, text=True).stdout for p in range(1, n_pages + 1)]
norm = lambda s: re.sub(r"\s+", " ", s).strip()

# two markups occur in the Google Docs export: "title<tab>number" in one run, and the closing entries (ВИСНОВКИ, ДОДАТКИ, ...)
# where the title and the "<tab>number" sit in two neighbouring hyperlinks
patA = re.compile(r'<w:t[^>]*>([^<]{3,220})</w:t><w:tab/><w:t[^>]*>(\d+)</w:t>')
patB = re.compile(r'<w:t[^>]*>([^<]{3,120})</w:t></w:r></w:hyperlink><w:hyperlink[^>]*>(?:(?!</w:hyperlink>).)*?<w:tab/><w:t[^>]*>(\d+)</w:t>', re.S)
found_entries = {}
for pat in (patA, patB):
    for m in pat.finditer(x):
        found_entries[m.start(2)] = (norm(m.group(1)), m.start(2), m.end(2), m.group(2))
entries = sorted(found_entries.values(), key=lambda t: t[1])
# the TOC ends where the introduction starts: first page whose text has a line equal to "ВСТУП" after the TOC pages
intro = next(i for i, t in enumerate(pages) if i > 3 and any(norm(l) == "ВСТУП" for l in t.splitlines()) and "ЗМІСТ" not in t)
cur, changes = intro, 0
res = []
for title, s_, e_, old in entries:
    key = title[:30] if not title.startswith("РОЗДІЛ") else title[:10]
    found = None
    for p in range(cur, n_pages):
        if any(norm(l).startswith(key) for l in pages[p].splitlines()):
            found = p
            break
    if found is None:
        print("NOT FOUND:", title)
        res.append(old)
        continue
    cur = found
    shown = str(found)           # displayed number = PDF page (1-based) - 1 = index (0-based)
    res.append(shown)
    if shown != old:
        changes += 1
        print(f"{title[:60]:60s} doc {old:>4s} -> pdf {shown:>4s}")
print(f"{len(entries)} entries, {changes} differ")

if write:
    x2 = x
    for (title, s_, e_, old), shown in sorted(zip(entries, res), key=lambda t: -t[0][1]):
        x2 = x2[:s_] + shown + x2[e_:]
    figs = len(set(re.findall(r"Рисунок \d+\.\d+ –", re.sub(r"<[^>]+>", "", x2))))
    paras = ["".join(re.findall(r"<w:t[^>]*>([^<]*)</w:t>", pp)) for pp in re.findall(r"<w:p[ >].*?</w:p>", x2, re.S)]
    start = max(i for i, t in enumerate(paras) if t.strip() == "СПИСОК ВИКОРИСТАНИХ ДЖЕРЕЛ")
    refs = 0
    for t in paras[start + 1:]:
        if t.strip().startswith("ДОДАТКИ"):
            break
        m = re.match(r"^(\d+)\. ", t.strip())
        if m:
            refs = max(refs, int(m.group(1)))
    print("pages", n_pages, "figures", figs, "references", refs)
    for old, new in ((r"Сторінок – \d+, рисунків – \d+, джерел літератури – \d+", f"Сторінок – {n_pages}, рисунків – {figs}, джерел літератури – {refs}"),
                     (r"Pages – \d+, figures – \d+, references – \d+", f"Pages – {n_pages}, figures – {figs}, references – {refs}")):
        x2, n = re.subn(old, new, x2)
        assert n == 1, old
    tmp = doc + ".tmp"
    with zipfile.ZipFile(doc) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for i in zin.infolist():
            zout.writestr(i, x2.encode("utf8") if i.filename == "word/document.xml" else zin.read(i.filename))
    import os
    os.replace(tmp, doc)
    print("updated", doc)
