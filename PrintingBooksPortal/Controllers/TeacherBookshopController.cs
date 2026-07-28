using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/teacher/bookshops")]
[Authorize(Roles = "Teacher")]
public class TeacherBookshopController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<TeacherBookshopController> _logger;

    public TeacherBookshopController(AppDbContext db, ITenantContext tenant, UserManager<ApplicationUser> userManager, ILogger<TeacherBookshopController> logger)
    {
        _db = db;
        _tenant = tenant;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetLinkedBookshops()
    {
        if (_tenant.TeacherId == null)
            return Unauthorized(new { error = "Teacher identity not found." });

        var links = await _db.TeacherBookshopLinks
            .Where(l => l.TeacherId == _tenant.TeacherId.Value)
            .Include(l => l.Bookshop)
            .Select(l => new
            {
                l.Id,
                l.BookshopId,
                BookshopName = l.Bookshop.Name,
                l.Bookshop.ContactPerson,
                l.Bookshop.Phone,
                l.Bookshop.Address,
                l.CopiesPrinted,
                l.UniqueApiKey,
                l.LastResetDate,
                l.IsActive,
                l.CreatedAt
            })
            .ToListAsync();

        return Ok(links);
    }

    [HttpPost("link")]
    public async Task<IActionResult> LinkBookshop([FromBody] LinkBookshopRequest request)
    {
        if (_tenant.TeacherId == null)
            return Unauthorized(new { error = "Teacher identity not found." });

        if (string.IsNullOrWhiteSpace(request.BookshopName))
            return BadRequest(new { error = "Bookshop name is required." });

        var bookshop = await _db.Bookshops
            .FirstOrDefaultAsync(b => b.Name == request.BookshopName);

        if (bookshop == null)
        {
            bookshop = new Bookshop
            {
                Name = request.BookshopName,
                ContactPerson = request.ContactPerson,
                Phone = request.Phone,
                Address = request.Address
            };
            _db.Bookshops.Add(bookshop);
            await _db.SaveChangesAsync();
        }

        var existingLink = await _db.TeacherBookshopLinks
            .AnyAsync(l => l.TeacherId == _tenant.TeacherId.Value && l.BookshopId == bookshop.Id);

        if (existingLink)
            return Conflict(new { error = "This bookshop is already linked to your account." });

        var apiKey = GenerateSecureApiKey();

        var link = new TeacherBookshopLink
        {
            TeacherId = _tenant.TeacherId.Value,
            BookshopId = bookshop.Id,
            UniqueApiKey = apiKey,
            CopiesPrinted = 0,
            IsActive = true
        };

        _db.TeacherBookshopLinks.Add(link);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Teacher {TeacherId} linked bookshop {BookshopId} with API key {ApiKey}",
            _tenant.TeacherId, bookshop.Id, apiKey[..8] + "...");

        return Ok(new
        {
            link.Id,
            link.BookshopId,
            BookshopName = bookshop.Name,
            link.UniqueApiKey,
            link.CopiesPrinted,
            link.IsActive,
            link.CreatedAt
        });
    }

    [HttpPost("{linkId}/reset-stats")]
    public async Task<IActionResult> ResetStats(int linkId)
    {
        if (_tenant.TeacherId == null)
            return Unauthorized(new { error = "Teacher identity not found." });

        var link = await _db.TeacherBookshopLinks
            .Include(l => l.Bookshop)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.TeacherId == _tenant.TeacherId.Value);

        if (link == null)
            return NotFound(new { error = "Link not found." });

        if (link.CopiesPrinted > 0)
        {
            var invoice = new Invoice
            {
                TeacherBookshopLinkId = link.Id,
                PeriodStart = link.LastResetDate,
                PeriodEnd = DateTime.UtcNow,
                TotalCopies = link.CopiesPrinted,
                TotalAmount = link.CopiesPrinted * 1.0m,
                Status = InvoiceStatus.Pending
            };
            _db.Invoices.Add(invoice);
        }

        link.CopiesPrinted = 0;
        link.LastResetDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Teacher {TeacherId} reset stats for bookshop link {LinkId} ({ShopName})",
            _tenant.TeacherId, linkId, link.Bookshop.Name);

        return Ok(new { success = true, message = $"Statistics reset for '{link.Bookshop.Name}'. Invoice generated." });
    }

    [HttpPost("{linkId}/unlink")]
    public async Task<IActionResult> UnlinkBookshop(int linkId)
    {
        if (_tenant.TeacherId == null)
            return Unauthorized(new { error = "Teacher identity not found." });

        var link = await _db.TeacherBookshopLinks
            .FirstOrDefaultAsync(l => l.Id == linkId && l.TeacherId == _tenant.TeacherId.Value);

        if (link == null)
            return NotFound(new { error = "Link not found." });

        link.IsActive = false;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Bookshop unlinked." });
    }

    [HttpPost("{linkId}/create-user")]
    public async Task<IActionResult> CreateBookshopUser(int linkId, [FromBody] CreateBookshopUserRequest request)
    {
        if (_tenant.TeacherId == null)
            return Unauthorized(new { error = "Teacher identity not found." });

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required." });

        var link = await _db.TeacherBookshopLinks
            .Include(l => l.Bookshop)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.TeacherId == _tenant.TeacherId.Value);

        if (link == null)
            return NotFound(new { error = "Link not found." });

        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
            return Conflict(new { error = "A user with this email already exists." });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = link.Bookshop.Name,
            Role = UserRole.BookshopManager,
            BookshopId = link.BookshopId,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { success = false, errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, "BookshopManager");

        _logger.LogInformation("Teacher {TeacherId} created bookshop user '{Email}' for bookshop {BookshopId}",
            _tenant.TeacherId, request.Email, link.BookshopId);

        return Ok(new { success = true, message = $"User '{request.Email}' created for '{link.Bookshop.Name}'." });
    }

    [HttpPost("{linkId}/reset-password")]
    public async Task<IActionResult> ResetBookshopUserPassword(int linkId, [FromBody] ResetPasswordRequest request)
    {
        if (_tenant.TeacherId == null)
            return Unauthorized(new { error = "Teacher identity not found." });

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "New password is required." });

        var link = await _db.TeacherBookshopLinks
            .Include(l => l.Bookshop)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.TeacherId == _tenant.TeacherId.Value);

        if (link == null)
            return NotFound(new { error = "Link not found." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.BookshopId == link.BookshopId);
        if (user == null)
            return NotFound(new { error = "No user account exists for this bookshop. Create one first." });

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { success = false, errors = result.Errors.Select(e => e.Description) });

        return Ok(new { success = true, message = $"Password reset for '{user.Email}'." });
    }

    public static string GenerateSecureApiKey()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }
}

public class CreateBookshopUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class LinkBookshopRequest
{
    public string BookshopName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
}

[ApiController]
[Route("api/teacher/invoices")]
[Authorize(Roles = "Teacher")]
public class TeacherInvoiceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public TeacherInvoiceController(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoices([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (_tenant.TeacherId == null)
            return Unauthorized(new { error = "Teacher identity not found." });

        var query = _db.Invoices
            .Where(i => i.Link.TeacherId == _tenant.TeacherId.Value)
            .Include(i => i.Link).ThenInclude(l => l.Bookshop);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.TeacherBookshopLinkId,
                BookshopName = i.Link.Bookshop.Name,
                i.PeriodStart,
                i.PeriodEnd,
                i.TotalCopies,
                i.Currency,
                i.TotalAmount,
                Status = i.Status.ToString(),
                i.CreatedAt,
                i.PaidAt
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }
}
