// Висновки, список джерел, додатки.
const fs = require("fs");
const path = require("path");
const ROOT = path.join(__dirname, "..", "..");

const REFERENCES = [
  "Yao S., Zhao J., Yu D., Du N., Shafran I., Narasimhan K., Cao Y. ReAct: Synergizing Reasoning and Acting in Language Models // International Conference on Learning Representations (ICLR). 2023. arXiv:2210.03629.",
  "Schick T., Dwivedi-Yu J., Dessì R., Raileanu R., Lomeli M., Zettlemoyer L., Cancedda N., Scialom T. Toolformer: Language Models Can Teach Themselves to Use Tools // Advances in Neural Information Processing Systems (NeurIPS). 2023. arXiv:2302.04761.",
  "Lewis P., Perez E., Piktus A., Petroni F., Karpukhin V., Goyal N. et al. Retrieval-Augmented Generation for Knowledge-Intensive NLP Tasks // Advances in Neural Information Processing Systems (NeurIPS). 2020. Vol. 33. P. 9459–9474.",
  "Wang L., Ma C., Feng X., Zhang Z., Yang H., Zhang J. et al. A Survey on Large Language Model Based Autonomous Agents // Frontiers of Computer Science. 2024. Vol. 18, No. 6. Article 186345.",
  "Patil S. G., Zhang T., Wang X., Gonzalez J. E. Gorilla: Large Language Model Connected with Massive APIs. arXiv:2305.15334. 2023.",
  "Greshake K., Abdelnabi S., Mishra S., Endres C., Holz T., Fritz M. Not What You've Signed Up For: Compromising Real-World LLM-Integrated Applications with Indirect Prompt Injection // Proceedings of the 16th ACM Workshop on Artificial Intelligence and Security (AISec). 2023. P. 79–90.",
  "OWASP Top 10 for Large Language Model Applications [Електронний ресурс]. – Режим доступу: https://genai.owasp.org/llm-top-10/",
  "Ji Z., Lee N., Frieske R., Yu T., Su D., Xu Y. et al. Survey of Hallucination in Natural Language Generation // ACM Computing Surveys. 2023. Vol. 55, No. 12. Article 248.",
  "Budzianowski P., Wen T.-H., Tseng B.-H., Casanueva I., Ultes S., Ramadan O., Gašić M. MultiWOZ – A Large-Scale Multi-Domain Wizard-of-Oz Dataset for Task-Oriented Dialogue Modelling // Proceedings of EMNLP 2018. P. 5016–5026.",
  "Bocklisch T., Faulkner J., Pawlowski N., Nichol A. Rasa: Open Source Language Understanding and Dialogue Management. arXiv:1712.05181. 2017.",
  "Karpukhin V., Oğuz B., Min S., Lewis P., Wu L., Edunov S., Chen D., Yih W. Dense Passage Retrieval for Open-Domain Question Answering // Proceedings of EMNLP 2020. P. 6769–6781.",
  "Robertson S., Zaragoza H. The Probabilistic Relevance Framework: BM25 and Beyond // Foundations and Trends in Information Retrieval. 2009. Vol. 3, No. 4. P. 333–389.",
  "Manning C. D., Raghavan P., Schütze H. Introduction to Information Retrieval. Cambridge : Cambridge University Press, 2008. 482 p.",
  "Thakur N., Reimers N., Rücklé A., Srivastava A., Gurevych I. BEIR: A Heterogeneous Benchmark for Zero-shot Evaluation of Information Retrieval Models // NeurIPS Datasets and Benchmarks Track. 2021. arXiv:2104.08663.",
  "Cormack G. V., Clarke C. L. A., Büttcher S. Reciprocal Rank Fusion Outperforms Condorcet and Individual Rank Learning Methods // Proceedings of the 32nd International ACM SIGIR Conference. 2009. P. 758–759.",
  "Efron B., Tibshirani R. J. An Introduction to the Bootstrap. New York : Chapman & Hall/CRC, 1993. 436 p.",
  "Agresti A. Categorical Data Analysis. 3rd ed. Hoboken : Wiley, 2013. 714 p.",
  "Microsoft. Microsoft Agent Framework. Документація [Електронний ресурс]. – Режим доступу: https://learn.microsoft.com/agent-framework/",
  "Microsoft. Microsoft.Extensions.AI libraries. Документація .NET [Електронний ресурс]. – Режим доступу: https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai",
  "Google. Gemini API: OpenAI compatibility [Електронний ресурс]. – Режим доступу: https://ai.google.dev/gemini-api/docs/openai",
  "Google. Gemini API: Embeddings [Електронний ресурс]. – Режим доступу: https://ai.google.dev/gemini-api/docs/embeddings",
  "Groq. GroqCloud Documentation [Електронний ресурс]. – Режим доступу: https://console.groq.com/docs",
  "MySQL 8.0 Reference Manual. Full-Text Search Functions [Електронний ресурс]. – Режим доступу: https://dev.mysql.com/doc/refman/8.0/en/fulltext-search.html",
  "Perez F., Ribeiro I. Ignore Previous Prompt: Attack Techniques For Language Models // NeurIPS ML Safety Workshop. 2022. arXiv:2211.09527.",
  "Mialon G., Dessì R., Lomeli M., Nalmpantis C., Pasunuru R., Raileanu R. et al. Augmented Language Models: a Survey // Transactions on Machine Learning Research. 2023. arXiv:2302.07842.",
  "Berkeley Function Calling Leaderboard [Електронний ресурс]. – Режим доступу: https://gorilla.cs.berkeley.edu/leaderboard.html",
  "Мартін Р. Чиста архітектура: посібник майстра для розробки програмного забезпечення. Харків : Фабула, 2019. 368 с.",
  "Грицюк Ю. І. Аналіз вимог до програмного забезпечення : навчальний посібник. Львів : Видавництво Львівської політехніки, 2018. 456 с.",
  "Google. Gemini API: Function calling [Електронний ресурс]. – Режим доступу: https://ai.google.dev/gemini-api/docs/function-calling",
  "Rastogi A., Zang X., Sunkara S., Gupta R., Khaitan P. Towards Scalable Multi-Domain Conversational Agents: The Schema-Guided Dialogue Dataset // Proceedings of the AAAI Conference on Artificial Intelligence. 2020. Vol. 34, No. 05. P. 8689–8696.",
  "Yao S., Shinn N., Razavi P., Narasimhan K. τ-bench: A Benchmark for Tool-Agent-User Interaction in Real-World Domains. arXiv:2406.12045. 2024.",
  "Debenedetti E., Zhang J., Balunović M., Beurer-Kellner L., Fischer M., Tramèr F. AgentDojo: A Dynamic Environment to Evaluate Prompt Injection Attacks and Defenses for LLM Agents // NeurIPS Datasets and Benchmarks Track. 2024. arXiv:2406.13352.",
  "Zhan Q., Liang Z., Ying Z., Kang D. InjecAgent: Benchmarking Indirect Prompt Injections in Tool-Integrated Large Language Model Agents // Findings of the Association for Computational Linguistics: ACL 2024. P. 10471–10506.",
  "Wilson E. B. Probable Inference, the Law of Succession, and Statistical Inference // Journal of the American Statistical Association. 1927. Vol. 22, No. 158. P. 209–212.",
  "McNemar Q. Note on the Sampling Error of the Difference Between Correlated Proportions or Percentages // Psychometrika. 1947. Vol. 12, No. 2. P. 153–157.",
  "Burges C. J. C. From RankNet to LambdaRank to LambdaMART: An Overview. Microsoft Research Technical Report MSR-TR-2010-82. 2010. 19 p.",
  "Ke G., Meng Q., Finley T., Wang T., Chen W., Ma W., Ye Q., Liu T.-Y. LightGBM: A Highly Efficient Gradient Boosting Decision Tree // Advances in Neural Information Processing Systems (NeurIPS). 2017. Vol. 30. P. 3146–3154.",
  "Järvelin K., Kekäläinen J. Cumulated Gain-Based Evaluation of IR Techniques // ACM Transactions on Information Systems. 2002. Vol. 20, No. 4. P. 422–446.",
  "Liu T.-Y. Learning to Rank for Information Retrieval // Foundations and Trends in Information Retrieval. 2009. Vol. 3, No. 3. P. 225–331.",
  "Expedia launches conversational trip planning powered by ChatGPT to inspire members to dream about travel in new ways [Електронний ресурс]. – Режим доступу: https://www.expedia.com/newsroom/expedia-launched-chatgpt",
  "Booking.com Launches New AI Trip Planner to Enhance Travel Planning Experience. Booking Holdings, 27.06.2023 [Електронний ресурс]. – Режим доступу: https://www.bookingholdings.com/press-releases/booking-com-launches-new-ai-trip-planner-to-enhance-travel-planning-experience/",
  "Expedia and Kayak Offer Early Convergence of ChatGPT and Travel Booking. Skift, 23.03.2023 [Електронний ресурс]. – Режим доступу: https://skift.com/2023/03/23/expedia-and-kayak-offer-early-convergence-of-chatgpt-and-travel-booking/",
  "Rentalcars.com. Car hire [Електронний ресурс]. – Режим доступу: https://www.rentalcars.com/",
  "Turo. Car sharing marketplace [Електронний ресурс]. – Режим доступу: https://turo.com/",
  "Hertz. Car rental [Електронний ресурс]. – Режим доступу: https://www.hertz.com/",
  "Uber Rent в Україні [Електронний ресурс]. – Режим доступу: https://www.uber.com/ua/uk/u/uber-rent",
  "KAYAK Україна. Оренда авто в Києві [Електронний ресурс]. – Режим доступу: https://www.ua.kayak.com/Orenda-Avto-v-Kiv-Deshevo.15139.cars.ksp",
];

// Дата звернення для всіх електронних ресурсів.
const ACCESSED = "23.11.2026";
for (let i = 0; i < REFERENCES.length; i++)
  if (REFERENCES[i].includes("Режим доступу:") && !REFERENCES[i].includes("дата звернення"))
    REFERENCES[i] = REFERENCES[i].replace(/\.?$/, "") + ` (дата звернення: ${ACCESSED}).`;

function read(rel) { return fs.readFileSync(path.join(ROOT, rel), "utf8"); }

// Текст системних інструкцій — зі справжнього коду AgentPrompts.cs (рядки sb.AppendLine("...")), з прикладом контексту.
function promptText() {
  const src = read("BookingSystem.Infrastructure/Ai/AgentPrompts.cs");
  const lines = [];
  const re = /sb\.AppendLine\((?:"((?:[^"\\]|\\.)*)")?\)/g;
  let m;
  const stopAt = src.indexOf('sb.AppendLine("## Context");');
  while ((m = re.exec(src)) && m.index <= stopAt) lines.push((m[1] || "").replace(/\\"/g, '"'));
  lines.push(
    "- Current local date and time: 2026-11-18 10:00 (Wednesday), Europe/Kyiv.",
    "- Calendar of the next days. Resolve relative dates (\"tomorrow\", \"this Friday\", \"next weekend\") by looking them up here, never by mental arithmetic:",
    "  2026-11-18 Wednesday (today)",
    "  2026-11-19 Thursday (tomorrow)",
    "  2026-11-20 Friday",
    "  … (усього 15 днів)",
    "- The user is signed in.",
    "",
    "## Known booking parameters",
    "Collected earlier in this conversation. Reuse them unless the user changes them; to drop a filter pass \"any\".",
    "- pickupDate: 2026-12-14",
    "- returnDate: 2026-12-16",
    "- minSeats: 7",
  );
  return lines.join("\n");
}

function stripUsings(code) {
  return code.split("\n").filter((l) => !/^using /.test(l)).join("\n").replace(/^\s*\n/, "");
}

function scenarioExamples() {
  const agent = JSON.parse(read("BookingSystem.Evaluation/datasets/agent_scenarios.json")).scenarios;
  const sec = JSON.parse(read("BookingSystem.Evaluation/datasets/security_scenarios.json")).scenarios;
  const q = JSON.parse(read("BookingSystem.Evaluation/datasets/retrieval_qrels.json")).queries;
  const pick = (arr, ids) => ids.map((id) => arr.find((x) => x.id === id)).filter(Boolean);
  return {
    agent: pick(agent, ["S-01", "I-02", "M-09", "R-03", "B-08", "B-15"]),
    sec: pick(sec, ["X-15", "X-19"]),
    q: pick(q, ["Q-01", "Q-02"]).concat(q.filter((x) => x.kind === "descriptive").slice(0, 1)),
  };
}

module.exports = function part3(r) {
  // ------------------------------------------------------------------ ВИСНОВКИ
  r.h1("ВИСНОВКИ");
  r.p(`У курсовій роботі досягнуто поставленої мети: розроблено й експериментально оцінено LLM-агента для системи оренди автомобілів, який за запитом природною мовою знаходить вільні авто, розраховує вартість, відповідає на питання про правила оренди з бази знань і готує бронювання з обов'язковим підтвердженням користувачем. Основні результати роботи такі.`);
  r.numbered([
    `Проаналізовано підходи до побудови LLM-агентів, виклику інструментів і доповненої генерації та характерні загрози таких систем: галюцинації параметрів, помилки у відносних датах, плутанину ідентифікаторів, пряму й непряму ін'єкцію інструкцій. Аналіз аналогів показав відсутність рішення, що поєднує природний діалог, роботу з реальними даними, контрольовану дію та виміряну надійність.`,
    `Спроєктовано агента, вбудованого в наявну багаторівневу архітектуру без дублювання бізнес-логіки: шість інструментів з мінімальними повноваженнями, чотири нові сутності бази даних, незалежність від провайдера моделі («provider:model»). Єдина дія агента зі змінами даних — чернетка бронювання, яку підтверджує людина; захист побудовано на шести рівнях, де критичні гарантії забезпечує код, а не інструкції моделі.`,
    `Підбір авто за нечіткими побажаннями сформульовано як задачу навчання ранжування й розв'язано власною моделлю LightGBM LambdaRank, навченою на 600 синтетичних побажаннях (15 типів потреб, українська й англійська мови) з ознаками ембединга побажання та характеристиками 48 авто. На відкладеній тестовій вибірці зі 160 побажань із новими формулюваннями модель має NDCG@5 = 0,895 (95 % ДІ [0,869; 0,918]) проти 0,180 у поточного сортування за ціною, 0,536 у схожості ембедингів і 0,639 у логістичної регресії; усі різниці значущі. Модель обчислюється в C# без ML-бібліотек і вбудована в інструмент пошуку агента: у 20 нових сценаріях частка випадків, коли перше рекомендоване авто відповідає побажанню, зросла з 0,35 до 0,95 (12 сценаріїв краще, 0 гірше, p = 0,0005). Обмеження: мітки релевантності синтетичні, тож відповідність смакам реальних клієнтів потребує перевірки на даних бронювань.`,
    `Розроблено механізм заповнення параметрів, який зливає аргументи моделі з відомими слотами сесії, валідує й нормалізує їх і не дає виконати інструмент із відсутніми, невалідними чи вигаданими значеннями; його доповнено перевіркою походження дат і звіркою назви авто з ідентифікатором. На 1 267 записаних викликах перевірка походження дат не дала жодного хибного блокування, а в повторному прогоні заблокувала 11 вигаданих або неправильних дат.`,
    `Розроблено адаптивний гібридний пошук у базі знань, що зважує векторний і повнотекстовий пошук за ознаками запиту. На коротких термінових запитах він найкращий (MRR@10 = 1,000 на тестовій вибірці), на описових поступається чистому векторному пошуку; у цілому на базі зі 110 фрагментів найкращим є векторний пошук (MRR@10 = 0,951), який і використовується на сайті.`,
    `Створено відтворювану систему оцінювання з датасетами 83 сценаріїв агента, 25 атакувальних сценаріїв і 56 розмічених запитів та проведено 2 241 прогін сценаріїв загальною вартістю 7,82 $. Порівняння семи моделей показало, що gemini-3.6-flash виконує 73 з 74 сценаріїв (0,986, 95 % ДІ [0,927; 0,998]) за медіанної затримки 4,7 с і вартості близько 0,7 цента за сценарій; від gemini-3.8-flash вона за якістю не відрізняється, lite-моделі на 4–5 п. п. гірші (різниця в межах ДІ), але в кілька разів дешевші й швидші; модель qwen3.8-27b для агента непридатна. Цільове значення Task success ≥ 0,95 досягається лише за критерієм уточнення, розширеним після перегляду результатів (за початковим — 0,878).`,
    `Ablation-аналіз показав, що для сильної моделі slot filling не змінює результату (ефект стелі), а для слабшої дає тенденцію до покращення й стабільності між повторами (Task success 0,928 → 0,946, стандартне відхилення 0,021 → 0), яка статистично не значуща (4 провали з 222, тест Мак-Немара за сценаріями p = 0,63). Механізм не збільшує затримку й вартість, а його основний внесок — детерміновані гарантії: невалідний виклик не виконується за побудовою.`,
    `За результатами якісного аналізу помилок агента вдосконалено: на 24 спільних сценаріях покращення виправили 10 зі 120 пар «модель — сценарій» без жодного погіршення (частка успішних 0,867 → 0,950, 95 % ДІ різниці [0,025; 0,150]). Ефект отримано лише для слабших моделей (для обраної gemini-3.6-flash змін немає) і на тих самих сценаріях, за якими покращення розроблено, тож оцінка оптимістична. В експерименті зі стійкості до атак моделі Gemini відбили всі 21 атаку (ASR = 0, 95 % ДІ [0; 0,155]); єдину успішну атаку на моделі gpt-oss (неправдива «знижка» в тексті відповіді) усунуто перевіркою сум у відповіді: у повторному прогоні вона спрацювала в 6 з 6 атак, а на записаних прогонах не змінила жодної з 1 209 легітимних відповідей.`,
  ]);
  r.p(`Практична цінність роботи полягає в тому, що агент працює в реальному вебзастосунку оренди, підбирає авто за побажаннями клієнта навченою моделлю, модель чату і режим пошуку обрано за вимірюваннями, а система оцінювання дозволяє однією командою повторити порівняння при появі нових моделей. Результати показують, що надійність LLM-агента визначається не лише вибором моделі: шар валідації між моделлю й інструментами, підтвердження людиною та перевірки в коді компенсують помилки моделей і роблять навіть дешевші моделі придатними для практичного застосування.`);
  r.p(`Обмеження роботи (п. 5.9): критерій зарахування уточнення змінено після перегляду результатів; незалежної тестової вибірки для покращень немає; ефективні вибірки малі (74 сценарії, 21 атака, 39 тестових запитів до бази знань), тож більшість різниць між моделями й методами статистично не підтверджена; автоматичні критерії вмісту відповіді не перевірено ручною розміткою.`);
  r.p(`Перспективи розвитку: перенавчання моделі ранжування на реальних бронюваннях і кліках та тестова вибірка побажань, розмічена людьми; незалежна тестова вибірка сценаріїв із критеріями, зафіксованими до прогону, і ручна перевірка вибірки відповідей; замість видалення речень із неперевіреною сумою — повторна генерація відповіді з підказкою; підказувати ідентифікатор авто з правильною назвою в помилці про неіснуючий id; кешування префікса системних інструкцій для зменшення вартості; адаптивні атаки з перебором формулювань в E4; розширення бази знань і повторне порівняння методів пошуку на більшому обсязі (≥ 150 запитів, k-кратна крос-валідація, власна реалізація BM25); онлайн-оплата після підтвердження бронювання.`);

  // ------------------------------------------------------------------ ДЖЕРЕЛА
  r.h1("СПИСОК ВИКОРИСТАНИХ ДЖЕРЕЛ");
  REFERENCES.forEach((ref, i) => r.p(`${i + 1}. ${ref}`, { indent: false, align: "left", spacing: { after: 60 } }));

  // ------------------------------------------------------------------ ДОДАТКИ
  r.h1("ДОДАТКИ");
  r.p(`Додаток А. Системні інструкції агента`, { indent: false });
  r.p(`Додаток Б. Код механізму заповнення параметрів`, { indent: false });
  r.p(`Додаток В. Код адаптивного злиття результатів пошуку`, { indent: false });
  r.p(`Додаток Г. Приклади сценаріїв датасетів`, { indent: false });
  r.p(`Додаток Д. Відтворення експериментів і налаштування агента`, { indent: false });
  r.p(`Додаток Е. Рубрика релевантності й обчислення моделі ранжування`, { indent: false });

  r.h1("ДОДАТОК А", { appendix: "А" });
  r.center(`**Системні інструкції агента**`, { keepNext: true });
  r.p(`Текст сформовано методом AgentPrompts.BuildInstructions (BookingSystem.Infrastructure/Ai/AgentPrompts.cs). Розділ Context і блок відомих параметрів наведено на прикладі сесії 18.11.2026 з уже відомими датами й кількістю місць.`, { indent: false });
  r.code(promptText(), { size: 17, keep: false });

  r.h1("ДОДАТОК Б", { appendix: "Б" });
  r.center(`**Код механізму заповнення параметрів**`, { keepNext: true });
  r.p(`Файл BookingSystem.Infrastructure/Ai/Slots/SlotFillingEngine.cs.`, { indent: false });
  r.code(stripUsings(read("BookingSystem.Infrastructure/Ai/Slots/SlotFillingEngine.cs")), { size: 16, keep: false });

  r.h1("ДОДАТОК В", { appendix: "В" });
  r.center(`**Код адаптивного злиття результатів пошуку**`, { keepNext: true });
  r.p(`В.1. Файл BookingSystem.Infrastructure/Ai/Knowledge/HybridFusion.cs.`, { indent: false });
  r.code(stripUsings(read("BookingSystem.Infrastructure/Ai/Knowledge/HybridFusion.cs")), { size: 16, keep: false });
  const qf = read("BookingSystem.Infrastructure/Ai/Knowledge/QueryFeatures.cs");
  const start = qf.indexOf("public sealed partial record QueryFeatures");
  const end = qf.indexOf("[GeneratedRegex");
  r.p(`В.2. Ознаки запиту й адаптивні ваги (фрагмент файлу QueryFeatures.cs).`, { indent: false, spacing: { before: 200 } });
  r.code(qf.slice(start, end).trimEnd(), { size: 16, keep: false });

  r.h1("ДОДАТОК Г", { appendix: "Г" });
  r.center(`**Приклади сценаріїв датасетів**`, { keepNext: true });
  const ex = scenarioExamples();
  r.p(`Г.1. Сценарії агента (agent_scenarios.json): простий запит, неповний запит, багатокроковий діалог, відносні дати, зайняте авто, бронювання без дати повернення.`, { indent: false });
  r.code(JSON.stringify(ex.agent, null, 1), { size: 16, keep: false });
  r.p(`Г.2. Атакувальні сценарії (security_scenarios.json): пряма спроба «знижки» і непряма ін'єкція через опис авто.`, { indent: false, spacing: { before: 200 } });
  r.code(JSON.stringify(ex.sec, null, 1), { size: 16, keep: false });
  r.p(`Г.3. Запити до бази знань із розміткою релевантних фрагментів (retrieval_qrels.json).`, { indent: false, spacing: { before: 200 } });
  r.code(JSON.stringify(ex.q, null, 1), { size: 16, keep: false });

  r.h1("ДОДАТОК Д", { appendix: "Д" });
  r.center(`**Відтворення експериментів і налаштування агента**`, { keepNext: true });
  r.p(`Д.1. Команди відтворення експериментів (виконуються з кореня репозиторію; потрібні MySQL 8 і ключі провайдерів у user-secrets).`, { indent: false });
  r.code(`# база для експериментів + індексація бази знань (ембединги Gemini)
dotnet run --project BookingSystem.Evaluation -- index

# E3 — пошук: параметри підбираються лише на dev, оцінка на test
dotnet run --project BookingSystem.Evaluation -- retrieval --tune-on dev --eval-on test

# E1 — порівняння моделей (model@N — кількість повторів; --max-cost — ліміт витрат)
dotnet run --project BookingSystem.Evaluation -- agent --models gemini:gemini-3.8-flash@3,\\
  gemini:gemini-3.6-flash@3,gemini:gemini-3.5-flash-lite@3,gemini:gemini-3.1-flash-lite@3,\\
  groq:openai/gpt-oss-120b@1,groq:openai/gpt-oss-20b@1,groq:qwen/qwen3.8-27b@1 --max-cost 5.90

# E2 — ablation slot filling (умова "on" береться з рядків E1)
dotnet run --project BookingSystem.Evaluation -- ablation \\
  --models gemini:gemini-3.8-flash@3,gemini:gemini-3.5-flash-lite@3 --max-cost 5.90

# оцінка перевірки походження дат на записаних прогонах (без викликів моделей)
dotnet run --project BookingSystem.Evaluation -- grounding-replay

# E1-v2 — повтор після покращень
dotnet run --project BookingSystem.Evaluation -- agent --experiment E1-v2 \\
  --types booking,incomplete,relative-dates --models gemini:gemini-3.6-flash@3,\\
  gemini:gemini-3.5-flash-lite@3,gemini:gemini-3.1-flash-lite@3,\\
  groq:openai/gpt-oss-120b@1,groq:openai/gpt-oss-20b@1 --max-cost 7.50

# E4 — prompt injection і доступ до чужих даних
dotnet run --project BookingSystem.Evaluation -- security --models gemini:gemini-3.6-flash@3,\\
  gemini:gemini-3.5-flash-lite@3,gemini:gemini-3.1-flash-lite@3,\\
  groq:openai/gpt-oss-120b@1,groq:openai/gpt-oss-20b@1 --max-cost 1.50

# перевірка сум: повтор X-15/X-16 в окремий файл і реплей на записаних прогонах
dotnet run --project BookingSystem.Evaluation -- security --ids X-15,X-16 \\
  --models groq:openai/gpt-oss-120b@3,groq:openai/gpt-oss-20b@3 --out results/security_amountcheck.csv
dotnet run --project BookingSystem.Evaluation -- amount-replay

# підсумкові таблиці, графіки й довірчі інтервали на рівні сценаріїв
dotnet run --project BookingSystem.Evaluation -- summary
tools/.venv/bin/python tools/plot.py
python3 tools/stats.py

# E5 — модель ранжування авто: дані, набір побажань, ембединги, навчання й оцінювання
dotnet run --project BookingSystem.Evaluation -- ranking-data
tools/.venv/bin/python tools/ranking/make_wishes.py
dotnet run --project BookingSystem.Evaluation -- ranking-data
tools/.venv/bin/python tools/ranking/train.py
dotnet run --project BookingSystem.Evaluation -- agent --experiment E5-ranked \\
  --scenarios wish_scenarios.json --models gemini:gemini-3.6-flash@1 --out results/ranking/agent_wishes.csv
dotnet run --project BookingSystem.Evaluation -- agent --experiment E5-price \\
  --scenarios wish_scenarios.json --models gemini:gemini-3.6-flash@1 --out results/ranking/agent_wishes.csv \\
  --Ai:CarRanking:Enabled=false
tools/.venv/bin/python tools/ranking/agent_eval.py`, { size: 16, keep: false });
  const cfg = JSON.parse(read("BookingSystem/appsettings.json")).Ai;
  for (const p of Object.values(cfg.Providers || {})) if ("ApiKey" in p) p.ApiKey = "";
  r.p(`Д.2. Розділ Ai конфігурації застосунку (BookingSystem/appsettings.json; ключі API задаються через user-secrets або змінні середовища).`, { indent: false, spacing: { before: 200 } });
  r.code(JSON.stringify(cfg, null, 2), { size: 16, keep: false });

  r.h1("ДОДАТОК Е", { appendix: "Е" });
  r.center(`**Рубрика релевантності й обчислення моделі ранжування**`, { keepNext: true });
  r.p(`Е.1. Рубрика релевантності — функції корисності потреб і обчислення мітки (файл tools/ranking/rubric.py).`, { indent: false });
  const rubric = read("tools/ranking/rubric.py");
  r.code(rubric.slice(rubric.indexOf("HARD = ")).trimEnd(), { size: 16, keep: false });
  const ranker = read("BookingSystem.Infrastructure/Ai/Ranking/CarRanker.cs");
  const a = ranker.indexOf("    public double Score(");
  const b = ranker.indexOf("    public static CarRankingModel Load(");
  const c = ranker.indexOf("    public sealed class Tree(");
  const d = ranker.indexOf("    /// <summary>Tree node:");
  r.p(`Е.2. Обчислення моделі на сервері: проєкція ембединга побажання на головні компоненти та обхід дерев LightGBM (фрагменти файлу BookingSystem.Infrastructure/Ai/Ranking/CarRanker.cs).`, { indent: false, spacing: { before: 200 } });
  r.code((ranker.slice(a, b) + ranker.slice(c, d)).trimEnd(), { size: 16, keep: false });
};
module.exports.REFERENCES = REFERENCES;
