using System.ComponentModel.DataAnnotations;

namespace BookingSystem.ViewModels;

public class AdminRejectViewModel
{
    public int    BookingId    { get; set; }

    // Display-only context shown on the rejection confirmation page
    public string ResourceName { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string TimeSlot     { get; set; } = string.Empty;

    [Required(ErrorMessage = "A rejection reason is required.")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Reason must be at least 5 characters.")]
    [Display(Name = "Rejection Reason")]
    public string AdminNote { get; set; } = string.Empty;
}
