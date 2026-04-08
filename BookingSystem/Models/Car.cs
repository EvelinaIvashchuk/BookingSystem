using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BookingSystem.Enums;

namespace BookingSystem.Models;

public class Car
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
    [Range(1990, 2030, ErrorMessage = "Year must be between 1990 and 2030.")]
    [Display(Name = "Year")]
    public int Year { get; set; }

    [Required]
    [StringLength(20, MinimumLength = 2)]
    [Display(Name = "License Plate")]
    public string LicensePlate { get; set; } = string.Empty;

    [Display(Name = "Fuel Type")]
    public FuelType FuelType { get; set; } = FuelType.Petrol;

    [Display(Name = "Transmission")]
    public Transmission Transmission { get; set; } = Transmission.Manual;

    [Required]
    [Range(1, 50, ErrorMessage = "Seats must be between 1 and 50.")]
    [Display(Name = "Seats")]
    public int Seats { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    [Range(0.01, 99999.99, ErrorMessage = "Price must be greater than zero.")]
    [Display(Name = "Price Per Day (₴)")]
    public decimal PricePerDay { get; set; }

    [StringLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [StringLength(200)]
    [Display(Name = "Location")]
    public string Location { get; set; } = string.Empty;

    [Display(Name = "Status")]
    public CarStatus Status { get; set; } = CarStatus.Available;

    [Display(Name = "Image URL")]
    [StringLength(500)]
    public string? ImageUrl { get; set; }

    // FK → Category
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category Category { get; set; } = null!;

    // Navigation
    public ICollection<Rental> Rentals { get; set; } = [];

    // Computed
    [NotMapped]
    public string FullName => $"{Brand} {Model} ({Year})";
}
