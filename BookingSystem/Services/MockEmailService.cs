using BookingSystem.Services.Interfaces;

namespace BookingSystem.Services;

public class MockEmailService(ILogger<MockEmailService> logger, IWebHostEnvironment env) : IEmailService
{
    private readonly string _logDir = Path.Combine(env.ContentRootPath, "Logs", "Emails");

    public Task SendRentalCreatedAsync(
        string toEmail, string userName, string carName, DateTime pickupDate, DateTime returnDate)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Rental Request Submitted",
            body:    $"""
                      Dear {userName},

                      Your rental request has been submitted and is awaiting admin approval.

                      Car:         {carName}
                      Pickup:      {pickupDate:dddd, dd MMM yyyy}
                      Return:      {returnDate:dddd, dd MMM yyyy}

                      You will be notified once the rental is confirmed or rejected.

                      — CarRental
                      """);
    }

    public Task SendRentalConfirmedAsync(
        string toEmail, string userName, string carName, DateTime pickupDate, DateTime returnDate)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Rental Confirmed",
            body:    $"""
                      Dear {userName},

                      Great news! Your rental has been confirmed.

                      Car:         {carName}
                      Pickup:      {pickupDate:dddd, dd MMM yyyy}
                      Return:      {returnDate:dddd, dd MMM yyyy}

                      Please arrive on time to pick up your car. Enjoy your drive!

                      — CarRental
                      """);
    }

    public Task SendRentalRejectedAsync(
        string toEmail, string userName, string carName, string reason)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Rental Rejected",
            body:    $"""
                      Dear {userName},

                      Unfortunately, your rental request for "{carName}" has been rejected.

                      Reason: {reason}

                      Please try different dates or another car.

                      — CarRental
                      """);
    }

    public Task SendRentalCancelledAsync(
        string toEmail, string userName, string carName, DateTime pickupDate, DateTime returnDate)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Rental Cancelled",
            body:    $"""
                      Dear {userName},

                      Your rental for "{carName}" ({pickupDate:dd MMM yyyy} – {returnDate:dd MMM yyyy})
                      has been cancelled as requested. The car is now available for others.

                      — CarRental
                      """);
    }

    private async Task LogEmailAsync(string to, string subject, string body)
    {
        logger.LogInformation(
            "[MOCK EMAIL] To: {To} | Subject: {Subject}", to, subject);

        Directory.CreateDirectory(_logDir);
        var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{subject.Replace(' ', '_')}.txt";
        var path     = Path.Combine(_logDir, filename);

        var content = $"""
                       ===================================
                       MOCK EMAIL — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}
                       ===================================
                       To:      {to}
                       Subject: {subject}
                       -----------------------------------

                       {body}

                       ===================================
                       """;

        await File.WriteAllTextAsync(path, content);
    }
}
