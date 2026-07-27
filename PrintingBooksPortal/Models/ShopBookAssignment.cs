namespace PrintingBooksPortal.Models;

public class ShopBookAssignment
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    public int BookId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public Shop Shop { get; set; } = null!;
    public Book Book { get; set; } = null!;
}
