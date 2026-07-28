using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public enum InvoiceStatus
{
    Pending,
    Paid
}

public class Invoice
{
    public int Id { get; set; }

    public int TeacherBookshopLinkId { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public int TotalCopies { get; set; }

    [Required, MaxLength(50)]
    public string Currency { get; set; } = "EGP";

    public decimal TotalAmount { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    public TeacherBookshopLink Link { get; set; } = null!;
}
