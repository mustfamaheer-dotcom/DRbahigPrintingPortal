using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class TenantApiKey
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    [Required, MaxLength(64)] public string KeyHash { get; set; } = string.Empty;
    [Required, MaxLength(8)] public string KeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}