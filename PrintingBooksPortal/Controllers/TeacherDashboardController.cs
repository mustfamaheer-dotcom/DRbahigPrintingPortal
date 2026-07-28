using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/teacher")]
[Authorize(Roles = "Teacher")]
public class TeacherDashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public TeacherDashboardController(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        if (_tenant.TeacherId == null)
            return Unauthorized();

        var tid = _tenant.TeacherId.Value;
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalBooks = await _db.Books.CountAsync(b => b.TeacherId == tid && b.IsActive);
        var activeBookshops = await _db.TeacherBookshopLinks.CountAsync(l => l.TeacherId == tid && l.IsActive);
        var copiesThisMonth = await _db.PrintLogs
            .Where(l => l.TeacherId == tid && l.PrintedAt >= monthStart)
            .SumAsync(l => (int?)l.Copies) ?? 0;
        var totalCopies = await _db.PrintLogs
            .Where(l => l.TeacherId == tid)
            .SumAsync(l => (int?)l.Copies) ?? 0;

        var recentLogs = await _db.PrintLogs
            .Where(l => l.TeacherId == tid)
            .OrderByDescending(l => l.PrintedAt)
            .Take(5)
            .Select(l => new
            {
                l.BookTitle,
                l.ShopName,
                l.Copies,
                l.PrintedAt
            })
            .ToListAsync();

        return Ok(new
        {
            totalBooks,
            activeBookshops,
            copiesThisMonth,
            totalCopiesAllTime = totalCopies,
            recentLogs
        });
    }
}
