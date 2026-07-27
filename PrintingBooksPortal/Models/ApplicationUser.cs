using Microsoft.AspNetCore.Identity;

namespace PrintingBooksPortal.Models;

public class ApplicationUser : IdentityUser
{
    public int? ShopId { get; set; }
    public string? FullName { get; set; }
    public Shop? Shop { get; set; }
}
