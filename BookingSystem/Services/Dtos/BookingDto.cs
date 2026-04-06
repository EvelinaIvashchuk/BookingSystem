using BookingSystem.Enums;

namespace BookingSystem.Services.Dtos;

/// <summary>
/// Read-only DTO з даними бронювання, який повертає сервіс.
/// Ізолює View від прямого доступу до Entity моделей.
/// </summary>
/// <param name="Id">Унікальний ідентифікатор бронювання.</param>
/// <param name="ResourceId">ID заброньованого ресурсу.</param>
/// <param name="ResourceName">Назва ресурсу (денормалізована для зручності).</param>
/// <param name="StartTime">Час початку бронювання.</param>
/// <param name="EndTime">Час завершення бронювання.</param>
/// <param name="Purpose">Мета бронювання.</param>
/// <param name="Status">Поточний статус бронювання.</param>
/// <param name="CreatedAt">Дата створення бронювання.</param>
public record BookingDto(
    int           Id,
    int           ResourceId,
    string        ResourceName,
    DateTime      StartTime,
    DateTime      EndTime,
    string?       Purpose,
    BookingStatus Status,
    DateTime      CreatedAt);
