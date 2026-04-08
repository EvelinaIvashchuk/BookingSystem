using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BookingSystem.Enums;

namespace BookingSystem.Models;

public class Rental
{
    public int Id { get; set; }

    // FK → ApplicationUser
    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    // FK → Car
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a car.")]
    [Display(Name = "Car")]
    public int CarId { get; set; }

    [ForeignKey(nameof(CarId))]
    public Car Car { get; set; } = null!;

    [Required(ErrorMessage = "Pickup date is required.")]
    [Display(Name = "Pickup Date")]
    [DataType(DataType.Date)]
    public DateTime? PickupDate { get; set; }

    [Required(ErrorMessage = "Return date is required.")]
    [Display(Name = "Return Date")]
    [DataType(DataType.Date)]
    public DateTime? ReturnDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "Total Price (₴)")]
    public decimal TotalPrice { get; set; }

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Status")]
    public RentalStatus Status { get; set; } = RentalStatus.Pending;

    [Display(Name = "Created At")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "Cancelled At")]
    public DateTime? CancelledAt { get; set; }

    [StringLength(500)]
    [Display(Name = "Admin Note")]
    public string? AdminNote { get; set; }

    // Computed helpers (not mapped)
    [NotMapped]
    public int? DurationDays => PickupDate.HasValue && ReturnDate.HasValue
        ? (int)(ReturnDate.Value.Date - PickupDate.Value.Date).TotalDays
        : null;

    [NotMapped]
    public bool CanBeCancelledByUser =>
        Status is RentalStatus.Pending or RentalStatus.Confirmed
        && PickupDate.HasValue
        && PickupDate.Value > DateTime.UtcNow;
}
