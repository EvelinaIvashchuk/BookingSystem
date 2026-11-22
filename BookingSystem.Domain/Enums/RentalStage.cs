namespace BookingSystem.Enums;

/// <summary>
/// Status of a rental as the user sees it: the stored <see cref="RentalStatus"/> plus the dates.
/// A confirmed rental is <see cref="Active"/> from the pickup day to the return day
/// and <see cref="Completed"/> after the return day.
/// </summary>
public enum RentalStage
{
    Pending = 0,
    Confirmed = 1,
    Active = 2,
    Completed = 3,
    Cancelled = 4,
    Rejected = 5
}
