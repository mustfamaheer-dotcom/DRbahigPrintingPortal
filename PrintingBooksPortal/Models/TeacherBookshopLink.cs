using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class TeacherBookshopLink
{
    public int Id { get; set; }

    public int TeacherId { get; set; }
    public int BookshopId { get; set; }

    [Required, MaxLength(128)]
    public string UniqueApiKey { get; set; } = string.Empty;

    public int CopiesPrinted { get; set; }
    public DateTime LastResetDate { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Teacher Teacher { get; set; } = null!;
    public Bookshop Bookshop { get; set; } = null!;
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<PrintLog> PrintLogs { get; set; } = new List<PrintLog>();
}
