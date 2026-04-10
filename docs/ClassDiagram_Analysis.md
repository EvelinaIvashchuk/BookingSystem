# Аналіз діаграми класів — Car Rental System

## 1. Розглянутий логічний ланцюжок

```
RentalCreateViewModel
    ↓  validates via
IValidator<RentalCreateViewModel>  (FluentValidation)
    ↓
RentalController
    ↓  maps via
IMapper  (AutoMapper / RentalMappingProfile)
    ↓  CreateRentalDto
IRentalService
    ↓
RentalService
    ↓  uow.Rentals / uow.Cars / uow.CommitAsync()
IUnitOfWork
    ↓
UnitOfWork
    ├── IRentalRepository  →  RentalRepository
    │       ↓
    │   IGenericRepository<T>  →  GenericRepository<T>
    │       ↓
    │   ApplicationDbContext  →  Rental (entity)
    │
    └── ICarRepository  →  CarRepository
            ↓
        IGenericRepository<T>  →  GenericRepository<T>
            ↓
        ApplicationDbContext  →  Car (entity)
```

Допоміжні класи: `ServiceResult<T>`, `PaginatedList<T>`,
`ApplicationUser` (extends `IdentityUser`), `Category`, `Payment`,
`IEmailService` / `MockEmailService`.

---

## 2. Реалізовані патерни та SOLID

### 2.1 Repository Pattern
`IGenericRepository<T>` → `GenericRepository<T>` → `RentalRepository` / `CarRepository`

- `IGenericRepository<T>` визначає базовий CRUD-контракт.
- `GenericRepository<T>` реалізує його поверх `ApplicationDbContext`.
- `RentalRepository` та `CarRepository` успадковують `GenericRepository<T>`
  і додають специфічні методи (overlap-перевірка за датами, фільтрація за статусом тощо).
- **Відповідність**: повна. Ні контролер, ні сервіс не знають про EF Core.

### 2.2 Unit of Work Pattern
`IUnitOfWork` → `UnitOfWork`

- `UnitOfWork` об'єднує `IRentalRepository` та `ICarRepository`
  під єдиним `ApplicationDbContext`.
- Єдиний `CommitAsync()` гарантує **атомарне збереження**: або обидва
  репозиторії зберегли зміни, або жоден.
- `RentalService` ніколи не викликає `SaveChangesAsync()` безпосередньо —
  лише `uow.CommitAsync()`.

### 2.3 DTO Pattern
`CreateRentalDto` (вхідний) / `RentalDto` (вихідний)

- `RentalCreateViewModel` (MVC-шар) і `CreateRentalDto` (сервісний шар) — **різні типи**.
- Перетворення `ViewModel → DTO` відбувається в контролері через `IMapper`.
- Сервіс не залежить від жодного MVC-специфічного типу.

### 2.4 AutoMapper
`RentalMappingProfile` : `Profile`

- `RentalCreateViewModel → CreateRentalDto` — маппінг при виклику сервісу.
- `Rental → RentalDto` — маппінг Entity → Data Transfer Object.
- Логіка перетворення зосереджена в одному місці, не розкидана по контролерах.

### 2.5 FluentValidation
`RentalCreateViewModelValidator` : `AbstractValidator<RentalCreateViewModel>`

Правила, що перевіряються перед передачею в сервіс:

| Правило | Значення |
|---|---|
| `PickupDate` >= `UtcNow.Date` | Дата не в минулому |
| `ReturnDate` > `PickupDate` | Коректний діапазон |
| Тривалість ≥ 1 день | Мінімальна оренда |
| Тривалість ≤ 30 днів | Максимальна оренда |
| `PickupDate` ≤ now + 60 днів | Не далі двох місяців наперед |
| `Notes.Length` ≤ 500 | Обмеження поля |

- Помилки автоматично передаються в `ModelState` через `AddToModelState()`.
- Сервіс не дублює ці перевірки (за винятком overlap та ліміту активних оренд,
  які потребують звернення до БД).

### 2.6 Service Layer Pattern
`IRentalService` → `RentalService`

Бізнес-правила, що перевіряються в `RentalService`:

| Правило | Де перевіряється |
|---|---|
| Автомобіль існує та `Available` | БД через `uow.Cars` |
| Кількість активних оренд ≤ 3 | БД через `uow.Rentals` |
| Відсутність overlap-перекриття за датами | БД через `uow.Rentals` |
| Не можна скасувати розпочату оренду | Порівняння `PickupDate` з `UtcNow` |
| Статус-машина (Pending → Confirmed/Rejected) | Перевірка поточного статусу |
| TotalPrice = кількість днів × PricePerDay | Обчислення в сервісі |

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
| **O** — Open/Closed | `GenericRepository<T>` розширюється без змін через спадкування; `RentalMappingProfile` додає маппінги без зміни існуючих | ✅ |
| **L** — Liskov Substitution | `RentalRepository` повністю замінює `GenericRepository<T>`; `UnitOfWork` повністю замінює `IUnitOfWork` | ✅ |
| **I** — Interface Segregation | `IRentalRepository` розширює `IGenericRepository<T>` лише специфічними методами; `IUnitOfWork` не містить зайвих методів | ✅ |
| **D** — Dependency Inversion | `RentalController` залежить від `IRentalService`, `IMapper`, `IValidator<T>`; `RentalService` залежить від `IUnitOfWork`, `IEmailService` — все через інтерфейси | ✅ |

---

## 4. Повний потік запиту (POST /Rental/Create)

```
[Browser] POST /Rental/Create
    ↓
RentalController.Create(RentalCreateViewModel vm)
    ↓  1. validator.ValidateAsync(vm)
IValidator<RentalCreateViewModel>          ← FluentValidation
    ↓  якщо помилки → повернути View з ModelState
    ↓  2. mapper.Map<CreateRentalDto>(vm)
IMapper / RentalMappingProfile             ← AutoMapper
    ↓  3. rentalService.CreateRentalAsync(userId, dto)
RentalService
    ↓  4. ValidateDates(dto.PickupDate, dto.ReturnDate)  [статична]
    ↓  5. uow.Cars.GetWithCategoryAsync(dto.CarId)
IUnitOfWork → CarRepository → GenericRepository → ApplicationDbContext
    ↓  6. uow.Rentals.GetActiveRentalCountAsync(userId)
    ↓  7. uow.Rentals.HasOverlapAsync(...)
IUnitOfWork → RentalRepository → GenericRepository → ApplicationDbContext
    ↓  8. TotalPrice = (ReturnDate - PickupDate).TotalDays × car.PricePerDay
    ↓  9. uow.Rentals.AddAsync(rental)
    ↓  10. uow.CommitAsync()         ← єдина точка збереження
    ↓  11. SafeSendEmailAsync(...)   ← fire-and-forget
MockEmailService
    ↓
ServiceResult<Rental>.Ok(created)
    ↓
RentalController → TempData["Success"] → RedirectToAction(MyRentals)
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
**Проблема**: `GetAllRentalsAsync()` повертає `IEnumerable<Rental>` без пагінації.
При великій кількості записів — проблема продуктивності.

**Рішення**: Додати `PaginatedList<T>` аналогічно до `MyRentals`.

---

## 6. Висновок

Архітектура проєкту реалізує повний стек патернів coursework-рівня:

| Патерн | Клас(и) | Статус |
|---|---|---|
| Repository | `IGenericRepository<T>`, `GenericRepository<T>`, `RentalRepository`, `CarRepository` | ✅ Реалізовано |
| Unit of Work | `IUnitOfWork`, `UnitOfWork` | ✅ Реалізовано |
| DTO | `CreateRentalDto`, `RentalDto` | ✅ Реалізовано |
| AutoMapper | `RentalMappingProfile` : `Profile` | ✅ Реалізовано |
| FluentValidation | `RentalCreateViewModelValidator` : `AbstractValidator<T>` | ✅ Реалізовано |
| Service Layer | `IRentalService`, `RentalService` | ✅ Реалізовано |
| Result Pattern | `ServiceResult<T>`, `ServiceResult` | ✅ Реалізовано |
| Dependency Injection | Всі залежності через інтерфейси та конструктор | ✅ Реалізовано |

Кожен шар архітектури має чітко визначену відповідальність.
Залежності між шарами спрямовані лише через інтерфейси (принцип DIP).
Збереження даних є атомарним завдяки `UnitOfWork.CommitAsync()`.
