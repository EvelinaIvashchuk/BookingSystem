using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BookingSystem.Enums;

namespace BookingSystem.Models;

public class Resource
{
    public int Id { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "Resource Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Location")]
    public string Location { get; set; } = string.Empty;

    [Required]
    [Range(1, 1000, ErrorMessage = "Capacity must be between 1 and 1000.")]
    [Display(Name = "Capacity")]
    public int Capacity { get; set; }

    [Display(Name = "Status")]
    public ResourceStatus Status { get; set; } = ResourceStatus.Available;

    [Display(Name = "Image URL")]
    [StringLength(500)]
    public string? ImageUrl { get; set; }

    // FK → Category
    // [Range] is required here: int defaults to 0 which is an invalid FK.
    // [Required] alone does not catch 0 on value types.
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category Category { get; set; } = null!;

    // Navigation
    public ICollection<Booking> Bookings { get; set; } = [];
}
