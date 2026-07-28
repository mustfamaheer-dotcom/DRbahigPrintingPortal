using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class PrintLog
{
    public int Id { get; set; }

    public int TeacherId { get; set; }
    public int? TeacherBookshopLinkId { get; set; }
    public int BookId { get; set; }

    [MaxLength(50)]
    public string ShopName { get; set; } = string.Empty;

    [MaxLength(300)]
    public string BookTitle { get; set; } = string.Empty;

    public int Copies { get; set; } = 1;

    [MaxLength(100)]
    public string? PrintedByUserId { get; set; }

    [MaxLength(100)]
    public string? PrintedByUserName { get; set; }

    [MaxLength(50)]
    public string? IPAddress { get; set; }

    public DateTime PrintedAt { get; set; } = DateTime.UtcNow;

    public Teacher Teacher { get; set; } = null!;
    public TeacherBookshopLink? TeacherBookshopLink { get; set; }
    public Book Book { get; set; } = null!;
}
