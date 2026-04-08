using System.ComponentModel.DataAnnotations;

namespace BookingSystem.ViewModels;

public class RentalCreateViewModel
{
    // Pre-filled from the car — not posted back
    public int     CarId        { get; set; }
    public string  CarName      { get; set; } = string.Empty;
    public string  Location     { get; set; } = string.Empty;
    public decimal PricePerDay  { get; set; }
    public string  CategoryName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Pickup date is required.")]
    [Display(Name = "Pickup Date")]
    [DataType(DataType.Date)]
    public DateTime? PickupDate { get; set; }

    [Required(ErrorMessage = "Return date is required.")]
    [Display(Name = "Return Date")]
    [DataType(DataType.Date)]
    public DateTime? ReturnDate { get; set; }

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
