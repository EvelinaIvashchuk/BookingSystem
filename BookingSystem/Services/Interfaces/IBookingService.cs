using BookingSystem.Models;
using BookingSystem.Services.Dtos;

namespace BookingSystem.Services.Interfaces;

/// <summary>
/// Сервіс для управління бронюваннями.
/// Містить всю бізнес-логіку: валідацію правил, перевірку перетинів,
/// ліміти та сповіщення електронною поштою.
/// </summary>
public interface IBookingService
{
    /// <summary>
    /// Створює бронювання після перевірки всіх бізнес-правил.
    /// Приймає DTO для відокремлення сервісу від MVC-шару.
    /// </summary>
    /// <param name="userId">ID автентифікованого користувача.</param>
    /// <param name="dto">Дані бронювання у вигляді DTO.</param>
    Task<ServiceResult<Booking>> CreateBookingAsync(string userId, CreateBookingDto dto);

    /// <summary>Скасовує бронювання, що належить <paramref name="userId"/>.</summary>
    Task<ServiceResult> CancelBookingAsync(int bookingId, string userId);

    /// <summary>Адмін: підтверджує бронювання зі статусом Pending.</summary>
    Task<ServiceResult> ConfirmBookingAsync(int bookingId, string? adminNote);

    /// <summary>Адмін: відхиляє бронювання (примітка обов'язкова).</summary>
    Task<ServiceResult> RejectBookingAsync(int bookingId, string adminNote);

    /// <summary>Повертає всі бронювання користувача, від новіших до старіших.</summary>
    Task<IEnumerable<Booking>> GetUserBookingsAsync(string userId);

    /// <summary>Повертає всі бронювання в системі (для адмін-панелі).</summary>
    Task<IEnumerable<Booking>> GetAllBookingsAsync();

    /// <summary>Повертає одне бронювання з повними навігаційними даними або null.</summary>
    Task<Booking?> GetBookingByIdAsync(int bookingId);
}
