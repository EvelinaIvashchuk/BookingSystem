using BookingSystem.Models;
using BookingSystem.Services.Dtos;

namespace BookingSystem.Services.Interfaces;

public interface IRentalService
{
    Task<ServiceResult<Rental>> CreateRentalAsync(string userId, CreateRentalDto dto);
    Task<ServiceResult> CancelRentalAsync(int rentalId, string userId);
    Task<ServiceResult> ConfirmRentalAsync(int rentalId, string? adminNote);
    Task<ServiceResult> RejectRentalAsync(int rentalId, string adminNote);
    Task<IEnumerable<Rental>> GetUserRentalsAsync(string userId);
    Task<IEnumerable<Rental>> GetAllRentalsAsync();
    Task<Rental?> GetRentalByIdAsync(int rentalId);
}
