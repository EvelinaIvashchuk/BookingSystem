# CarRental — бронювання авто з LLM-агентом

Вебзастосунок для оренди автомобілів (ASP.NET Core 9 MVC, MySQL) з чат-асистентом, який розуміє
запити природною мовою: знаходить вільні авто на потрібні дати, рахує ціну, відповідає на питання
про правила оренди з бази знань і готує чернетку бронювання. Саме бронювання створюється **лише після
того, як користувач натисне «Підтвердити»** — у моделі немає інструмента, що бронює чи платить.

Курсова робота, 4 курс. Власні внески:

1. **Заповнення параметрів (slot filling) з валідацією** — `SlotFillingEngine` перед кожним викликом
   інструмента зливає аргументи моделі з параметрами, які користувач назвав раніше, перевіряє дати
   (у тому числі за текстом запиту), межі та значення перелічень і не дає виконати виклик із
   відсутніми чи вигаданими параметрами. Вмикається `Ai:SlotFilling:Enabled`.
2. **Адаптивний гібридний пошук у базі знань** — векторний пошук (ембединги Gemini) і MySQL FULLTEXT
   по стемованому тексту зливаються через RRF з вагами, що залежать від ознак запиту (довжина, терміни, цифри).
   Режим задається `Ai:Search:Mode`: `Vector`, `FullText`, `HybridRrf`, `HybridAdaptive`.

## Можливості асистента

| Інструмент | Що робить |
|---|---|
| `search_available_cars` | вільні на весь період авто з фільтрами (місто, категорія, коробка, пальне, місця, ціна) |
| `get_car_details` | характеристики одного авто |
| `calculate_price` | ціна конкретного авто на дати та перевірка зайнятості |
| `get_my_rentals` | оренди поточного користувача |
| `create_rental_draft` | чернетка бронювання (`PendingAction`, діє 15 хв) і картка з кнопками «Підтвердити» / «Скасувати» |
| `search_rental_policies` | фрагменти правил оренди з бази знань (`BookingSystem/KnowledgeBase/*.md`) |

Відносні дати («цієї п'ятниці на 3 дні») модель розв'язує за календарем у системних інструкціях;
неповний запит («хочу авто на вихідні») приводить до уточнювального питання, а не до вигаданих параметрів.

Безпека: rate limit на IP і сесію, antiforgery на всіх POST, обмеження довжини повідомлення,
захист системних інструкцій від prompt injection, `userId` береться лише з автентифікації,
підтвердження чужої чи протермінованої чернетки відхиляється (403 / 410).

## Архітектура

```
BookingSystem                  MVC: контролери (у т.ч. AiController), Razor-подання, чат-віджет, KnowledgeBase/*.md
BookingSystem.Application      інтерфейси, DTO, налаштування (AiOptions)
BookingSystem.Domain           сутності: Car, Rental, ChatSession, PendingAction, KnowledgeChunk …
BookingSystem.Infrastructure   EF Core + MySQL, сервіси; Ai/ — агент, інструменти, slot filling, пошук
BookingSystem.Evaluation       консольні експерименти E1–E3 (див. BookingSystem.Evaluation/results/README.md)
BookingSystem.Tests            xUnit-тести
tools/plot.py                  графіки результатів експериментів
docs/                          UML-діаграми (PlantUML)
```

Агент побудовано на Microsoft Agent Framework (`ChatClientAgent`) і Microsoft.Extensions.AI;
моделі підключаються через OpenAI-сумісні API (Groq, Gemini), модель задається рядком `provider:model`.

Діаграми в `docs/`:

| Файл | Що показує |
|---|---|
| `ComponentDiagram_AiAgent.puml` | компоненти LLM-агента та їх зв'язки з наявною системою |
| `SequenceDiagram_AiBooking.puml` | запит → виклик інструмента (зі slot filling) → підтвердження бронювання |
| `StateDiagram_PendingAction.puml` | стани чернетки: Pending → Confirmed / Cancelled / Expired |
| `ComponentDiagram.puml`, `SequenceDiagram_CreateBooking.puml`, `StateDiagram_Booking.puml`, … | базова система оренди |

## Запуск локально

Потрібні .NET SDK 9 і MySQL 8 (локальний або `docker compose up mysql`).

```bash
# рядок підключення (у Development за замовчуванням — localhost:3306, база CarRentalDb_Dev, див. appsettings.Development.json)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Port=3306;Database=CarRentalDb;User=root;Password=...;" --project BookingSystem

# ключі LLM-провайдерів (без них сайт працює, а чат відповідає «AI service is not configured»)
dotnet user-secrets set "Ai:Providers:groq:ApiKey" "<ключ Groq>" --project BookingSystem
dotnet user-secrets set "Ai:Providers:gemini:ApiKey" "<ключ Gemini>" --project BookingSystem

dotnet run --project BookingSystem
```

Під час старту застосовуються міграції (`InitialCreate`, `AddAiAgent`, `AddKnowledgeBase`), створюються
ролі й адміністратор (`Seed:AdminEmail` / `Seed:AdminPassword`), додається демо-автопарк із 50 авто
(`Seed:DemoFleet=false` вимикає) з фото з Wikimedia Commons (CC BY / CC BY-SA; автори — на сторінці `/Home/Photos`, фото, завантажені в адмінці, не перезаписуються) і індексується база знань (`Ai:KnowledgeBase:IndexOnStartup`;
змінені документи переіндексовуються, незмінені пропускаються). Ембединги рахує Gemini, тож для
векторного пошуку потрібен ключ Gemini; без нього гібридні режими переходять на повнотекстовий пошук.

## Налаштування AI (`BookingSystem/appsettings.json`, розділ `Ai`)

| Ключ | Значення за замовчуванням | Призначення |
|---|---|---|
| `DefaultModel` | `gemini:gemini-3.6-flash` | модель чату, `provider:model`; змінюється без зміни коду (обрано за E1) |
| `Providers:<name>:Endpoint`, `ApiKey`, `TimeoutSeconds` | Groq, Gemini | OpenAI-сумісні провайдери |
| `Temperature`, `MaxOutputTokens`, `MaxAgentIterations`, `HistoryLength` | 0.2, 1024, 5, 12 | параметри агента |
| `SlotFilling:Enabled` | `true` | внесок 1 (вимкнення — базовий варіант для ablation) |
| `Search:Mode`, `TopK`, `Candidates`, `RrfK`, `Adaptive:*` | `Vector`, 3, 20, 60 | внесок 2 — режим `HybridAdaptive` (параметри `Adaptive` підібрано на dev-вибірці); на сайті `Vector` — найкращий у цілому за E3 |
| `PendingActionTtlMinutes` | 15 | строк дії чернетки бронювання |
| `RateLimit:PerIpPerMinute`, `PerSessionPerHour` | 20, 60 | обмеження запитів до чату |
| `Pricing[]` | — | ціни моделей за 1M токенів (для оцінки вартості в логах і експериментах) |

Моделі Gemini 3 вимагають повертати `thought_signature` разом із викликами інструментів;
це робить `GeminiThoughtSignatureHandler`, який підключається автоматично для ендпоінта Gemini.

## Тести

```bash
dotnet test BookingSystem.sln
```

Тести агента (`BookingSystem.Tests/Ai`) використовують замоканий `IChatClient` і InMemory-базу:
ліміт ітерацій, `userId` з контексту, обробка `invalid_slots`, валідація слотів, формула RRF і адаптивні ваги,
пошук вільних авто з перетином дат, підтвердження чужої / протермінованої чернетки тощо.

## Експерименти

Проєкт `BookingSystem.Evaluation` запускає той самий `AgentService` і пошук на окремій базі
`bookingsystem_eval`: E1 — порівняння моделей, E2 — ablation slot filling, E3 — методи пошуку.
Команди, результати, таблиці й графіки — у [`BookingSystem.Evaluation/results/README.md`](BookingSystem.Evaluation/results/README.md).

## Деплой (Railway)

`railway.toml` збирає `BookingSystem/Dockerfile`. Змінні середовища сервісу:

| Змінна | Призначення |
|---|---|
| `ConnectionStrings__DefaultConnection` | MySQL (Railway MySQL plugin) |
| `Ai__Providers__groq__ApiKey` (або старий `Groq__ApiKey`) | ключ Groq |
| `Ai__Providers__gemini__ApiKey` | ключ Gemini (чат на моделях Gemini і ембединги бази знань) |
| `Ai__DefaultModel`, `Ai__Search__Mode` | за потреби — інша модель чи режим пошуку без перезбирання |
| `Seed__AdminEmail`, `Seed__AdminPassword` | обліковий запис адміністратора |

Міграції, демо-автопарк та індексація бази знань виконуються під час старту контейнера.
