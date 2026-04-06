# Аналіз діаграми класів — BookingSystem

## 1. Розглянутий логічний ланцюжок

```
BookingController → IBookingService → BookingService
    → IBookingRepository → BookingRepository
    → IGenericRepository<T> → GenericRepository<T>
    → ApplicationDbContext → Booking (entity)
```

Допоміжні класи: `ServiceResult<T>`, `BookingCreateViewModel`,
`PaginatedList<T>`, `ApplicationUser`, `Resource`, `IEmailService`, `MockEmailService`.

---

## 2. Реалізовані патерни та SOLID

### Repository Pattern
`IGenericRepository<T>` → `GenericRepository<T>` → `BookingRepository`
- Абстрагує доступ до бази через інтерфейси
- `BookingRepository` додає специфічні методи поверх базового CRUD
- **Відповідність**: повна. Контролери не знають про EF Core.

### Service Layer Pattern
`IBookingService` → `BookingService`
- Вся бізнес-логіка інкапсульована в сервісі
- Контролер тільки передає дані та повертає View

### Result Pattern (ServiceResult<T>)
- Замість винятків для очікуваних відмов (`IsSuccess/Error`)
- Уникає `try/catch` у контролері для бізнес-правил

### SOLID принципи

| Принцип | Реалізація | Оцінка |
|---|---|---|
| **S** — Single Responsibility | Кожен клас має одну відповідальність: Controller→HTTP, Service→бізнес-логіка, Repository→доступ до даних | ✅ |
| **O** — Open/Closed | `GenericRepository<T>` розширюється без змін через спадкування (`BookingRepository`) | ✅ |
| **L** — Liskov Substitution | `BookingRepository` повністю замінює `GenericRepository<T>` | ✅ |
| **I** — Interface Segregation | `IBookingRepository` розширює `IGenericRepository<T>` — не змушує реалізовувати непотрібне | ✅ |
| **D** — Dependency Inversion | `BookingController` залежить від `IBookingService`, а не `BookingService` | ✅ |

---

## 3. Виявлені слабкі місця

### 3.1 Відсутній Unit of Work (UoW)
**Проблема**: `BookingService.CreateBookingAsync` викликає кілька репозиторіїв і кожен має свій `SaveChangesAsync`. Якщо перший зберіг, а другий впав — дані неконсистентні.

**Рішення**: Впровадити `IUnitOfWork` з єдиним `CommitAsync()`:
```csharp
public interface IUnitOfWork
{
    IBookingRepository Bookings { get; }
    IResourceRepository Resources { get; }
    Task<int> CommitAsync();
}
```

### 3.2 GenericRepository<T> приймає будь-який T
**Проблема**: Немає обмеження типу — можна передати будь-який клас, навіть не-entity.

**Рішення**: Додати constraint:
```csharp
public class GenericRepository<T> where T : class, IEntity
```

### 3.3 MockEmailService — fire-and-forget без обробки помилок
**Проблема**: `SafeSendEmailAsync` проковтує всі помилки. У реальному проєкті email може мовчки не відправитись.

**Рішення**: Для production — черга повідомлень (MassTransit / Hangfire). Для coursework рівня — прийнятно.

### 3.4 Відсутній DTO між Controller і Service
**Проблема**: `BookingCreateViewModel` (UI-шар) передається напряму в `IBookingService`. Це порушує розділення шарів — сервіс залежить від ViewModel.

**Рішення**: Ввести окремий `CreateBookingDto`:
```csharp
// Services/Dtos/CreateBookingDto.cs
public record CreateBookingDto(int ResourceId, DateTime StartTime, DateTime EndTime, string Purpose);
```

### 3.5 Відсутня валідація на рівні моделі для overlap
**Проблема**: Перевірка перекриття бронювань (`HasOverlapAsync`) — тільки в сервісі. При прямому виклику репозиторія правило можна обійти.

**Рішення**: Прийнятно для даної архітектури, оскільки репозиторій не публічний API.

---

## 4. Можливі покращення структури

| # | Покращення | Пріоритет |
|---|---|---|
| 1 | Впровадити `IUnitOfWork` для атомарності транзакцій | Високий |
| 2 | Замінити `BookingCreateViewModel` на `CreateBookingDto` в сервісному шарі | Середній |
| 3 | Додати `where T : class` constraint у `GenericRepository<T>` | Низький |
| 4 | Впровадити FluentValidation для валідації ViewModel | Середній |
| 5 | Додати AutoMapper для маппінгу ViewModel ↔ Entity ↔ DTO | Середній |

---

## 5. Висновок

Архітектура проєкту відповідає coursework-рівню та реалізує:
- **Repository Pattern** з Generic базою та специфічними розширеннями
- **Service Layer** з чітким відокремленням бізнес-логіки
- **Result Pattern** замість exception-based flow
- **Dependency Injection** через інтерфейси (всі залежності — через конструктор)
- **SOLID** принципи — в цілому дотримані

Головне архітектурне обмеження — відсутність UoW, що може призвести до
неконсистентного стану при складних транзакціях. Для навчального проєкту
це прийнятний компроміс.
