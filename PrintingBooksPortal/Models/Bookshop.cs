using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class Bookshop
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? ContactPerson { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser? BookshopUser { get; set; }
    public ICollection<TeacherBookshopLink> TeacherLinks { get; set; } = new List<TeacherBookshopLink>();
}
