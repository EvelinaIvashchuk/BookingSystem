using System.ComponentModel.DataAnnotations;

namespace BookingSystem.ViewModels;

public class LoginViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Account_Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Account_Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Account_RememberMe")]
    public bool RememberMe { get; set; }

    // Redirect target after successful login
    public string? ReturnUrl { get; set; }
}
