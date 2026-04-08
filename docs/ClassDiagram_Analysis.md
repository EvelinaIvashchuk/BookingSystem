# Аналіз діаграми класів — BookingSystem

## 1. Розглянутий логічний ланцюжок

```
BookingCreateViewModel
    ↓  validates via
IValidator<BookingCreateViewModel>  (FluentValidation)
    ↓
BookingController
    ↓  maps via
IMapper  (AutoMapper / BookingMappingProfile)
    ↓  CreateBookingDto
IBookingService
    ↓
BookingService
    ↓  uow.Bookings / uow.Resources / uow.CommitAsync()
IUnitOfWork
    ↓
UnitOfWork
    ├── IBookingRepository  →  BookingRepository
    │       ↓
    │   IGenericRepository<T>  →  GenericRepository<T>
    │       ↓
    │   ApplicationDbContext  →  Booking (entity)
    │
    └── IResourceRepository  →  ResourceRepository
            ↓
        IGenericRepository<T>  →  GenericRepository<T>
            ↓
        ApplicationDbContext  →  Resource (entity)
```

Допоміжні класи: `ServiceResult<T>`, `PaginatedList<T>`,
`ApplicationUser` (extends `IdentityUser`), `Category`,
`IEmailService` / `MockEmailService`.

---

## 2. Реалізовані патерни та SOLID

### 2.1 Repository Pattern
`IGenericRepository<T>` → `GenericRepository<T>` → `BookingRepository` / `ResourceRepository`

- `IGenericRepository<T>` визначає базовий CRUD-контракт.
- `GenericRepository<T>` реалізує його поверх `ApplicationDbContext`.
- `BookingRepository` та `ResourceRepository` успадковують `GenericRepository<T>`
  і додають специфічні методи (overlap-перевірка, фільтрація за статусом тощо).
- **Відповідність**: повна. Ні контролер, ні сервіс не знають про EF Core.

### 2.2 Unit of Work Pattern
`IUnitOfWork` → `UnitOfWork`

- `UnitOfWork` об'єднує `IBookingRepository` та `IResourceRepository`
  під єдиним `ApplicationDbContext`.
- Єдиний `CommitAsync()` гарантує **атомарне збереження**: або обидва
  репозиторії зберегли зміни, або жоден.
- `BookingService` ніколи не викликає `SaveChangesAsync()` безпосередньо —
  лише `uow.CommitAsync()`.

### 2.3 DTO Pattern
`CreateBookingDto` (вхідний) / `BookingDto` (вихідний)

- `BookingCreateViewModel` (MVC-шар) і `CreateBookingDto` (сервісний шар) — **різні типи**.
- Перетворення `ViewModel → DTO` відбувається в контролері через `IMapper`.
- Сервіс не залежить від жодного MVC-специфічного типу.

### 2.4 AutoMapper
`BookingMappingProfile` : `Profile`

- `BookingCreateViewModel → CreateBookingDto` — маппінг при виклику сервісу.
- `Booking → BookingDto` — маппінг Entity → Data Transfer Object.
- Логіка перетворення зосереджена в одному місці, не розкидана по контролерах.

### 2.5 FluentValidation
`BookingCreateViewModelValidator` : `AbstractValidator<BookingCreateViewModel>`

Правила, що перевіряються перед передачею в сервіс:

| Правило | Значення |
|---|---|
| `StartTime` > `UtcNow` | Час у майбутньому |
| `EndTime` > `StartTime` | Коректний діапазон |
| Тривалість ≥ 30 хв | Мінімальне бронювання |
| Тривалість ≤ 8 год | Максимальне бронювання |
| `StartTime` ≤ now + 30 днів | Не далі місяця наперед |
| `Purpose.Length` ≤ 500 | Обмеження поля |

- Помилки автоматично передаються в `ModelState` через `AddToModelState()`.
- Сервіс не дублює ці перевірки (за винятком overlap та ліміту активних бронювань,
  які потребують звернення до БД).

### 2.6 Service Layer Pattern
`IBookingService` → `BookingService`

Бізнес-правила, що перевіряються в `BookingService`:

| Правило | Де перевіряється |
|---|---|
| Ресурс існує та `Available` | БД через `uow.Resources` |
| Кількість активних бронювань ≤ 3 | БД через `uow.Bookings` |
| Відсутність overlap-перекриття | БД через `uow.Bookings` |
| Не можна скасувати розпочате | Порівняння `StartTime` з `UtcNow` |
| Статус-машина (Pending → Confirmed/Rejected) | Перевірка поточного статусу |

### 2.7 Result Pattern
`ServiceResult` / `ServiceResult<T>`

- Очікувані відмови передаються через `IsSuccess = false` + `Error`.
- Контролер не містить `try/catch` для бізнес-правил.
- Винятки зарезервовані для інфраструктурних збоїв (мережа, БД).

---

## 3. SOLID-аналіз

| Принцип | Реалізація | Оцінка |
|---|---|---|
| **S** — Single Responsibility | Controller → HTTP та прив'язка форми; Validator → правила UI-валідації; Mapper → перетворення типів; Service → бізнес-логіка; Repository → доступ до даних | ✅ |
| **O** — Open/Closed | `GenericRepository<T>` розширюється без змін через спадкування; `BookingMappingProfile` додає маппінги без зміни існуючих | ✅ |
| **L** — Liskov Substitution | `BookingRepository` повністю замінює `GenericRepository<T>`; `UnitOfWork` повністю замінює `IUnitOfWork` | ✅ |
| **I** — Interface Segregation | `IBookingRepository` розширює `IGenericRepository<T>` лише специфічними методами; `IUnitOfWork` не містить зайвих методів | ✅ |
| **D** — Dependency Inversion | `BookingController` залежить від `IBookingService`, `IMapper`, `IValidator<T>`; `BookingService` залежить від `IUnitOfWork`, `IEmailService` — все через інтерфейси | ✅ |

---

## 4. Повний потік запиту (POST /Booking/Create)

```
[Browser] POST /Booking/Create
    ↓
BookingController.Create(BookingCreateViewModel vm)
    ↓  1. validator.ValidateAsync(vm)
IValidator<BookingCreateViewModel>          ← FluentValidation
    ↓  якщо помилки → повернути View з ModelState
    ↓  2. mapper.Map<CreateBookingDto>(vm)
IMapper / BookingMappingProfile             ← AutoMapper
    ↓  3. bookingService.CreateBookingAsync(userId, dto)
BookingService
    ↓  4. ValidateTimes(dto.StartTime, dto.EndTime)  [static]
    ↓  5. uow.Resources.GetWithCategoryAsync(dto.ResourceId)
IUnitOfWork → ResourceRepository → GenericRepository → ApplicationDbContext
    ↓  6. uow.Bookings.GetActiveBookingCountAsync(userId)
    ↓  7. uow.Bookings.HasOverlapAsync(...)
IUnitOfWork → BookingRepository → GenericRepository → ApplicationDbContext
    ↓  8. uow.Bookings.AddAsync(booking)
    ↓  9. uow.CommitAsync()          ← єдина точка збереження
    ↓  10. SafeSendEmailAsync(...)   ← fire-and-forget
MockEmailService
    ↓
ServiceResult<Booking>.Ok(created)
    ↓
BookingController → TempData["Success"] → RedirectToAction(MyBookings)
```

---

## 5. Залишкові слабкі місця

### 5.1 `GenericRepository<T>` — відсутнє обмеження типу
**Проблема**: `where T : class` присутнє неявно через EF Core, але явного
`IEntity`-constraint немає. Можна передати будь-який клас.

**Рішення**: Додати маркерний інтерфейс:
```csharp
public interface IEntity { int Id { get; } }
public class GenericRepository<T> where T : class, IEntity { ... }
```
Для coursework-рівня — прийнятний компроміс.

### 5.2 MockEmailService — fire-and-forget
**Проблема**: `SafeSendEmailAsync` логує помилки, але не перевіряє доставку.
У продакшн-середовищі email може мовчки не відправитись.

**Рішення**: Черга повідомлень (Hangfire / MassTransit / IHostedService).
Для навчального проєкту — прийнятно.

### 5.3 Немає пагінації для адмін-панелі
**Проблема**: `GetAllBookingsAsync()` повертає `IEnumerable<Booking>` без пагінації.
При великій кількості записів — проблема продуктивності.

**Рішення**: Додати `PaginatedList<T>` аналогічно до `MyBookings`.

---

## 6. Висновок

Архітектура проєкту реалізує повний стек патернів coursework-рівня:

| Патерн | Клас(и) | Статус |
|---|---|---|
| Repository | `IGenericRepository<T>`, `GenericRepository<T>`, `BookingRepository`, `ResourceRepository` | ✅ Реалізовано |
| Unit of Work | `IUnitOfWork`, `UnitOfWork` | ✅ Реалізовано |
| DTO | `CreateBookingDto`, `BookingDto` | ✅ Реалізовано |
| AutoMapper | `BookingMappingProfile` : `Profile` | ✅ Реалізовано |
| FluentValidation | `BookingCreateViewModelValidator` : `AbstractValidator<T>` | ✅ Реалізовано |
| Service Layer | `IBookingService`, `BookingService` | ✅ Реалізовано |
| Result Pattern | `ServiceResult<T>`, `ServiceResult` | ✅ Реалізовано |
| Dependency Injection | Всі залежності через інтерфейси та конструктор | ✅ Реалізовано |

Кожен шар архітектури має чітко визначену відповідальність.
Залежності між шарами спрямовані лише через інтерфейси (принцип DIP).
Збереження даних є атомарним завдяки `UnitOfWork.CommitAsync()`.
