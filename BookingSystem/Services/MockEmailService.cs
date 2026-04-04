using BookingSystem.Services.Interfaces;

namespace BookingSystem.Services;

/// <summary>
/// Mock email service that logs emails to the console and a text file
/// instead of sending real email. Replace with an SMTP/SendGrid
/// implementation for production.
/// </summary>
public class MockEmailService(
    ILogger<MockEmailService> logger,
    IWebHostEnvironment env) : IEmailService
{
    private readonly string _logDir = Path.Combine(env.ContentRootPath, "Logs", "Emails");

    public Task SendBookingCreatedAsync(
        string toEmail, string userName, string resourceName, DateTime start, DateTime end)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Booking Request Submitted",
            body:    $"""
                      Dear {userName},

                      Your booking request has been submitted and is awaiting admin approval.

                      Resource:  {resourceName}
                      From:      {start:dddd, dd MMM yyyy HH:mm}
                      To:        {end:dddd, dd MMM yyyy HH:mm}

                      You will be notified once the booking is confirmed or rejected.

                      — BookingSystem
                      """);
    }

    public Task SendBookingConfirmedAsync(
        string toEmail, string userName, string resourceName, DateTime start, DateTime end)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Booking Confirmed",
            body:    $"""
                      Dear {userName},

                      Great news! Your booking has been confirmed.

                      Resource:  {resourceName}
                      From:      {start:dddd, dd MMM yyyy HH:mm}
                      To:        {end:dddd, dd MMM yyyy HH:mm}

                      Please arrive on time. Have a great session!

                      — BookingSystem
                      """);
    }

    public Task SendBookingRejectedAsync(
        string toEmail, string userName, string resourceName, string reason)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Booking Rejected",
            body:    $"""
                      Dear {userName},

                      Unfortunately, your booking request for "{resourceName}" has been rejected.

                      Reason: {reason}

                      Please try a different time or resource.

                      — BookingSystem
                      """);
    }

    public Task SendBookingCancelledAsync(
        string toEmail, string userName, string resourceName, DateTime start, DateTime end)
    {
        return LogEmailAsync(
            to:      toEmail,
            subject: "Booking Cancelled",
            body:    $"""
                      Dear {userName},

                      Your booking for "{resourceName}" on {start:dd MMM yyyy HH:mm} – {end:HH:mm}
                      has been cancelled as requested. The time slot is now available for others.

                      — BookingSystem
                      """);
    }

    private async Task LogEmailAsync(string to, string subject, string body)
    {
        logger.LogInformation(
            "[MOCK EMAIL] To: {To} | Subject: {Subject}", to, subject);

        // Also persist to a file so the coursework marker can verify emails
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
