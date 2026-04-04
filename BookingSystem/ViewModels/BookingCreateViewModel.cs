using System.ComponentModel.DataAnnotations;

namespace BookingSystem.ViewModels;

public class BookingCreateViewModel
{
    // Pre-filled from the resource — not posted back
    public int    ResourceId   { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string Location     { get; set; } = string.Empty;
    public int    Capacity     { get; set; }
    public string CategoryName { get; set; } = string.Empty;

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
}
