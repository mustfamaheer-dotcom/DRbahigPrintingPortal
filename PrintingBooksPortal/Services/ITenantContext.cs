namespace PrintingBooksPortal.Services;

public interface ITenantContext
{
    int? TeacherId { get; }
    bool IsAdmin { get; }
    string? UserId { get; }
}

public class TenantContext : ITenantContext
{
    public int? TeacherId { get; set; }
    public bool IsAdmin { get; set; }
    public string? UserId { get; set; }
}
