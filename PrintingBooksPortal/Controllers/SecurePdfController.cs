using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Hubs;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/pdf")]
public class SecurePdfController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FileStorageService _fileStorage;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PrintLoggingService _printLogging;
    private readonly IWatermarkService _watermarkService;
    private readonly ISettingsService _settingsService;
    private readonly PrintTokenService _printTokenService;
    private readonly IPdfSecurityService _pdfSecurity;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecurePdfController> _logger;
    private readonly IHubContext<PrintHub> _hubContext;

    public SecurePdfController(
        AppDbContext db,
        FileStorageService fileStorage,
        UserManager<ApplicationUser> userManager,
        PrintLoggingService printLogging,
        IWatermarkService watermarkService,
        ISettingsService settingsService,
        PrintTokenService printTokenService,
        IPdfSecurityService pdfSecurity,
        IConfiguration configuration,
        ILogger<SecurePdfController> logger,
        IHubContext<PrintHub> hubContext)
    {
        _db = db;
        _fileStorage = fileStorage;
        _userManager = userManager;
        _printLogging = printLogging;
        _watermarkService = watermarkService;
        _settingsService = settingsService;
        _printTokenService = printTokenService;
        _pdfSecurity = pdfSecurity;
        _configuration = configuration;
        _logger = logger;
        _hubContext = hubContext;
    }

    private async Task<(Book? book, ApplicationUser? user)> ValidateAccess(int bookId)
    {
        var book = await _db.Books.Include(b => b.Board).FirstOrDefaultAsync(b => b.Id == bookId && b.IsActive);
        if (book == null)
            return (null, null);

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return (null, null);

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        if (isAdmin)
            return (book, user);

        var isTeacher = await _userManager.IsInRoleAsync(user, "Teacher");
        if (isTeacher && book.TeacherId == user.TeacherId)
            return (book, user);

        return (null, null);
    }

    private async Task<bool> IsJobOwnerAsync(string jobId, ClaimsPrincipal user)
    {
        if (PendingPrintJobs.Jobs.TryGetValue(jobId, out var info))
        {
            var appUser = await _userManager.GetUserAsync(user);
            if (appUser == null) return false;

            var isAdmin = await _userManager.IsInRoleAsync(appUser, "Admin");
            return isAdmin || info.TeacherId == appUser.TeacherId;
        }
        return false;
    }

    private async Task<TeacherBookshopLink?> ResolveAgentLinkAsync()
    {
        var providedKey = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(providedKey))
            return null;

        return await _db.TeacherBookshopLinks
            .Include(l => l.Bookshop)
            .FirstOrDefaultAsync(l => l.UniqueApiKey == providedKey && l.IsActive);
    }

    private async Task<byte[]?> GetOriginalPdfBytes(int bookId)
    {
        var filePath = _fileStorage.GetFilePath((await _db.Books.FindAsync(bookId))?.FilePath ?? "");
        if (!System.IO.File.Exists(filePath))
            return null;
        return await System.IO.File.ReadAllBytesAsync(filePath);
    }

    [HttpGet("view-secure/{bookId}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> ViewSecurePdf(int bookId)
    {
        var (book, user) = await ValidateAccess(bookId);
        if (book == null || user == null)
            return NotFound(new { error = "Access Denied: You are not authorized to view this book." });

        var teacher = user.TeacherId != null ? await _db.Teachers.FindAsync(user.TeacherId.Value) : null;
        var teacherName = teacher?.Name ?? "Unknown Teacher";

        _logger.LogInformation("User {UserId} viewing secure PDF for book {BookId}", user.Id, bookId);

        try
        {
            var originalBytes = await System.IO.File.ReadAllBytesAsync(_fileStorage.GetFilePath(book.FilePath));
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermark(originalBytes, teacherName, user.UserName ?? "Unknown", DateTime.UtcNow, watermarkEnabled, watermarkText);
            return Ok(new { pdfData = Convert.ToBase64String(watermarked), watermarkEnabled });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heavy watermarking failed for book {BookId}", bookId);
            return StatusCode(500, new { error = "Failed to process PDF for viewing." });
        }
    }

    [HttpPost("process-print")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> ProcessPrint([FromBody] ProcessPrintRequest request)
    {
        _logger.LogInformation("Received Print Settings: {Settings}", JsonSerializer.Serialize(request));

        var user = await _userManager.GetUserAsync(User);
        if (user?.TeacherId == null)
            return Unauthorized(new { success = false, error = "Access Denied: You are not authorized to print." });

        var isOwner = await _db.Books.AnyAsync(b => b.Id == request.BookId && b.TeacherId == user.TeacherId);
        if (!isOwner)
            return Forbid();

        var book = await _db.Books.FindAsync(request.BookId);
        if (book == null)
            return NotFound(new { success = false, error = "Book not found." });

        var filePath = _fileStorage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { success = false, error = "PDF file not found on server." });

        var teacher = await _db.Teachers.FindAsync(user.TeacherId.Value);
        var teacherName = teacher?.Name ?? "Unknown Teacher";
        var copies = Math.Max(1, request.Copies);

        var jobId = Guid.NewGuid().ToString("N");
        var userPass = $"PRINT-{jobId}";
        var ownerPass = _configuration.GetValue<string>("OwnerPassword__KeyVaultOrEnvVar")
            ?? Environment.GetEnvironmentVariable("OWNER_PASSWORD")
            ?? throw new InvalidOperationException("OwnerPassword is not configured. Set OwnerPassword__KeyVaultOrEnvVar in config or OWNER_PASSWORD environment variable.");

        _logger.LogInformation("ProcessPrint: Job={JobId}, Book={BookId}, Teacher={TeacherId}, Copies={Copies}",
            jobId, request.BookId, user.TeacherId, copies);

        try
        {
            var originalBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermark(originalBytes, teacherName, user.UserName ?? "Unknown", DateTime.UtcNow, watermarkEnabled, watermarkText);
            var securedBytes = _pdfSecurity.EncryptPdfWithPassword(watermarked, userPass, ownerPass);

            var secureDir = Path.Combine(Directory.GetCurrentDirectory(), "SecurePrints");
            Directory.CreateDirectory(secureDir);
            var securePath = Path.Combine(secureDir, $"{jobId}.pdf");
            await System.IO.File.WriteAllBytesAsync(securePath, securedBytes);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _printLogging.LogPrintAsync(
                user.TeacherId.Value,
                null,
                request.BookId,
                copies,
                user.Id,
                user.UserName
            );

            _logger.LogInformation("Print logged: Job={JobId}, Teacher={TeacherId}, Book={BookId}, Copies={Copies}, IP={IP}",
                jobId, user.TeacherId, request.BookId, copies, ipAddress);

            var added = PendingPrintJobs.Jobs.TryAdd(jobId, new PendingJobInfo
            {
                TeacherId = user.TeacherId.Value,
                BookId = request.BookId,
                Copies = copies,
                CreatedAt = DateTime.UtcNow,
                PrinterName = request.PrinterName,
                PaperSize = request.PaperSize ?? "A4",
                Orientation = request.Orientation ?? "portrait",
                ScalingMode = request.ScalingMode ?? "actual",
                CustomScale = request.CustomScale ?? 100,
                MarginUnit = request.MarginUnit ?? "mm",
                MarginTop = request.MarginTop ?? 25.4,
                MarginBottom = request.MarginBottom ?? 25.4,
                MarginLeft = request.MarginLeft ?? 31.75,
                MarginRight = request.MarginRight ?? 31.75
            });

            if (added)
            {
                JobStatusTracker.SetStatus(jobId, JobStatus.Queued, "Print job created and queued.");

                try
                {
                    await _hubContext.Clients.Group("PrintAgents").SendAsync("NewPrintJob", new PrintJobRequest
                    {
                        JobId = jobId,
                        BookId = request.BookId,
                        Copies = copies,
                        PrinterName = request.PrinterName,
                        PaperSize = request.PaperSize ?? "A4",
                        Orientation = request.Orientation ?? "portrait",
                        ScalingMode = request.ScalingMode ?? "actual",
                        CustomScale = request.CustomScale ?? 100,
                        MarginUnit = request.MarginUnit ?? "mm",
                        MarginTop = request.MarginTop ?? 25.4,
                        MarginBottom = request.MarginBottom ?? 25.4,
                        MarginLeft = request.MarginLeft ?? 31.75,
                        MarginRight = request.MarginRight ?? 31.75
                    });
                    _logger.LogInformation("SignalR broadcast sent for job {JobId}", jobId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR broadcast failed for job {JobId} (non-fatal)", jobId);
                }

                try
                {
                    var teacherUserIds = await _userManager.GetUsersInRoleAsync("Teacher");
                    await _hubContext.Clients.All.SendAsync("PrintStatusChanged", new
                    {
                        jobId,
                        status = "queued",
                        message = "Print job queued successfully."
                    });
                }
                catch { }
            }

            var queueCount = PendingPrintJobs.Jobs.Count;
            _logger.LogInformation("Pending queue: Job={JobId} added={Added}, queueSize={Size}", jobId, added, queueCount);

            return Ok(new
            {
                success = true,
                jobId,
                added,
                queueCount,
                watermarkEnabled,
                printerName = request.PrinterName,
                message = $"Print job {jobId} created for {copies} copy(ies)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessPrint failed for book {BookId}", request.BookId);
            return StatusCode(500, new { success = false, error = "Failed to process print job." });
        }
    }

    [HttpGet("print-file/{jobId}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> GetPrintFile(string jobId)
    {
        if (!Guid.TryParse(jobId, out _))
            return BadRequest(new { error = "Invalid job ID format." });

        if (!await IsJobOwnerAsync(jobId, User))
            return Forbid();

        var securePath = Path.Combine(Directory.GetCurrentDirectory(), "SecurePrints", $"{jobId}.pdf");
        if (!System.IO.File.Exists(securePath))
            return NotFound("Print job not found or expired.");

        var fileBytes = System.IO.File.ReadAllBytes(securePath);
        System.IO.File.Delete(securePath);

        return File(fileBytes, "application/pdf", $"print_{jobId}.pdf");
    }

    [HttpGet("download-secured/{jobId}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadSecured(string jobId)
    {
        if (!Guid.TryParse(jobId, out _))
            return BadRequest(new { error = "Invalid job ID format." });

        var agentLink = await ResolveAgentLinkAsync();
        var isAgent = agentLink != null;

        if (!isAgent)
        {
            if (!(User.Identity?.IsAuthenticated == true))
                return Unauthorized();

            if (!await IsJobOwnerAsync(jobId, User))
                return Forbid();
        }

        var securePath = Path.Combine(Directory.GetCurrentDirectory(), "SecurePrints", $"{jobId}.pdf");
        if (!System.IO.File.Exists(securePath))
            return NotFound("Print job not found or expired.");

        var fileBytes = System.IO.File.ReadAllBytes(securePath);
        return File(fileBytes, "application/pdf", $"secured_{jobId}.pdf");
    }

    [HttpGet("print/{bookId}")]
    public async Task<IActionResult> PrintPdf(int bookId, [FromQuery] string? token = null)
    {
        Book? book = null;
        ApplicationUser? user = null;
        string teacherName = "Unknown Teacher";
        string userId = "unknown";
        string userName = "Unknown User";

        if (!string.IsNullOrEmpty(token))
        {
            if (_printTokenService.ValidateToken(token, out int tid, out userId, out teacherName, out userName))
            {
                book = await _db.Books.Include(b => b.Board).FirstOrDefaultAsync(b => b.Id == tid && b.IsActive);
                if (book == null)
                    return NotFound();
            }
            else
            {
                return Unauthorized("Invalid or expired print token.");
            }
        }
        else
        {
            (book, user) = await ValidateAccess(bookId);
            if (book == null || user == null)
                return NotFound();

            var teacher = user.TeacherId != null ? await _db.Teachers.FindAsync(user.TeacherId.Value) : null;
            teacherName = teacher?.Name ?? "Unknown Teacher";
            userId = user.Id;
            userName = user.UserName ?? "Unknown";
        }

        var filePath = _fileStorage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound("PDF file not found on server.");

        _logger.LogInformation("Print request for book {BookId} by {UserName} (Teacher: {TeacherName})", bookId, userName, teacherName);

        try
        {
            var originalBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermark(originalBytes, teacherName, userName, DateTime.UtcNow, watermarkEnabled, watermarkText);
            return File(new MemoryStream(watermarked), "application/pdf", enableRangeProcessing: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watermarking failed for book {BookId}", bookId);
            return StatusCode(500, new { error = "Failed to process secure document." });
        }
    }

    [HttpGet("print-token/{bookId}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetPrintToken(int bookId)
    {
        var (book, user) = await ValidateAccess(bookId);
        if (book == null || user == null)
            return NotFound();

        var teacher = user.TeacherId != null ? await _db.Teachers.FindAsync(user.TeacherId.Value) : null;
        var teacherName = teacher?.Name ?? "Unknown Teacher";

        var token = _printTokenService.GenerateToken(bookId, user.Id, teacherName, user.UserName ?? "Unknown");
        return Ok(new { token, expiresInMinutes = 5 });
    }

    [HttpGet("print-agent/pending")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPendingJobs()
    {
        var agentLink = await ResolveAgentLinkAsync();

        if (!(User.Identity?.IsAuthenticated == true) && agentLink == null)
            return Unauthorized(new { error = "Authentication required." });

        var cutoff = DateTime.UtcNow.Add(-PendingPrintJobs.Expiry);
        var expired = PendingPrintJobs.Jobs.Where(kv => kv.Value.CreatedAt < cutoff).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
            PendingPrintJobs.Jobs.TryRemove(key, out _);

        List<string> jobs;
        if (agentLink != null)
        {
            jobs = PendingPrintJobs.Jobs
                .Where(kv => kv.Value.TeacherId == agentLink.TeacherId)
                .Select(kv => kv.Key)
                .ToList();
        }
        else
        {
            jobs = PendingPrintJobs.Jobs.Keys.ToList();
        }

        _logger.LogInformation("GetPendingJobs returning {Count} jobs for teacher {TeacherId}", jobs.Count, agentLink?.TeacherId);
        return Ok(new { jobs });
    }

    [HttpGet("print-agent/debug")]
    [AllowAnonymous]
    public IActionResult DebugPending()
    {
        if (!(User.Identity?.IsAuthenticated == true))
            return Unauthorized(new { error = "Authentication required." });
        var now = DateTime.UtcNow;
        var cutoff = now.Add(-PendingPrintJobs.Expiry);
        return Ok(new
        {
            jobCount = PendingPrintJobs.Jobs.Count,
            expiryMinutes = PendingPrintJobs.Expiry.TotalMinutes,
            now = now,
            jobs = PendingPrintJobs.Jobs.Select(kv => new
            {
                jobId = kv.Key,
                teacherId = kv.Value.TeacherId,
                copies = kv.Value.Copies,
                createdAt = kv.Value.CreatedAt,
                isExpired = kv.Value.CreatedAt < cutoff
            }).ToList()
        });
    }

    [HttpPost("print-agent/claim/{jobId}")]
    [AllowAnonymous]
    public async Task<IActionResult> ClaimJob(string jobId)
    {
        var agentLink = await ResolveAgentLinkAsync();
        if (agentLink == null)
            return Unauthorized(new { error = "Valid API key required." });

        if (PendingPrintJobs.Jobs.TryRemove(jobId, out var info))
        {
            if (info.TeacherId != agentLink.TeacherId)
            {
                PendingPrintJobs.Jobs.TryAdd(jobId, info);
                return Forbid();
            }

            agentLink.CopiesPrinted += info.Copies;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                jobId,
                copies = info.Copies,
                printerName = info.PrinterName,
                paperSize = info.PaperSize ?? "A4",
                orientation = info.Orientation ?? "portrait",
                scalingMode = info.ScalingMode ?? "actual",
                customScale = info.CustomScale ?? 100,
                marginUnit = info.MarginUnit ?? "mm",
                marginTop = info.MarginTop ?? 0,
                marginBottom = info.MarginBottom ?? 0,
                marginLeft = info.MarginLeft ?? 0,
                marginRight = info.MarginRight ?? 0
            });
        }
        return NotFound(new { success = false, error = "Job not found or already claimed." });
    }

    [HttpPost("print-agent/release/{jobId}")]
    [AllowAnonymous]
    public async Task<IActionResult> ReleaseJob(string jobId)
    {
        var agentLink = await ResolveAgentLinkAsync();
        if (agentLink == null)
            return Unauthorized(new { error = "Valid API key required." });

        if (!PendingPrintJobs.Jobs.ContainsKey(jobId))
        {
            PendingPrintJobs.Jobs.TryAdd(jobId, new PendingJobInfo
            {
                TeacherId = agentLink.TeacherId,
                Copies = 1,
                CreatedAt = DateTime.UtcNow
            });
            return Ok(new { success = true, message = "Job returned to pending queue." });
        }
        return Ok(new { success = true, message = "Job already in queue." });
    }

    [HttpGet("job-status/{jobId}")]
    [Authorize(Roles = "Teacher,Admin")]
    public IActionResult GetJobStatus(string jobId)
    {
        if (!Guid.TryParse(jobId, out _))
            return BadRequest(new { error = "Invalid job ID format." });

        if (!JobStatusTracker.Jobs.TryGetValue(jobId, out var statusInfo))
            return NotFound(new { error = "Job not found or expired." });

        return Ok(new
        {
            jobId,
            status = statusInfo.Status.ToString(),
            message = statusInfo.Message,
            lastUpdated = statusInfo.LastUpdated,
            createdAt = statusInfo.CreatedAt
        });
    }

    [HttpPost("job-status/{jobId}")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateJobStatus(string jobId, [FromBody] JobStatusUpdateRequest request)
    {
        var agentLink = await ResolveAgentLinkAsync();
        if (agentLink == null)
            return Unauthorized(new { error = "Valid API key required." });

        if (!Guid.TryParse(jobId, out _))
            return BadRequest(new { error = "Invalid job ID format." });

        if (Enum.TryParse<JobStatus>(request.Status, true, out var status))
        {
            JobStatusTracker.SetStatus(jobId, status, request.Message);
            return Ok(new { success = true });
        }

        return BadRequest(new { error = $"Invalid status: {request.Status}" });
    }
}

public class JobStatusUpdateRequest
{
    public string Status { get; set; } = "";
    public string? Message { get; set; }
}

public class ProcessPrintRequest
{
    public int BookId { get; set; }

    [Range(1, 50, ErrorMessage = "Copies must be between 1 and 50.")]
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

public class PendingJobInfo
{
    public int? TeacherId { get; set; }
    public int BookId { get; set; }
    public int Copies { get; set; }
    public DateTime CreatedAt { get; set; }
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

public static class PendingPrintJobs
{
    public static System.Collections.Concurrent.ConcurrentDictionary<string, PendingJobInfo> Jobs = new();
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);
}

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public class JobStatusInfo
{
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string? Message { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class JobStatusTracker
{
    public static System.Collections.Concurrent.ConcurrentDictionary<string, JobStatusInfo> Jobs = new();

    public static void SetStatus(string jobId, JobStatus status, string? message = null)
    {
        var info = Jobs.GetOrAdd(jobId, _ => new JobStatusInfo
        {
            CreatedAt = DateTime.UtcNow,
            Status = JobStatus.Queued
        });
        info.Status = status;
        info.Message = message;
        info.LastUpdated = DateTime.UtcNow;
    }

    public static void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow.Add(-maxAge);
        foreach (var kv in Jobs.Where(kv => kv.Value.CreatedAt < cutoff))
            Jobs.TryRemove(kv.Key, out _);
    }

    public static void RevertStaleProcessing(TimeSpan staleThreshold)
    {
        var cutoff = DateTime.UtcNow.Add(-staleThreshold);
        foreach (var kv in Jobs.Where(kv =>
            kv.Value.Status == JobStatus.Processing && kv.Value.LastUpdated < cutoff))
        {
            if (Jobs.TryGetValue(kv.Key, out var info))
            {
                info.Status = JobStatus.Queued;
                info.Message = "Reset from stale Processing state";
                info.LastUpdated = DateTime.UtcNow;
            }
        }
    }
}
