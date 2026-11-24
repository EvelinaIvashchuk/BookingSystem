// Допоміжні функції для генерації пояснювальної записки (docx-js).
// Контент описується блоками; нумерація рисунків/таблиць/формул — за розділами, посилання {fig:key}, {tab:key}, {eq:key}.
const fs = require("fs");
const path = require("path");
const { createRequire } = require("module");

function loadDocx() {
  const candidates = [process.env.DOCX_MODULE_DIR, __dirname, process.cwd()].filter(Boolean);
  for (const dir of candidates) {
    try { return createRequire(path.join(dir, "x.js"))("docx"); } catch { /* next */ }
  }
  throw new Error("docx module not found: set DOCX_MODULE_DIR to a folder with node_modules/docx");
}
const D = loadDocx();
const {
  Paragraph, TextRun, ImageRun, Table, TableRow, TableCell, WidthType, AlignmentType, BorderStyle,
  ShadingType, TabStopType, LevelFormat, PageBreak, VerticalAlign, HeadingLevel,
} = D;

const FIG_DIR = path.join(__dirname, "..", "figures");
const FONT = "Times New Roman";
const MONO = "Courier New";
const TEXT_W = 9071;          // ширина тексту, DXA (A4, поля 30/20 мм)
const TEXT_W_PX = 600;        // та сама ширина в пікселях (96 dpi) для зображень

// ------------------------------------------------------------------ inline-розмітка: **жирний**, *курсив*, `код`
function runs(text, base = {}) {
  const out = [];
  if (String(text).includes("{PAGES}")) {
    const [before, after] = String(text).split("{PAGES}");
    return [...runs(before, base), new TextRun({ ...base, children: [D.PageNumber.TOTAL_PAGES] }), ...runs(after, base)];
  }
  const parts = String(text).split(/(\*\*[^*]+\*\*|\*[^*\s][^*]*\*|`[^`]+`)/);
  for (const part of parts) {
    if (!part) continue;
    if (part.startsWith("**")) out.push(new TextRun({ ...base, text: part.slice(2, -2), bold: true }));
    else if (part.startsWith("`")) out.push(new TextRun({ ...base, text: part.slice(1, -1), font: MONO, size: Math.round((base.size || 28) * 0.86) }));
    else if (part.startsWith("*") && part.endsWith("*") && part.length > 2) out.push(new TextRun({ ...base, text: part.slice(1, -1), italics: true }));
    else out.push(new TextRun({ ...base, text: part }));
  }
  return out;
}

// ------------------------------------------------------------------ розміри зображень
function imageSize(file) {
  const b = fs.readFileSync(file);
  if (b[0] === 0x89 && b[1] === 0x50) return { w: b.readUInt32BE(16), h: b.readUInt32BE(20), type: "png", data: b };
  let i = 2;
  while (i < b.length) {
    if (b[i] !== 0xff) { i++; continue; }
    const marker = b[i + 1];
    const len = b.readUInt16BE(i + 2);
    if (marker >= 0xc0 && marker <= 0xcf && ![0xc4, 0xc8, 0xcc].includes(marker))
      return { h: b.readUInt16BE(i + 5), w: b.readUInt16BE(i + 7), type: "jpg", data: b };
    i += 2 + len;
  }
  throw new Error("unknown image " + file);
}

// ------------------------------------------------------------------ збирач блоків
class Report {
  constructor() {
    this.blocks = [];
    this.chapter = 0;
    this.listInstance = 0;
    this.counts = { fig: 0, tab: 0, refs: 0, app: 0 };
  }
  // структура
  h1(text, opts = {}) { this.blocks.push({ k: "h1", text, chapter: opts.chapter, appendix: opts.appendix, toc: opts.toc !== false, pageBreak: opts.pageBreak !== false }); }
  h2(text) { this.blocks.push({ k: "h2", text }); }
  h3(text) { this.blocks.push({ k: "h3", text }); }
  sub(text) { this.blocks.push({ k: "sub", text }); }          // жирний підзаголовок без TOC
  p(text, opts = {}) { this.blocks.push({ k: "p", text, ...opts }); }
  center(text, opts = {}) { this.blocks.push({ k: "p", text, align: "center", indent: false, ...opts }); }
  bullets(items) { this.blocks.push({ k: "list", items, kind: "dash", inst: ++this.listInstance }); }
  numbered(items) { this.blocks.push({ k: "list", items, kind: "num", inst: ++this.listInstance }); }
  fig(file, caption, opts = {}) { this.blocks.push({ k: "fig", file, caption, ...opts }); }
  figs(items, caption, opts = {}) { this.blocks.push({ k: "figs", items, caption, ...opts }); }
  table(caption, header, rows, widths, opts = {}) { this.blocks.push({ k: "table", caption, header, rows, widths, ...opts }); }
  code(text, opts = {}) { this.blocks.push({ k: "code", text, ...opts }); }
  formula(text, key) { this.blocks.push({ k: "eq", text, key }); }
  pageBreak() { this.blocks.push({ k: "pb" }); }
  raw(fn) { this.blocks.push({ k: "raw", fn }); }

  // нумерація і підстановка посилань
  number() {
    const map = {};
    let ch = 0, fig = 0, tab = 0, eq = 0, app = null;
    for (const b of this.blocks) {
      if (b.k === "h1" && b.chapter) { ch = b.chapter; fig = tab = eq = 0; app = null; }
      if (b.k === "h1" && b.appendix) { app = b.appendix; fig = tab = eq = 0; }
      const prefix = app || String(ch);
      if (b.k === "fig" || b.k === "figs") { b.num = `${prefix}.${++fig}`; this.counts.fig++; if (b.key) map["fig:" + b.key] = b.num; }
      if (b.k === "table" && !b.nonum) { b.num = `${prefix}.${++tab}`; this.counts.tab++; if (b.key) map["tab:" + b.key] = b.num; }
      if (b.k === "eq") { b.num = `${prefix}.${++eq}`; if (b.key) map["eq:" + b.key] = b.num; }
    }
    const sub = (s) => typeof s === "string" ? s.replace(/\{((?:fig|tab|eq):[\w-]+)\}/g, (m, k) => {
      if (!(k in map)) throw new Error("unknown ref " + k);
      return map[k];
    }) : s;
    for (const b of this.blocks) {
      for (const f of ["text", "caption"]) if (b[f]) b[f] = sub(b[f]);
      if (b.items) b.items = b.items.map((it) => (typeof it === "string" ? sub(it) : it));
      if (b.rows) b.rows = b.rows.map((r) => r.map(sub));
    }
    return map;
  }

  // ------------------------------------------------------------------ рендер
  render() {
    const out = [];
    for (const b of this.blocks) {
      switch (b.k) {
        case "h1":
          out.push(new Paragraph({
            heading: b.toc ? HeadingLevel.HEADING_1 : undefined, style: b.toc ? undefined : "H1NoToc",
            pageBreakBefore: b.pageBreak, children: runs(b.text),
          }));
          break;
        case "h2": out.push(new Paragraph({ heading: HeadingLevel.HEADING_2, children: runs(b.text) })); break;
        case "h3": out.push(new Paragraph({ heading: HeadingLevel.HEADING_3, children: runs(b.text) })); break;
        case "sub":
          out.push(new Paragraph({ style: "Body", keepNext: true, spacing: { before: 160, after: 0 }, children: runs(b.text, { bold: true }) }));
          break;
        case "p":
          out.push(new Paragraph({
            style: b.style || "Body",
            alignment: b.align === "center" ? AlignmentType.CENTER : b.align === "right" ? AlignmentType.RIGHT : b.align === "left" ? AlignmentType.LEFT : undefined,
            indent: b.indent === false ? { firstLine: 0 } : undefined,
            keepNext: b.keepNext, spacing: b.spacing,
            children: runs(b.text, { bold: b.bold, size: b.size, italics: b.italic }),
          }));
          break;
        case "list":
          b.items.forEach((it, i) => out.push(new Paragraph({
            style: "Body",
            numbering: { reference: b.kind === "num" ? "num" : "dash", level: 0, instance: b.inst },
            indent: { left: 1069, hanging: 360 },
            keepNext: i < b.items.length - 1 && b.items.length <= 3,
            children: runs(it),
          })));
          break;
        case "fig": {
          const img = imageSize(path.join(FIG_DIR, b.file));
          const w = Math.min(b.w || TEXT_W_PX, TEXT_W_PX);
          const h = Math.round(w * img.h / img.w);
          out.push(new Paragraph({
            alignment: AlignmentType.CENTER, keepNext: true, spacing: { before: 120, after: 60, line: 240 },
            children: [new ImageRun({ type: img.type, data: img.data, transformation: { width: w, height: h }, altText: { title: b.caption, description: b.caption, name: b.file } })],
          }));
          out.push(caption(`Рисунок ${b.num} – ${b.caption}`));
          break;
        }
        case "figs": {
          const n = b.items.length;
          const colW = Math.floor(TEXT_W / n);
          const cells = b.items.map((it) => {
            const img = imageSize(path.join(FIG_DIR, it.file));
            const w = Math.min(it.w || Math.floor(TEXT_W_PX / n) - 10, Math.floor(TEXT_W_PX / n) - 6);
            const h = Math.round(w * img.h / img.w);
            return new TableCell({
              width: { size: colW, type: WidthType.DXA }, borders: noBorders(), verticalAlign: VerticalAlign.TOP,
              margins: { top: 0, bottom: 0, left: 40, right: 40 },
              children: [
                new Paragraph({ alignment: AlignmentType.CENTER, spacing: { line: 240, before: 0, after: 40 }, children: [new ImageRun({ type: img.type, data: img.data, transformation: { width: w, height: h }, altText: { title: it.label, description: it.label, name: it.file } })] }),
                new Paragraph({ alignment: AlignmentType.CENTER, spacing: { line: 240, after: 0 }, children: runs(it.label, { size: 24 }) }),
              ],
            });
          });
          out.push(new Table({
            width: { size: TEXT_W, type: WidthType.DXA }, columnWidths: Array(n).fill(colW), borders: noBordersTable(),
            alignment: AlignmentType.CENTER,
            rows: [new TableRow({ cantSplit: true, children: cells })],
          }));
          out.push(caption(`Рисунок ${b.num} – ${b.caption}`));
          break;
        }
        case "table": out.push(...renderTable(b)); break;
        case "code": out.push(...renderCode(b)); break;
        case "eq":
          out.push(new Paragraph({
            style: "Body", indent: { firstLine: 0 },
            tabStops: [{ type: TabStopType.CENTER, position: Math.round(TEXT_W / 2) }, { type: TabStopType.RIGHT, position: TEXT_W }],
            spacing: { before: 120, after: 120 },
            children: [new TextRun({ text: "\t" }), ...runs(b.text, { italics: false }), new TextRun({ text: `\t(${b.num})` })],
          }));
          break;
        case "pb": out.push(new Paragraph({ children: [new PageBreak()] })); break;
        case "raw": out.push(...[].concat(b.fn(D))); break;
      }
    }
    return out;
  }
}

function caption(text) {
  return new Paragraph({ style: "Caption", children: runs(text) });
}
function noBorders() {
  const n = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
  return { top: n, bottom: n, left: n, right: n };
}
function noBordersTable() {
  const n = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
  return { top: n, bottom: n, left: n, right: n, insideHorizontal: n, insideVertical: n };
}

function renderTable(b) {
  const border = { style: BorderStyle.SINGLE, size: 4, color: "000000" };
  const borders = { top: border, bottom: border, left: border, right: border };
  const size = b.size || 24;
  const total = b.widths.reduce((a, c) => a + c, 0);
  if (total !== TEXT_W) throw new Error(`table widths ${total} != ${TEXT_W}: ${b.caption}`);
  const align = (i) => (b.align && b.align[i] === "c" ? AlignmentType.CENTER : b.align && b.align[i] === "r" ? AlignmentType.RIGHT : AlignmentType.LEFT);
  const cell = (text, i, header, bold) => new TableCell({
    width: { size: b.widths[i], type: WidthType.DXA }, borders,
    shading: header ? { fill: "E7E6E6", type: ShadingType.CLEAR, color: "auto" } : undefined,
    margins: { top: 30, bottom: 30, left: 80, right: 80 }, verticalAlign: VerticalAlign.CENTER,
    children: String(text).split("\n").map((line) => new Paragraph({
      style: "TableText", alignment: header ? AlignmentType.CENTER : align(i),
      children: runs(line, { size, bold: header || bold }),
    })),
  });
  const rows = [new TableRow({ tableHeader: true, cantSplit: true, children: b.header.map((h, i) => cell(h, i, true)) })];
  b.rows.forEach((r, ri) => rows.push(new TableRow({ cantSplit: true, children: r.map((c, i) => cell(c, i, false, b.boldRows && b.boldRows.includes(ri))) })));
  return [
    ...(b.nonum ? [] : [new Paragraph({ style: "TableCaption", children: runs(`Таблиця ${b.num} – ${b.caption}`) })]),
    new Table({ width: { size: TEXT_W, type: WidthType.DXA }, columnWidths: b.widths, rows }),
    new Paragraph({ style: "Body", spacing: { line: 240, after: 0 }, children: [] }),
  ];
}

function renderCode(b) {
  const box = { style: BorderStyle.SINGLE, size: 4, color: "A6A6A6", space: 4 };
  const lines = b.text.replace(/\s+$/, "").split("\n");
  const out = [];
  if (b.title) out.push(new Paragraph({ style: "Body", keepNext: true, spacing: { before: 120 }, indent: { firstLine: 0 }, children: runs(b.title, { italics: true, size: 26 }) }));
  lines.forEach((line, i) => out.push(new Paragraph({
    keepNext: b.keep !== false && i < lines.length - 1 && lines.length < 30,
    shading: { fill: "F4F4F4", type: ShadingType.CLEAR, color: "auto" },
    border: { top: box, bottom: box, left: box, right: box },
    spacing: { line: 240, before: 0, after: 0 }, indent: { left: 60, right: 60 },
    children: [new TextRun({ text: line.length ? line : " ", font: MONO, size: b.size || 19 })],
  })));
  out.push(new Paragraph({ style: "Body", spacing: { line: 240, after: 60 }, children: [] }));
  return out;
}

const styles = {
  default: { document: { run: { font: FONT, size: 28 }, paragraph: { spacing: { line: 360, after: 0 } } } },
  paragraphStyles: [
    { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
      run: { font: FONT, size: 28, bold: true, allCaps: true, color: "000000" },
      paragraph: { alignment: AlignmentType.CENTER, spacing: { before: 0, after: 240, line: 360 }, keepNext: true, keepLines: true, outlineLevel: 0 } },
    { id: "H1NoToc", name: "Heading 1 (no TOC)", basedOn: "Normal", next: "Normal",
      run: { font: FONT, size: 28, bold: true, allCaps: true },
      paragraph: { alignment: AlignmentType.CENTER, spacing: { before: 0, after: 240, line: 360 }, keepNext: true } },
    { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
      run: { font: FONT, size: 28, bold: true, color: "000000" },
      paragraph: { alignment: AlignmentType.LEFT, spacing: { before: 240, after: 120, line: 360 }, indent: { firstLine: 708 }, keepNext: true, keepLines: true, outlineLevel: 1 } },
    { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
      run: { font: FONT, size: 28, bold: true, italics: true, color: "000000" },
      paragraph: { alignment: AlignmentType.LEFT, spacing: { before: 160, after: 60, line: 360 }, indent: { firstLine: 708 }, keepNext: true, keepLines: true, outlineLevel: 2 } },
    { id: "Body", name: "Body Text CR", basedOn: "Normal", quickFormat: true,
      run: { font: FONT, size: 28 },
      paragraph: { alignment: AlignmentType.JUSTIFIED, spacing: { line: 360, before: 0, after: 0 }, indent: { firstLine: 708 } } },
    { id: "Caption", name: "Caption", basedOn: "Normal", next: "Body",
      run: { font: FONT, size: 26 },
      paragraph: { alignment: AlignmentType.CENTER, spacing: { before: 60, after: 200, line: 276 } } },
    { id: "TableCaption", name: "Table Caption", basedOn: "Normal", next: "Normal",
      run: { font: FONT, size: 28 },
      paragraph: { alignment: AlignmentType.LEFT, spacing: { before: 200, after: 80, line: 276 }, keepNext: true } },
    { id: "TableText", name: "Table Text", basedOn: "Normal",
      run: { font: FONT, size: 24 },
      paragraph: { spacing: { line: 240, before: 0, after: 0 } } },
    { id: "Title0", name: "Title Page", basedOn: "Normal",
      run: { font: FONT, size: 28 },
      paragraph: { alignment: AlignmentType.CENTER, spacing: { line: 300, before: 0, after: 0 } } },
  ],
};

const numbering = {
  config: [
    { reference: "dash", levels: [{ level: 0, format: LevelFormat.BULLET, text: "–", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 1069, hanging: 360 } } } }] },
    { reference: "num", levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 1069, hanging: 360 } } } }] },
  ],
};

module.exports = { D, Report, runs, styles, numbering, TEXT_W, TEXT_W_PX, FONT, noBorders, noBordersTable, imageSize };
