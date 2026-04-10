using BookingSystem;
using BookingSystem.ViewModels;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace BookingSystem.Validators;

public class RentalCreateViewModelValidator : AbstractValidator<RentalCreateViewModel>
{
    private const int MinRentalDays  = 1;
    private const int MaxRentalDays  = 30;
    private const int MaxAdvanceDays = 60;

    public RentalCreateViewModelValidator(IStringLocalizer<SharedResources> localizer)
    {
        RuleFor(x => x.PickupDate)
            .NotEmpty()
                .WithMessage(localizer["Val_PickupRequired"].Value)
            .GreaterThanOrEqualTo(DateTime.UtcNow.Date)
                .WithMessage(localizer["Val_PickupNotPast"].Value)
            .LessThanOrEqualTo(DateTime.UtcNow.Date.AddDays(MaxAdvanceDays))
                .WithMessage(string.Format(localizer["Val_PickupAdvance"].Value, MaxAdvanceDays));

        RuleFor(x => x.ReturnDate)
            .NotEmpty()
                .WithMessage(localizer["Val_ReturnRequired"].Value)
            .GreaterThan(x => x.PickupDate)
                .When(x => x.PickupDate.HasValue)
                .WithMessage(localizer["Val_ReturnAfterPickup"].Value);

        RuleFor(x => x)
            .Must(x => !x.PickupDate.HasValue || !x.ReturnDate.HasValue ||
                       (x.ReturnDate.Value.Date - x.PickupDate.Value.Date).TotalDays >= MinRentalDays)
            .WithMessage(string.Format(localizer["Val_MinDays"].Value, MinRentalDays))
            .WithName("Duration")
            .When(x => x.PickupDate.HasValue && x.ReturnDate.HasValue);

        RuleFor(x => x)
            .Must(x => !x.PickupDate.HasValue || !x.ReturnDate.HasValue ||
                       (x.ReturnDate.Value.Date - x.PickupDate.Value.Date).TotalDays <= MaxRentalDays)
            .WithMessage(string.Format(localizer["Val_MaxDays"].Value, MaxRentalDays))
            .WithName("Duration")
            .When(x => x.PickupDate.HasValue && x.ReturnDate.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(500)
                .WithMessage(localizer["Val_NotesMax"].Value)
            .When(x => x.Notes is not null);
    }
}
