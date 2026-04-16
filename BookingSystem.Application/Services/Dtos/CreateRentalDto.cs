namespace BookingSystem.Services.Dtos;

public record CreateRentalDto(int CarId, DateTime PickupDate, DateTime ReturnDate, string? Notes);
