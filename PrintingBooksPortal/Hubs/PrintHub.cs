using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Hubs;

[AllowAnonymous] // enforcement is in OnConnectedAsync (agents have no cookie)
public class PrintHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IApiKeyService _apiKeys;
    private readonly ILogger<PrintHub> _logger;

    public PrintHub(AppDbContext db, IApiKeyService apiKeys, ILogger<PrintHub> logger)
    {
        _db = db;
        _apiKeys = apiKeys;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Path 1: Agent (API key in query "access_token" or header "X-Api-Key")
        var key = Context.GetHttpContext()?.Request.Query["access_token"].FirstOrDefault()
                  ?? Context.GetHttpContext()?.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(key))
        {
            var tenantId = await _apiKeys.ResolveTenantAsync(key);
            if (tenantId > 0)
            {
                Context.Items["TenantId"] = tenantId;
                await Groups.AddToGroupAsync(Context.ConnectionId, $"PrintAgents_{tenantId}");
                _logger.LogInformation("Agent connected for tenant {TenantId}: {ConnectionId}", tenantId, Context.ConnectionId);
                await base.OnConnectedAsync();
                return;
            }
            Context.Abort();
            return;
        }

        // Path 2: Browser (cookie auth)
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated == true && user.IsInRole("Shop"))
        {
            var tid = int.TryParse(user.FindFirstValue("TenantId"), out var t) ? t : 0;
            if (tid > 0)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Shop_{tid}");
                await base.OnConnectedAsync();
                return;
            }
        }
        Context.Abort(); // reject everything else
    }

    public async Task RequestPrint(int bookId, int copies)
    {
        var userName = Context.User?.Identity?.Name ?? "unknown";

        _logger.LogInformation("SignalR print request from {User} for book {BookId}, {Copies} copies", userName, bookId, copies);

        await Clients.Caller.SendAsync("PrintRequested", new
        {
            bookId,
            copies,
            status = "logged"
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("PrintHub client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}