using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;

namespace PrintingBooksPortal.Hubs;

[AllowAnonymous]
public class PrintHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PrintHub> _logger;

    public PrintHub(AppDbContext db, IConfiguration configuration, ILogger<PrintHub> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RequestPrint(PrintJobRequest request)
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
            throw new HubException("Authentication required.");

        var userId = Context.UserIdentifier ?? Context.ConnectionId;
        var userName = Context.User?.Identity?.Name ?? "unknown";

        _logger.LogInformation("SignalR print request from {User} for book {BookId}, {Copies} copies, printer={Printer}, orientation={Orientation}",
            userName, request.BookId, request.Copies, request.PrinterName, request.Orientation);

        await Clients.Caller.SendAsync("PrintStatusChanged", new
        {
            jobId = request.JobId,
            status = "queued",
            message = "Print job queued and sent to local agent."
        });

        await Clients.Group("PrintAgents").SendAsync("NewPrintJob", request);
    }

    public async Task RegisterAsAgent()
    {
        var apiKey = Context.GetHttpContext()?.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Agent registration rejected: missing API key from {ConnectionId}", Context.ConnectionId);
            throw new HubException("Invalid API key.");
        }

        var link = await _db.TeacherBookshopLinks
            .FirstOrDefaultAsync(l => l.UniqueApiKey == apiKey && l.IsActive);

        if (link == null)
        {
            _logger.LogWarning("Agent registration rejected: invalid API key from {ConnectionId}", Context.ConnectionId);
            throw new HubException("Invalid API key.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, "PrintAgents");
        _logger.LogInformation("Agent registered for Teacher {TeacherId}: {ConnectionId}", link.TeacherId, Context.ConnectionId);
        await Clients.Caller.SendAsync("AgentRegistered", new { connectionId = Context.ConnectionId });
    }

    public async Task UpdateJobStatus(string jobId, string status, string? message = null)
    {
        var apiKey = Context.GetHttpContext()?.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Job status update rejected: missing API key from {ConnectionId}", Context.ConnectionId);
            throw new HubException("Invalid API key.");
        }

        var link = await _db.TeacherBookshopLinks
            .FirstOrDefaultAsync(l => l.UniqueApiKey == apiKey && l.IsActive);

        if (link == null)
        {
            _logger.LogWarning("Job status update rejected: invalid API key from {ConnectionId}", Context.ConnectionId);
            throw new HubException("Invalid API key.");
        }

        _logger.LogInformation("Job {JobId} status update: {Status} ({Message})", jobId, status, message ?? "");

        await Clients.All.SendAsync("PrintStatusChanged", new
        {
            jobId,
            status,
            message
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

public class PrintJobRequest
{
    public string JobId { get; set; } = "";
    public int BookId { get; set; }
    public int Copies { get; set; } = 1;
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Orientation { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}
