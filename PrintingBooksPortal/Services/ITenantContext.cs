using System.Security.Claims;

namespace PrintingBooksPortal.Services;

public interface ITenantContext
{
    int TenantId { get; }                 // 0 = no tenant (SystemAdmin or unauthenticated)
    bool IsSystemAdmin { get; }
    void Initialize(ClaimsPrincipal user); // called by Blazor circuits (claims only, no DB)
}