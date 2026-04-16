namespace BookingSystem.Services.Interfaces;

public interface IEmailService
{
    Task SendRentalCreatedAsync(string toEmail, string userName, string carName, DateTime pickupDate, DateTime returnDate);
    Task SendRentalConfirmedAsync(string toEmail, string userName, string carName, DateTime pickupDate, DateTime returnDate);
    Task SendRentalRejectedAsync(string toEmail, string userName, string carName, string reason);
    Task SendRentalCancelledAsync(string toEmail, string userName, string carName, DateTime pickupDate, DateTime returnDate);
}
