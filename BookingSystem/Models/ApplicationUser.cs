using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace BookingSystem.Models;

public class ApplicationUser : IdentityUser
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 2)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Display(Name = "Member Since")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Booking> Bookings { get; set; } = [];

    // Computed helper (not mapped)
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string FullName => $"{FirstName} {LastName}";
}
