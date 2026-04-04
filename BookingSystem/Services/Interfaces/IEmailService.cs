namespace BookingSystem.Services.Interfaces;

public interface IEmailService
{
    Task SendBookingCreatedAsync(string toEmail, string userName, string resourceName, DateTime start, DateTime end);
    Task SendBookingConfirmedAsync(string toEmail, string userName, string resourceName, DateTime start, DateTime end);
    Task SendBookingRejectedAsync(string toEmail, string userName, string resourceName, string reason);
    Task SendBookingCancelledAsync(string toEmail, string userName, string resourceName, DateTime start, DateTime end);
}
