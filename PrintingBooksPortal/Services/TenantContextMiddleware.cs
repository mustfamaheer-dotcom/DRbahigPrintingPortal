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
        var ctx = (TenantContext)tenantContext;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var teacherIdClaim = context.User.FindFirst("TeacherId");
            if (teacherIdClaim != null && int.TryParse(teacherIdClaim.Value, out var teacherId))
                ctx.TeacherId = teacherId;

            ctx.IsAdmin = context.User.IsInRole("Admin");
            ctx.UserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        await _next(context);
    }
}
