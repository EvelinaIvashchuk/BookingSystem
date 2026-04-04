using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BookingSystem.Enums;

namespace BookingSystem.Models;

public class Booking
{
    public int Id { get; set; }

    // FK → ApplicationUser
    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    // FK → Resource
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a resource.")]
    [Display(Name = "Resource")]
    public int ResourceId { get; set; }

    [ForeignKey(nameof(ResourceId))]
    public Resource Resource { get; set; } = null!;

    // DateTime? + [Required] correctly catches missing/unsubmitted date fields.
    // Plain DateTime with [Required] does nothing — value types are never null.
    [Required(ErrorMessage = "Start time is required.")]
    [Display(Name = "Start Time")]
    [DataType(DataType.DateTime)]
    public DateTime? StartTime { get; set; }

    [Required(ErrorMessage = "End time is required.")]
    [Display(Name = "End Time")]
    [DataType(DataType.DateTime)]
    public DateTime? EndTime { get; set; }

    [StringLength(500)]
    [Display(Name = "Purpose / Notes")]
    public string? Purpose { get; set; }

    [Display(Name = "Status")]
    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    [Display(Name = "Booked At")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "Cancelled At")]
    public DateTime? CancelledAt { get; set; }

    // Set by Admin when confirming or rejecting
    [StringLength(500)]
    [Display(Name = "Admin Note")]
    public string? AdminNote { get; set; }

    // Computed helpers (not mapped)
    [NotMapped]
    public TimeSpan? Duration => StartTime.HasValue && EndTime.HasValue
        ? EndTime.Value - StartTime.Value
        : null;

    [NotMapped]
    public bool CanBeCancelledByUser =>
        Status is BookingStatus.Pending or BookingStatus.Confirmed
        && StartTime.HasValue
        && StartTime.Value > DateTime.UtcNow;
}
