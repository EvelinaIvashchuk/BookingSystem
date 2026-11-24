# Пояснювальна записка (курсова, 4 курс)

`zvit_Ivashchuk_LLM_agent.docx` — згенерований документ. Після відкриття у Word погодьтеся оновити поля:
так оновляться зміст і кількість сторінок в анотації.

Як перезібрати після правок тексту (`src/content-*.js`) або результатів:

```bash
tools/.venv/bin/python report/src/charts.py      # графіки з BookingSystem.Evaluation/results/*.csv
node report/src/diagrams.js                       # діаграми (headless Chrome)
node report/src/screenshots.js                    # знімки екрана (потрібен запущений сайт на :5110; робить запити до LLM)
DOCX_MODULE_DIR=<папка з node_modules/docx> node report/src/build_report.js

# номери сторінок у змісті (без них зміст заповниться лише після оновлення полів у Word)
soffice --headless --convert-to pdf --outdir /tmp/zvit report/zvit_Ivashchuk_LLM_agent.docx
python3 report/src/toc_pages.py report/zvit_Ivashchuk_LLM_agent.docx /tmp/zvit/zvit_Ivashchuk_LLM_agent.pdf
soffice --headless --convert-to pdf --outdir report report/zvit_Ivashchuk_LLM_agent.docx
```

- `src/build_report.js`: титульна сторінка, завдання, метадані (тема, група, дати).
- `src/content-1.js`: анотації, скорочення, вступ, розділи 1–2.
- `src/content-2.js`: розділи 3–5.
- `src/content-3.js`: висновки, джерела, додатки.
- Рисунки й таблиці нумеруються автоматично, посилання в тексті: `{fig:key}`, `{tab:key}`, `{eq:key}`.

## Звіт, що здається (Google Docs-експорт) і E6

Здається не згенерований `zvit_Ivashchuk_LLM_agent.docx`, а `01_Звіт_Іващук_ЕВ_444_виправлений.docx` з правками. Нейромережі й порівняння
з Gemini (E6), розділ про поділ даних і формат (14 pt, підписи таблиць зверху, широкі таблиці на альбомних сторінках) додає скрипт;
вихідний файл не змінюється, результат — `02_Звіт_Іващук_ЕВ_444_нейромережі.docx`:

```bash
tools/.venv/bin/python tools/ranking/train_nn.py        # MLP і двобаштова мережа (PyTorch), ~7 хв
tools/.venv/bin/python tools/ranking/gemini_rank.py     # Gemini як ранжувальник на 160 тестових побажаннях (~2 $, відповіді кешуються)
tools/.venv/bin/python tools/ranking/bench_nn.py        # час навчання й затримка нейромереж
tools/.venv/bin/python tools/ranking/compare_models.py  # зведена таблиця, парні різниці, графіки ch_e6_*.png
tools/.venv/bin/python report/src/patch_docx.py         # збирає 02_*.docx (усі числа беруться з results/ranking/*.json)
soffice --headless --convert-to pdf --outdir /tmp/zvit report/02_Звіт_Іващук_ЕВ_444_нейромережі.docx
python3 report/src/toc_pages.py report/02_Звіт_Іващук_ЕВ_444_нейромережі.docx /tmp/zvit/02_Звіт_Іващук_ЕВ_444_нейромережі.pdf --write
tools/.venv/bin/python report/src/check_docx.py         # перевірка: 14 pt у всіх фрагментах, підписи таблиць лише зверху
```

Зміст у цьому файлі — статичний текст (не поле Word), номери сторінок беруться з рендеру LibreOffice і можуть відрізнятися
від Word на 1–2 сторінки: після відкриття у Word звірте їх.
