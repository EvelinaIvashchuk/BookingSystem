using BookingSystem.Enums;

namespace BookingSystem.Services.Dtos;

public record RentalDto(int Id, int CarId, string CarName, DateTime PickupDate, DateTime ReturnDate, decimal TotalPrice,
    string? Notes, RentalStatus Status, DateTime CreatedAt);
