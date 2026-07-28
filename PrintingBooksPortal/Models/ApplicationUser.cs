using Microsoft.AspNetCore.Identity;

namespace PrintingBooksPortal.Models;

public enum UserRole
{
    Admin,
    Teacher,
    BookshopManager
}

public class ApplicationUser : IdentityUser
{
    public UserRole Role { get; set; } = UserRole.Teacher;
    public int? TeacherId { get; set; }
    public string? FullName { get; set; }

    public Teacher? Teacher { get; set; }
}
