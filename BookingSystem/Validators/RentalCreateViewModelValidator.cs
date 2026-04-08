using BookingSystem.ViewModels;
using FluentValidation;

namespace BookingSystem.Validators;

public class RentalCreateViewModelValidator : AbstractValidator<RentalCreateViewModel>
{
    private const int MinRentalDays  = 1;
    private const int MaxRentalDays  = 30;
    private const int MaxAdvanceDays = 60;

    public RentalCreateViewModelValidator()
    {
        RuleFor(x => x.PickupDate)
            .NotEmpty()
                .WithMessage("Pickup date is required.")
            .GreaterThanOrEqualTo(DateTime.UtcNow.Date)
                .WithMessage("Pickup date cannot be in the past.")
            .LessThanOrEqualTo(DateTime.UtcNow.Date.AddDays(MaxAdvanceDays))
                .WithMessage($"Rental cannot be booked more than {MaxAdvanceDays} days in advance.");

        RuleFor(x => x.ReturnDate)
            .NotEmpty()
                .WithMessage("Return date is required.")
            .GreaterThan(x => x.PickupDate)
                .When(x => x.PickupDate.HasValue)
                .WithMessage("Return date must be after pickup date.");

        RuleFor(x => x)
            .Must(x => !x.PickupDate.HasValue || !x.ReturnDate.HasValue ||
                       (x.ReturnDate.Value.Date - x.PickupDate.Value.Date).TotalDays >= MinRentalDays)
            .WithMessage($"Rental must be at least {MinRentalDays} day(s).")
            .WithName("Duration")
            .When(x => x.PickupDate.HasValue && x.ReturnDate.HasValue);

        RuleFor(x => x)
            .Must(x => !x.PickupDate.HasValue || !x.ReturnDate.HasValue ||
                       (x.ReturnDate.Value.Date - x.PickupDate.Value.Date).TotalDays <= MaxRentalDays)
            .WithMessage($"Rental cannot exceed {MaxRentalDays} days.")
            .WithName("Duration")
            .When(x => x.PickupDate.HasValue && x.ReturnDate.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(500)
                .WithMessage("Notes cannot exceed 500 characters.")
            .When(x => x.Notes is not null);
    }
}
