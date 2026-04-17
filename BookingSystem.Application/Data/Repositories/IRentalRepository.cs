using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

public interface IRentalRepository : IGenericRepository<Rental>
{
    Task<IEnumerable<Rental>> GetUserRentalsAsync(string userId);
    Task<IEnumerable<Rental>> GetAllWithDetailsAsync();
    Task<Rental?> GetWithDetailsAsync(int rentalId);
    Task<bool> HasOverlapAsync(int carId, DateTime pickupDate, DateTime returnDate, int? excludeRentalId = null);
    Task<int> GetActiveRentalCountAsync(string userId);
    Task<IEnumerable<(DateTime Pickup, DateTime Return)>> GetBookedRangesForCarAsync(int carId);
}
