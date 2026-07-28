using System.Security.Claims;

namespace PrintingBooksPortal.Services;

public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true && tenantContext is TenantContext ctx)
        {
            ctx.InitializeFromPrincipal(context.User);
        }
        await _next(context);
    }
}