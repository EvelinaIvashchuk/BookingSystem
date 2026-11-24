// Приблизний попередній перегляд DOCX без Word/LibreOffice: mammoth → HTML (A4, TNR 14, 1.5) → PDF (Chrome).
// DOCX_MODULE_DIR=<папка з node_modules/mammoth> node report/src/preview.js <docx> <out.pdf>
const fs = require("fs");
const path = require("path");
const { createRequire } = require("module");
const { execFileSync } = require("child_process");
const mammoth = createRequire(path.join(process.env.DOCX_MODULE_DIR || __dirname, "x.js"))("mammoth");

const [docx, outPdf] = process.argv.slice(2);
const styleMap = [
  "p[style-name='Caption'] => p.caption:fresh",
  "p[style-name='Table Caption'] => p.tcap:fresh",
  "p[style-name='Title Page'] => p.t0:fresh",
  "p[style-name='Table Text'] => p.tt:fresh",
  "p[style-name='Heading 1 (no TOC)'] => h1.notoc:fresh",
  "p[style-name='Body Text CR'] => p.body:fresh",
];
mammoth.convertToHtml({ path: docx }, { styleMap }).then((res) => {
  const css = `
    @page { size: A4; margin: 20mm 20mm 20mm 30mm; }
    body { font-family: 'Times New Roman', serif; font-size: 14pt; line-height: 1.5; margin: 0; }
    p { margin: 0; text-align: justify; text-indent: 1.25cm; }
    p.t0 { text-align: center; text-indent: 0; line-height: 1.25; }
    p.caption { text-align: center; text-indent: 0; font-size: 13pt; line-height: 1.15; margin: 4pt 0 10pt; }
    p.tcap { text-indent: 0; margin: 10pt 0 4pt; line-height: 1.15; }
    p.tt, td p { text-indent: 0; font-size: 12pt; line-height: 1.0; text-align: left; }
    h1 { font-size: 14pt; text-transform: uppercase; text-align: center; break-before: page; margin: 0 0 12pt; }
    h2 { font-size: 14pt; text-indent: 1.25cm; margin: 12pt 0 6pt; break-after: avoid; }
    h3 { font-size: 14pt; font-style: italic; text-indent: 1.25cm; margin: 8pt 0 3pt; }
    table { border-collapse: collapse; width: 100%; margin: 0 0 6pt; break-inside: auto; }
    td, th { border: 0.5pt solid #000; padding: 1pt 4pt; vertical-align: middle; }
    tr { break-inside: avoid; }
    img { display: block; margin: 6pt auto 2pt; max-width: 100%; }
    p:has(> img) { text-indent: 0; text-align: center; break-after: avoid; }
  `;
  const html = `<!doctype html><html><head><meta charset="utf-8"><style>${css}</style></head><body>${res.value}</body></html>`;
  const htmlPath = outPdf.replace(/\.pdf$/, ".html");
  fs.writeFileSync(htmlPath, html);
  execFileSync("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", [
    "--headless=new", "--disable-gpu", "--no-pdf-header-footer", `--print-to-pdf=${outPdf}`, "--virtual-time-budget=5000", "file://" + htmlPath,
  ], { stdio: "ignore" });
  console.log("messages:", res.messages.filter((m) => m.type !== "warning" || !/Unrecognised/.test(m.message)).slice(0, 5));
  console.log("wrote", outPdf);
});
