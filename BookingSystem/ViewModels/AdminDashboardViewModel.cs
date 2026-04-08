using BookingSystem.Models;

namespace BookingSystem.ViewModels;

public class AdminDashboardViewModel
{
    public int TotalCars        { get; set; }
    public int AvailableCars    { get; set; }
    public int TotalRentals     { get; set; }
    public int PendingRentals   { get; set; }
    public int TodaysRentals    { get; set; }

    public IEnumerable<Rental> RecentPending { get; set; } = [];
}
