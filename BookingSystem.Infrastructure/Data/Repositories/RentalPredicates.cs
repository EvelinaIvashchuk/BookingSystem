using System.Linq.Expressions;
using BookingSystem.Enums;
using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

/// <summary>Query predicates shared by repositories so availability is defined in one place.</summary>
internal static class RentalPredicates
{
    /// <summary>
    /// An active (Pending or Confirmed) rental that intersects [pickup, return).
    /// Back-to-back rentals (return day = next pickup day) do not overlap.
    /// </summary>
    public static Expression<Func<Rental, bool>> ActiveOverlapping(DateTime pickupDate, DateTime returnDate) =>
        r => (r.Status == RentalStatus.Pending || r.Status == RentalStatus.Confirmed)
             && r.PickupDate < returnDate
             && r.ReturnDate > pickupDate;
}
