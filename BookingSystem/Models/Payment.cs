using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BookingSystem.Enums;

namespace BookingSystem.Models;

public class Payment
{
    public int Id { get; set; }

    // FK → Booking (one-to-one)
    [Required]
    public int BookingId { get; set; }

    [ForeignKey(nameof(BookingId))]
    public Booking Booking { get; set; } = null!;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    [Range(0.01, 99999.99, ErrorMessage = "Amount must be greater than zero.")]
    [Display(Name = "Amount (£)")]
    public decimal Amount { get; set; }

    [Display(Name = "Payment Status")]
    public PaymentStatus Status { get; set; } = PaymentStatus.Unpaid;

    [Display(Name = "Paid At")]
    public DateTime? PaidAt { get; set; }

    [StringLength(100)]
    [Display(Name = "Transaction Reference")]
    public string? TransactionReference { get; set; }

    [Display(Name = "Created At")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
