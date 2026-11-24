// Збирає пояснювальну записку: DOCX_MODULE_DIR=<папка з node_modules/docx> node report/src/build_report.js
const fs = require("fs");
const path = require("path");
const kit = require("./docx-kit");
const { D, Report, runs, styles, numbering, TEXT_W, noBorders } = kit;
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell, WidthType, AlignmentType, Header,
  PageNumber, BorderStyle, ShadingType, VerticalAlign, PageBreak,
} = D;
const part1 = require("./content-1");
const part2 = require("./content-2");
const part3 = require("./content-3");

const META = {
  title: "Розроблення методу інтелектуального підбору та бронювання автомобілів на основі LLM-агента з викликом функцій та гібридним пошуком у базі",
  student: "Е.В. Іващук",
  studentFull: "Іващук Евеліни Віталіївни",
  group: "444",
  course: "4",
  supervisor: "М.П. Горський",
  supervisorTitle: "доцент, доктор фізико-математичних наук",
  year: "2026",
  deadline: "24 листопада 2026 р.",
};
const OUT = path.join(__dirname, "..", "zvit_Ivashchuk_LLM_agent.docx");

// ------------------------------------------------------------------ контент (два проходи: спершу рахуємо рисунки/таблиці)
function buildContent(meta) {
  const r = new Report();
  part1(r, meta);
  part2(r);
  part3(r);
  r.number();
  return r;
}
const probe = buildContent({ figures: 0, tables: 0, refs: 0, appendices: 0 });
const meta = { figures: probe.counts.fig, tables: probe.counts.tab, refs: part3.REFERENCES.length, appendices: 6 };
const report = buildContent(meta);
console.log("figures", meta.figures, "tables", meta.tables, "refs", meta.refs);

// ------------------------------------------------------------------ титульна сторінка і завдання
const T = (text, opts = {}) => new Paragraph({
  style: "Title0", alignment: opts.align ?? AlignmentType.CENTER, pageBreakBefore: opts.pageBreak,
  indent: opts.left ? { left: opts.left } : undefined,
  spacing: { before: opts.before || 0, after: opts.after || 0, line: opts.line || 300 },
  children: runs(text, { bold: opts.bold, size: opts.size, italics: opts.italic }),
});
const blank = (n = 1) => Array.from({ length: n }, () => T(""));

const titlePage = [
  T("МІНІСТЕРСТВО ОСВІТИ І НАУКИ УКРАЇНИ"),
  T("Чернівецький національний університет імені Юрія Федьковича"),
  T("Навчально-науковий інститут фізико-технічних та комп’ютерних наук"),
  T("Кафедра комп’ютерних наук"),
  ...blank(4),
  T("ПОЯСНЮВАЛЬНА ЗАПИСКА", { bold: true, size: 32 }),
  T("до курсової роботи на тему:", { before: 120 }),
  T(`«${META.title}»`, { bold: true, before: 120, line: 360 }),
  ...blank(3),
  T(`Виконала студентка ${META.course}-го курсу групи ${META.group}`, { align: AlignmentType.LEFT, left: 4600 }),
  T("спеціальності 122 «Комп’ютерні науки»", { align: AlignmentType.LEFT, left: 4600 }),
  T(`__________ ${META.student}`, { align: AlignmentType.LEFT, left: 4600, before: 240 }),
  T("(підпис)            (ініціали та прізвище)", { align: AlignmentType.LEFT, left: 4600, size: 20 }),
  T("Керівник:", { align: AlignmentType.LEFT, left: 4600, before: 360 }),
  T(`${META.supervisorTitle}`, { align: AlignmentType.LEFT, left: 4600 }),
  T(`__________ ${META.supervisor}`, { align: AlignmentType.LEFT, left: 4600, before: 240 }),
  T("(підпис)            (ініціали та прізвище)", { align: AlignmentType.LEFT, left: 4600, size: 20 }),
  ...blank(5),
  T(`Чернівці – ${META.year}`),
];

function gridTable(widths, rows, opts = {}) {
  const b = { style: BorderStyle.SINGLE, size: 4, color: "000000" };
  return new Table({
    width: { size: TEXT_W, type: WidthType.DXA }, columnWidths: widths,
    rows: rows.map((row, ri) => new TableRow({
      tableHeader: ri === 0 && opts.header, cantSplit: true,
      children: row.map((cell, ci) => new TableCell({
        width: { size: widths[ci], type: WidthType.DXA }, borders: opts.noBorder ? noBorders() : { top: b, bottom: b, left: b, right: b },
        shading: ri === 0 && opts.header ? { fill: "E7E6E6", type: ShadingType.CLEAR, color: "auto" } : undefined,
        margins: { top: 30, bottom: 30, left: 80, right: 80 }, verticalAlign: VerticalAlign.CENTER,
        children: String(cell).split("\n").map((t) => new Paragraph({
          style: "TableText", alignment: opts.align && opts.align[ci] === "c" ? AlignmentType.CENTER : AlignmentType.LEFT,
          children: runs(t, { size: opts.size || 24, bold: ri === 0 && opts.header }),
        })),
      })),
    })),
  });
}

const L = (label, value) => new Paragraph({
  style: "Title0", alignment: AlignmentType.LEFT, spacing: { before: 100, after: 0, line: 300 },
  children: [...runs(label, { bold: true }), ...runs(value)],
});

const taskPage = [
  T("Чернівецький національний університет імені Юрія Федьковича", { pageBreak: true }),
  T("Кафедра комп’ютерних наук", { after: 200 }),
  T("ЗАВДАННЯ", { bold: true }),
  T("НА КУРСОВУ РОБОТУ СТУДЕНТА", { bold: true, after: 200 }),
  T(META.studentFull, { bold: true }),
  T("(прізвище, ім’я, по батькові)", { size: 20, after: 120 }),
  L("Тема роботи: ", `«${META.title}».`),
  L("Термін подання студентом закінченої роботи: ", META.deadline),
  L("Вхідні дані до роботи: ", "тематика дослідження; програмний код системи бронювання оренди автомобілів (курсова робота 3 курсу); документація Microsoft Agent Framework, Gemini API, GroqCloud, LightGBM; характеристики моделей авто демо-автопарку; датасети сценаріїв і побажань клієнтів для навчання й оцінювання."),
  L("Зміст пояснювальної записки (перелік питань, які треба розробити): ", ""),
  ...["Вступ",
    "Розділ 1. Аналіз предметної області та постановка задачі",
    "Розділ 2. Проєктування LLM-агента",
    "Розділ 3. Методи підбору авто, заповнення параметрів і пошуку в базі знань",
    "Розділ 4. Програмна реалізація",
    "Розділ 5. Експериментальне дослідження",
    "Висновки, список використаних джерел, додатки"].map((t) => T(`– ${t}`, { align: AlignmentType.LEFT, left: 360 })),
  L("Перелік графічного матеріалу: ", "UML-діаграми (компонентів, станів, послідовності), ER-діаграма, схеми алгоритмів, графіки й таблиці результатів експериментів і навчання моделі ранжування, знімки екрана."),
  T("Календарний план підготовки курсової роботи", { bold: true, after: 160, pageBreak: true }),
  gridTable([600, 4971, 2200, 1300], [
    ["№", "Назва етапів курсової роботи", "Термін виконання", "Примітки"],
    ["1", "Отримання завдання на курсову роботу", "01.09.2026", "виконано"],
    ["2", "Аналіз літератури: LLM-агенти, виклик функцій, RAG, загрози", "01.09–20.09.2026", "виконано"],
    ["3", "Аналіз аналогів і постановка задачі", "21.09–05.10.2026", "виконано"],
    ["4", "Проєктування архітектури агента, інструментів і моделі даних", "06.10–31.10.2026", "виконано"],
    ["5", "Реалізація агента, інструментів і підтвердження бронювання", "01.11–14.11.2026", "виконано"],
    ["6", "Реалізація slot filling, бази знань і гібридного пошуку", "15.11–17.11.2026", "виконано"],
    ["7", "Датасети і проведення експериментів E1–E4", "18.11–20.11.2026", "виконано"],
    ["8", "Покращення агента за результатами експериментів", "19.11–20.11.2026", "виконано"],
    ["9", "Набір даних побажань, навчання й оцінювання моделі ранжування авто", "21.11–23.11.2026", "виконано"],
    ["10", "Написання пояснювальної записки", "20.11–24.11.2026", ""],
    ["11", "Представлення закінченої роботи на перевірку", META.deadline.replace(" р.", ""), ""],
    ["12", "Захист курсової роботи", "01.12.2026", ""],
  ], { header: true, align: ["c", "l", "c", "c"] }),
  T("", { before: 200 }),
  gridTable([3000, 3000, 3071], [
    ["Студент", "__________ (підпис)", `${META.student}`],
    ["Науковий керівник", "__________ (підпис)", `${META.supervisor}`],
    ["«___» ____________ 20__ р.", "", ""],
  ], { noBorder: true, size: 26 }),
];

// ------------------------------------------------------------------ документ
const pageProps = {
  page: {
    size: { width: 11906, height: 16838 },
    margin: { top: 1134, bottom: 1134, left: 1701, right: 1134, header: 600, footer: 600 },
  },
};
const doc = new Document({
  creator: "Е.В. Іващук",
  title: META.title,
  description: "Пояснювальна записка до курсової роботи",
  styles, numbering,
  features: { updateFields: true },
  sections: [
    { properties: pageProps, children: [...titlePage, ...taskPage] },
    {
      properties: pageProps,
      headers: {
        default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [new TextRun({ children: [PageNumber.CURRENT], font: "Times New Roman", size: 28 })] })] }),
      },
      children: report.render(),
    },
  ],
});

Packer.toBuffer(doc).then((buf) => {
  fs.writeFileSync(OUT, buf);
  console.log("wrote", OUT, (buf.length / 1024 / 1024).toFixed(1) + " MB");
});
