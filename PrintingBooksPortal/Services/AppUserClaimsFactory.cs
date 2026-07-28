using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class AppUserClaimsFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public AppUserClaimsFactory(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.TeacherId.HasValue)
            identity.AddClaim(new Claim("TeacherId", user.TeacherId.Value.ToString()));
        if (user.BookshopId.HasValue)
            identity.AddClaim(new Claim("BookshopId", user.BookshopId.Value.ToString()));
        return identity;
    }
}