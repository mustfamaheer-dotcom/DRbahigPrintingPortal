using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PdfSharpCore;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpPost("create-teacher")]
    public async Task<IActionResult> CreateTeacher([FromBody] CreateTeacherRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { success = false, error = "Email and Name are required." });

        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
            return Conflict(new { success = false, error = "A user with this email already exists." });

        var password = string.IsNullOrWhiteSpace(request.Password) ? "Teacher@123" : request.Password;

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.Name,
            Role = UserRole.Teacher,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return BadRequest(new { success = false, errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, "Teacher");

        var teacher = new Teacher
        {
            Name = request.Name
        };
        _db.Teachers.Add(teacher);
        await _db.SaveChangesAsync();

        user.TeacherId = teacher.Id;
        await _userManager.UpdateAsync(user);

        return Ok(new
        {
            success = true,
            teacherId = teacher.Id,
            email = request.Email,
            temporaryPassword = password,
            message = $"Teacher '{request.Name}' created successfully."
        });
    }

    [HttpGet("teachers")]
    public async Task<IActionResult> GetTeachers()
    {
        var teachers = await _db.Teachers
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.CreatedAt,
                BookCount = t.Books.Count,
                BoardCount = t.Boards.Count,
                BookshopCount = t.BookshopLinks.Count(l => l.IsActive)
            })
            .ToListAsync();

        return Ok(teachers);
    }

    [HttpPost("fix-teacher-ids")]
    public async Task<IActionResult> FixTeacherIds()
    {
        var teachers = await _userManager.GetUsersInRoleAsync("Teacher");
        var fixed_count = 0;
        var skipped = 0;
        foreach (var user in teachers)
        {
            if (user.TeacherId != null)
            {
                var teacherExists = await _db.Teachers.AnyAsync(t => t.Id == user.TeacherId);
                if (!teacherExists)
                {
                    var teacher = new Teacher { Name = user.FullName ?? user.Email ?? "Teacher" };
                    _db.Teachers.Add(teacher);
                    await _db.SaveChangesAsync();
                    user.TeacherId = teacher.Id;
                    await _userManager.UpdateAsync(user);
                    await _userManager.UpdateSecurityStampAsync(user);
                    fixed_count++;
                }
                else
                {
                    skipped++;
                }
            }
            else
            {
                var teacher = new Teacher { Name = user.FullName ?? user.Email ?? "Teacher" };
                _db.Teachers.Add(teacher);
                await _db.SaveChangesAsync();
                user.TeacherId = teacher.Id;
                await _userManager.UpdateAsync(user);
                await _userManager.UpdateSecurityStampAsync(user);
                fixed_count++;
            }
        }
        return Ok(new { success = true, message = $"Fixed {fixed_count} teacher(s), skipped {skipped}. Affected users must log out and log back in." });
    }

    [HttpPost("refresh-claims")]
    public async Task<IActionResult> RefreshTeacherClaims()
    {
        var teachers = await _userManager.GetUsersInRoleAsync("Teacher");
        var count = 0;
        foreach (var user in teachers)
        {
            if (user.TeacherId != null)
            {
                await _userManager.UpdateSecurityStampAsync(user);
                count++;
            }
        }
        return Ok(new { success = true, message = $"Claims refresh triggered for {count} teacher(s). They must log out and log back in." });
    }

    [HttpPost("reset-shop-stats/{teacherId:int}")]
    public async Task<IActionResult> ResetTeacherStats(int teacherId, [FromBody] ResetRequest request)
    {
        if (request?.Password != "0000")
            return BadRequest(new { success = false, error = "Wrong password. Stats were NOT reset." });

        var logs = await _db.PrintLogs.Where(l => l.TeacherId == teacherId).ToListAsync();
        _db.PrintLogs.RemoveRange(logs);
        await _db.SaveChangesAsync();

        var teacher = await _db.Teachers.FindAsync(teacherId);
        return Ok(new { success = true, message = $"Statistics reset for '{teacher?.Name ?? "teacher"}' ({logs.Count} log entries removed)." });
    }

    [HttpGet("shop-receipt/{teacherId:int}")]
    public async Task<IActionResult> GetTeacherReceipt(int teacherId)
    {
        var teacher = await _db.Teachers.FindAsync(teacherId);
        if (teacher == null)
            return NotFound(new { error = "Teacher not found." });

        var logs = await _db.PrintLogs.Where(l => l.TeacherId == teacherId).ToListAsync();
        var totalPrints = logs.Sum(l => l.Copies);
        var perBook = logs.GroupBy(l => l.BookTitle)
                          .Select(g => new { Book = g.Key, Copies = g.Sum(l => l.Copies) })
                          .OrderByDescending(x => x.Copies)
                          .ToList();

        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        var fontTitle = new XFont("Arial", 18, XFontStyle.Bold);
        var fontHeader = new XFont("Arial", 13, XFontStyle.Bold);
        var fontBody = new XFont("Arial", 11, XFontStyle.Regular);
        var fontSmall = new XFont("Arial", 9, XFontStyle.Regular);
        var gray = XBrushes.Gray;
        var black = XBrushes.Black;
        var accent = new XSolidBrush(XColor.FromArgb(16, 185, 129));

        int y = 40;
        gfx.DrawString("DR Bahig Books Portal", fontTitle, accent, new XPoint(40, y));
        y += 30;
        gfx.DrawString("Print Receipt", fontHeader, black, new XPoint(40, y));
        y += 28;

        gfx.DrawString($"Teacher: {teacher.Name}", fontBody, black, new XPoint(40, y));
        y += 20;
        gfx.DrawString($"Date: {DateTime.Now:dd/MM/yyyy HH:mm}", fontBody, gray, new XPoint(40, y));
        y += 30;

        gfx.DrawLine(new XPen(accent.Color, 2), 40, y, 550, y);
        y += 20;

        gfx.DrawString($"Total prints: {totalPrints}", fontHeader, black, new XPoint(40, y));
        y += 28;

        if (perBook.Count > 0)
        {
            gfx.DrawString("Prints by book:", fontHeader, black, new XPoint(40, y));
            y += 24;

            foreach (var item in perBook)
            {
                gfx.DrawString($"\u2022  {item.Book}", fontBody, black, new XPoint(50, y));
                gfx.DrawString($"{item.Copies} copy(ies)", fontBody, gray, new XPoint(420, y));
                y += 20;

                if (y > 770)
                {
                    page = doc.AddPage();
                    page.Size = PageSize.A4;
                    gfx = XGraphics.FromPdfPage(page);
                    y = 40;
                }
            }
        }
        else
        {
            gfx.DrawString("No prints recorded for this teacher.", fontBody, gray, new XPoint(50, y));
        }

        y = Math.Max(y + 30, 780);
        gfx.DrawLine(new XPen(XColor.FromArgb(200, 200, 200)), 40, y, 550, y);
        y += 16;
        gfx.DrawString("Generated by DR Bahig Books Portal Print System", fontSmall, gray, new XPoint(40, y));

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        var fileName = $"receipt_{teacher.Name.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.pdf";
        return File(ms.ToArray(), "application/pdf", fileName);
    }
}

public class CreateTeacherRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Password { get; set; }
}

public class ResetRequest
{
    public string Password { get; set; } = "";
}
