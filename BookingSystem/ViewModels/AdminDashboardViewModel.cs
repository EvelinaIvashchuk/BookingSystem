using BookingSystem.Models;

namespace BookingSystem.ViewModels;

public class AdminDashboardViewModel
{
    public int TotalResources    { get; set; }
    public int AvailableResources { get; set; }
    public int TotalBookings     { get; set; }
    public int PendingBookings   { get; set; }
    public int TodaysBookings    { get; set; }

    public IEnumerable<Booking> RecentPending { get; set; } = [];
}
