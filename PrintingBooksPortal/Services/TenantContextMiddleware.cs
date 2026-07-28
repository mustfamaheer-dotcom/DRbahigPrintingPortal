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
            await ctx.InitializeAsync();
        }
        await _next(context);
    }
}
