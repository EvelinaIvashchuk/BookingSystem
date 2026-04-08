using System.ComponentModel.DataAnnotations;
using BookingSystem.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BookingSystem.ViewModels;

public class CarFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(50, MinimumLength = 2)]
    [Display(Name = "Brand")]
    public string Brand { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 1)]
    [Display(Name = "Model")]
    public string Model { get; set; } = string.Empty;

    [Required]
    [Range(1990, 2030)]
    [Display(Name = "Year")]
    public int Year { get; set; } = DateTime.UtcNow.Year;

    [Required]
    [StringLength(20, MinimumLength = 2)]
    [Display(Name = "License Plate")]
    public string LicensePlate { get; set; } = string.Empty;

    [Display(Name = "Fuel Type")]
    public FuelType FuelType { get; set; } = FuelType.Petrol;

    [Display(Name = "Transmission")]
    public Transmission Transmission { get; set; } = Transmission.Manual;

    [Required]
    [Range(1, 50)]
    [Display(Name = "Seats")]
    public int Seats { get; set; } = 5;

    [Required]
    [Range(0.01, 99999.99)]
    [Display(Name = "Price Per Day (₴)")]
    public decimal PricePerDay { get; set; }

    [StringLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [StringLength(200)]
    [Display(Name = "Location")]
    public string Location { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Image URL")]
    public string? ImageUrl { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Display(Name = "Status")]
    public CarStatus Status { get; set; } = CarStatus.Available;

    public IEnumerable<SelectListItem> Categories { get; set; } = [];
}
