using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PrintingBooksPortal.Data;

namespace PrintingBooksPortal.Hubs;

[Authorize(Roles = "Shop")]
public class PrintHub : Hub
{
    private readonly AppDbContext _db;
    private readonly ILogger<PrintHub> _logger;

    public PrintHub(AppDbContext db, ILogger<PrintHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RequestPrint(int bookId, int copies)
    {
        var userId = Context.UserIdentifier ?? Context.ConnectionId;
        var userName = Context.User?.Identity?.Name ?? "unknown";

        _logger.LogInformation("SignalR print request from {User} for book {BookId}, {Copies} copies", userName, bookId, copies);

        await Clients.Caller.SendAsync("PrintRequested", new
        {
            bookId,
            copies,
            status = "logged"
        });
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("PrintHub client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("PrintHub client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
