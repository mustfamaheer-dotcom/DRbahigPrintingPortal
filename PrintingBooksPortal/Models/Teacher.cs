namespace PrintingBooksPortal.Models;

public class Teacher
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EducationalBoard> Boards { get; set; } = new List<EducationalBoard>();
    public ICollection<Book> Books { get; set; } = new List<Book>();
    public ICollection<TeacherBookshopLink> BookshopLinks { get; set; } = new List<TeacherBookshopLink>();
    public ICollection<PrintLog> PrintLogs { get; set; } = new List<PrintLog>();
}
