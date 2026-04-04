using System.ComponentModel.DataAnnotations;
using BookingSystem.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BookingSystem.ViewModels;

/// <summary>Used for both Create and Edit resource forms in the Admin area.</summary>
public class ResourceFormViewModel
{
    public int Id { get; set; }   // 0 on create

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

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [StringLength(500)]
    [Display(Name = "Image URL")]
    public string? ImageUrl { get; set; }

    [Display(Name = "Status")]
    public ResourceStatus Status { get; set; } = ResourceStatus.Available;

    // Populated by the controller for the <select> list — never posted
    public IEnumerable<SelectListItem> Categories { get; set; } = [];
}
