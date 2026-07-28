using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
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
        ILogger<SecurePdfController> logger)
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

        var hasAccess = await _db.ShopBookAssignments
            .AnyAsync(a => a.ShopId == user.ShopId && a.BookId == bookId && a.IsActive);

        return hasAccess ? (book, user) : (null, null);
    }

    private async Task<bool> IsJobOwnerAsync(string jobId, System.Security.Claims.ClaimsPrincipal user)
    {
        if (PendingPrintJobs.Jobs.TryGetValue(jobId, out var info))
        {
            var appUser = await _userManager.GetUserAsync(user);
            if (appUser == null) return false;

            var isAdmin = await _userManager.IsInRoleAsync(appUser, "Admin");
            // Admin can access any job; Shop can only access their own jobs
            return isAdmin || info.ShopId == appUser.ShopId;
        }
        return false;
    }

    private bool IsValidAgentApiKey()
    {
        var configuredKey = _configuration.GetValue<string>("AgentSettings:ApiKey");
        if (string.IsNullOrEmpty(configuredKey))
            return false;
        var providedKey = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        return string.Equals(providedKey, configuredKey, StringComparison.Ordinal);
    }

    private async Task<byte[]?> GetOriginalPdfBytes(int bookId)
    {
        var filePath = _fileStorage.GetFilePath((await _db.Books.FindAsync(bookId))?.FilePath ?? "");
        if (!System.IO.File.Exists(filePath))
            return null;
        return await System.IO.File.ReadAllBytesAsync(filePath);
    }

    [HttpGet("view-secure/{bookId}")]
    [Authorize(Roles = "Shop,Admin")]
    public async Task<IActionResult> ViewSecurePdf(int bookId)
    {
        var (book, user) = await ValidateAccess(bookId);
        if (book == null || user == null)
            return NotFound(new { error = "Access Denied: You are not authorized to view this book." });

        var shop = user.ShopId != null ? await _db.Shops.FindAsync(user.ShopId.Value) : null;
        var shopName = shop?.Name ?? "Unknown Shop";

        _logger.LogInformation("User {UserId} viewing secure PDF for book {BookId}", user.Id, bookId);

        try
        {
            var originalBytes = await System.IO.File.ReadAllBytesAsync(_fileStorage.GetFilePath(book.FilePath));
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermark(originalBytes, shopName, user.UserName ?? "Unknown", DateTime.UtcNow, watermarkEnabled, watermarkText);
            return Ok(new { pdfData = Convert.ToBase64String(watermarked), watermarkEnabled });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heavy watermarking failed for book {BookId}", bookId);
            return StatusCode(500, new { error = "Failed to process PDF for viewing." });
        }
    }

    [HttpPost("process-print")]
    [Authorize(Roles = "Shop")]
    public async Task<IActionResult> ProcessPrint([FromBody] ProcessPrintRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.ShopId == null)
            return Unauthorized(new { success = false, error = "Access Denied: You are not authorized to print." });

        var hasAccess = await _db.ShopBookAssignments
            .AnyAsync(a => a.ShopId == user.ShopId && a.BookId == request.BookId && a.IsActive);

        if (!hasAccess)
            return Forbid();

        var book = await _db.Books.FindAsync(request.BookId);
        if (book == null)
            return NotFound(new { success = false, error = "Book not found." });

        var filePath = _fileStorage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { success = false, error = "PDF file not found on server." });

        var shop = await _db.Shops.FindAsync(user.ShopId.Value);
        var shopName = shop?.Name ?? "Unknown Shop";
        var copies = Math.Max(1, request.Copies);

        var jobId = Guid.NewGuid().ToString("N");
        var userPass = $"PRINT-{jobId}";
        // Security: read OwnerPassword from config or env var; fail if unset in production
        var ownerPass = _configuration.GetValue<string>("OwnerPassword__KeyVaultOrEnvVar")
            ?? Environment.GetEnvironmentVariable("OWNER_PASSWORD")
            ?? throw new InvalidOperationException("OwnerPassword is not configured. Set OwnerPassword__KeyVaultOrEnvVar in config or OWNER_PASSWORD environment variable.");

        _logger.LogInformation("ProcessPrint: Job={JobId}, Book={BookId}, Shop={ShopId}, Copies={Copies}",
            jobId, request.BookId, user.ShopId, copies);

        try
        {
            var originalBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermark(originalBytes, shopName, user.UserName ?? "Unknown", DateTime.UtcNow, watermarkEnabled, watermarkText);
            var securedBytes = _pdfSecurity.EncryptPdfWithPassword(watermarked, userPass, ownerPass);

            var secureDir = Path.Combine(Directory.GetCurrentDirectory(), "SecurePrints");
            Directory.CreateDirectory(secureDir);
            var securePath = Path.Combine(secureDir, $"{jobId}.pdf");
            await System.IO.File.WriteAllBytesAsync(securePath, securedBytes);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _printLogging.LogPrintAsync(
                user.ShopId.Value,
                request.BookId,
                copies,
                user.Id,
                user.UserName
            );

            _logger.LogInformation("Print logged: Job={JobId}, Shop={ShopId}, Book={BookId}, Copies={Copies}, IP={IP}",
                jobId, user.ShopId, request.BookId, copies, ipAddress);

            // Track ownership so only the creating shop (or Admin) can download/print the secured file
            var added = PendingPrintJobs.Jobs.TryAdd(jobId, new PendingJobInfo
            {
                ShopId = user.ShopId.Value,
                Copies = copies,
                CreatedAt = DateTime.UtcNow,
                PrinterName = request.PrinterName,
                PaperSize = request.PaperSize ?? "A4",
                Duplex = request.Duplex ?? "off",
                ScalingMode = request.ScalingMode ?? "actual",
                CustomScale = request.CustomScale ?? 100,
                MarginUnit = request.MarginUnit ?? "mm",
                MarginTop = request.MarginTop ?? 25.4,
                MarginBottom = request.MarginBottom ?? 25.4,
                MarginLeft = request.MarginLeft ?? 25.4,
                MarginRight = request.MarginRight ?? 25.4
            });

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
            // Security: never expose internal exception details to the client
            return StatusCode(500, new { success = false, error = "Failed to process print job." });
        }
    }

    [HttpGet("print-file/{jobId}")]
    [Authorize(Roles = "Shop,Admin")]
    public async Task<IActionResult> GetPrintFile(string jobId)
    {
        if (!Guid.TryParse(jobId, out _))
            return BadRequest(new { error = "Invalid job ID format." });

        // Verify the job belongs to the current user's shop (or user is Admin)
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

        var isAgent = IsValidAgentApiKey();

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
        string shopName = "Unknown Shop";
        string userId = "unknown";
        string userName = "Unknown User";

        if (!string.IsNullOrEmpty(token))
        {
            if (_printTokenService.ValidateToken(token, out int tid, out userId, out shopName, out userName))
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

            var shop = user.ShopId != null ? await _db.Shops.FindAsync(user.ShopId.Value) : null;
            shopName = shop?.Name ?? "Unknown Shop";
            userId = user.Id;
            userName = user.UserName ?? "Unknown";
        }

        var filePath = _fileStorage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound("PDF file not found on server.");

        _logger.LogInformation("Print request for book {BookId} by {UserName} (Shop: {ShopName})", bookId, userName, shopName);

        try
        {
            var originalBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermark(originalBytes, shopName, userName, DateTime.UtcNow, watermarkEnabled, watermarkText);
            return File(new MemoryStream(watermarked), "application/pdf", enableRangeProcessing: false);
        }
        catch (Exception ex)
        {
            // Security: fail CLOSED â€” never expose the unwatermarked file
            _logger.LogError(ex, "Watermarking failed for book {BookId}", bookId);
            return StatusCode(500, new { error = "Failed to process secure document." });
        }
    }

    [HttpGet("print-token/{bookId}")]
    [Authorize(Roles = "Shop")]
    public async Task<IActionResult> GetPrintToken(int bookId)
    {
        var (book, user) = await ValidateAccess(bookId);
        if (book == null || user == null)
            return NotFound();

        var shop = user.ShopId != null ? await _db.Shops.FindAsync(user.ShopId.Value) : null;
        var shopName = shop?.Name ?? "Unknown Shop";

        var token = _printTokenService.GenerateToken(bookId, user.Id, shopName, user.UserName ?? "Unknown");
        return Ok(new { token, expiresInMinutes = 5 });
    }

    [HttpGet("print-agent/pending")]
    [AllowAnonymous]
    public IActionResult GetPendingJobs()
    {
        if (!(User.Identity?.IsAuthenticated == true) && !IsValidAgentApiKey())
            return Unauthorized(new { error = "Authentication required." });

        var cutoff = DateTime.UtcNow.Add(-PendingPrintJobs.Expiry);
        var expired = PendingPrintJobs.Jobs.Where(kv => kv.Value.CreatedAt < cutoff).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
            PendingPrintJobs.Jobs.TryRemove(key, out _);

        var jobs = PendingPrintJobs.Jobs.Keys.ToList();
        _logger.LogInformation("GetPendingJobs returning {Count} jobs", jobs.Count);
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
                shopId = kv.Value.ShopId,
                copies = kv.Value.Copies,
                createdAt = kv.Value.CreatedAt,
                isExpired = kv.Value.CreatedAt < cutoff
            }).ToList()
        });
    }

    [HttpPost("print-agent/claim/{jobId}")]
    [AllowAnonymous]
    public IActionResult ClaimJob(string jobId)
    {
        if (!IsValidAgentApiKey())
            return Unauthorized(new { error = "Valid API key required." });

        if (PendingPrintJobs.Jobs.TryRemove(jobId, out var info))
            return Ok(new {
                success = true,
                jobId,
                copies = info.Copies,
                printerName = info.PrinterName,
                paperSize = info.PaperSize ?? "A4",
                duplex = info.Duplex ?? "off",
                scalingMode = info.ScalingMode ?? "actual",
                customScale = info.CustomScale ?? 100,
                marginUnit = info.MarginUnit ?? "mm",
                marginTop = info.MarginTop ?? 0,
                marginBottom = info.MarginBottom ?? 0,
                marginLeft = info.MarginLeft ?? 0,
                marginRight = info.MarginRight ?? 0
            });
        return NotFound(new { success = false, error = "Job not found or already claimed." });
    }

    [HttpPost("print-agent/release/{jobId}")]
    [AllowAnonymous]
    public IActionResult ReleaseJob(string jobId)
    {
        if (!IsValidAgentApiKey())
            return Unauthorized(new { error = "Valid API key required." });

        // Only re-add if not already in the queue
        if (!PendingPrintJobs.Jobs.ContainsKey(jobId))
        {
            PendingPrintJobs.Jobs.TryAdd(jobId, new PendingJobInfo
            {
                ShopId = 0,
                Copies = 1,
                CreatedAt = DateTime.UtcNow
            });
            return Ok(new { success = true, message = "Job returned to pending queue." });
        }
        return Ok(new { success = true, message = "Job already in queue." });
    }
}

public class ProcessPrintRequest
{
    public int BookId { get; set; }

    [Range(1, 50, ErrorMessage = "Copies must be between 1 and 50.")]
    public int Copies { get; set; } = 1;

    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Duplex { get; set; }
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
    public int ShopId { get; set; }
    public int Copies { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Duplex { get; set; }
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
