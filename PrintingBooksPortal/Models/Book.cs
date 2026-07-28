using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class Book
{
    public int Id { get; set; }

    public int? TeacherId { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    public int BoardId { get; set; }

    [Required, MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? OriginalFileName { get; set; }

    public long FileSizeBytes { get; set; }

    public int PageCount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Teacher? Teacher { get; set; }
    public EducationalBoard Board { get; set; } = null!;
    public ICollection<PrintLog> PrintLogs { get; set; } = new List<PrintLog>();
}
