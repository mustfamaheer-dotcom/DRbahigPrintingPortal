using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;

namespace PrintingBooksPortal.Middleware;

public class TenantActivityMiddleware
{
    private readonly RequestDelegate _next;
    public TenantActivityMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantId = int.TryParse(user.FindFirstValue("TenantId"), out var tid) ? tid : 0;
            var isTenantUser = !user.IsInRole("SystemAdmin");
            if (isTenantUser && tenantId > 0)
            {
                var active = await db.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive);
                if (!active)
                {
                    context.Response.Redirect("/access-denied");
                    return;
                }
            }
        }
        await _next(context);
    }
}