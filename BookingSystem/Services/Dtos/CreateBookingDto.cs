namespace BookingSystem.Services.Dtos;

/// <summary>
/// DTO для передачі даних створення бронювання від контролера до сервісу.
/// Відокремлює сервісний шар від MVC-специфічного BookingCreateViewModel.
/// </summary>
/// <param name="ResourceId">Ідентифікатор ресурсу, який бронюється.</param>
/// <param name="StartTime">Дата та час початку бронювання.</param>
/// <param name="EndTime">Дата та час завершення бронювання.</param>
/// <param name="Purpose">Мета/опис бронювання (опціонально).</param>
public record CreateBookingDto(
    int      ResourceId,
    DateTime StartTime,
    DateTime EndTime,
    string?  Purpose);
