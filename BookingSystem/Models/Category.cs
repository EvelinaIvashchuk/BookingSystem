using System.ComponentModel.DataAnnotations;

namespace BookingSystem.Models;

public class Category
{
    public int Id { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "Category Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }

    // Navigation
    public ICollection<Car> Cars { get; set; } = [];
}
