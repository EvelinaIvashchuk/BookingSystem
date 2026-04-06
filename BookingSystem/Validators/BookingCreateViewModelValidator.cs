using BookingSystem.ViewModels;
using FluentValidation;

namespace BookingSystem.Validators;

/// <summary>
/// FluentValidation валідатор для форми створення бронювання.
///
/// Переносить бізнес-правила валідації з DataAnnotations у окремий клас,
/// що покращує тестованість та підтримку коду.
/// </summary>
public class BookingCreateViewModelValidator : AbstractValidator<BookingCreateViewModel>
{
    private const int MinDurationMinutes = 30;
    private const int MaxDurationHours   = 8;
    private const int MaxAdvanceDays     = 30;

    public BookingCreateViewModelValidator()
    {
        // ── StartTime ────────────────────────────────────────────────────────
        RuleFor(x => x.StartTime)
            .NotEmpty()
                .WithMessage("Start time is required.")
            .GreaterThan(DateTime.UtcNow)
                .WithMessage("Start time must be in the future.")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(MaxAdvanceDays))
                .WithMessage($"Booking cannot be made more than {MaxAdvanceDays} days in advance.");

        // ── EndTime ──────────────────────────────────────────────────────────
        RuleFor(x => x.EndTime)
            .NotEmpty()
                .WithMessage("End time is required.")
            .GreaterThan(x => x.StartTime)
                .When(x => x.StartTime.HasValue)
                .WithMessage("End time must be after start time.");

        // ── Duration (cross-field) ────────────────────────────────────────────
        RuleFor(x => x)
            .Must(x => !x.StartTime.HasValue || !x.EndTime.HasValue ||
                       (x.EndTime.Value - x.StartTime.Value).TotalMinutes >= MinDurationMinutes)
            .WithMessage($"Booking must be at least {MinDurationMinutes} minutes long.")
            .WithName("Duration")
            .When(x => x.StartTime.HasValue && x.EndTime.HasValue);

        RuleFor(x => x)
            .Must(x => !x.StartTime.HasValue || !x.EndTime.HasValue ||
                       (x.EndTime.Value - x.StartTime.Value).TotalHours <= MaxDurationHours)
            .WithMessage($"Booking cannot exceed {MaxDurationHours} hours.")
            .WithName("Duration")
            .When(x => x.StartTime.HasValue && x.EndTime.HasValue);

        // ── Purpose ──────────────────────────────────────────────────────────
        RuleFor(x => x.Purpose)
            .MaximumLength(500)
                .WithMessage("Purpose cannot exceed 500 characters.")
            .When(x => x.Purpose is not null);
    }
}
