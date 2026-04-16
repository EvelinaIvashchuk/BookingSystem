using System.ComponentModel.DataAnnotations;

namespace BookingSystem.ViewModels;

public class RegisterViewModel
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    [Display(Name = "Account_FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 2)]
    [Display(Name = "Account_LastName")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Account_Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Account_Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Account_ConfirmPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
