using Microsoft.AspNetCore.Identity;

namespace PrintingBooksPortal.Models;

public class ApplicationUser : IdentityUser
{
    public int? ShopId { get; set; }
    public int? TenantId { get; set; }
    public bool MustChangePassword { get; set; }
    public string? FullName { get; set; }
    public Shop? Shop { get; set; }
    public Tenant? Tenant { get; set; }
}