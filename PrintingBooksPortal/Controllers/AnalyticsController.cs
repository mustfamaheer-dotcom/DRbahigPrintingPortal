using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "Admin")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("print-summary")]
    public async Task<IActionResult> GetPrintSummary()
    {
        var logs = _db.PrintLogs.AsNoTracking();

        var perShop = await logs
            .GroupBy(l => new { l.ShopId, l.ShopName, l.BookId, l.BookTitle })
            .Select(g => new
            {
                ShopId = g.Key.ShopId,
                ShopName = g.Key.ShopName,
                BookId = g.Key.BookId,
                BookTitle = g.Key.BookTitle,
                TotalCopies = g.Sum(l => l.Copies),
                PrintCount = g.Count()
            })
            .OrderByDescending(x => x.TotalCopies)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var daily = await logs
            .Where(l => l.PrintedAt >= now.AddDays(-7))
            .GroupBy(l => l.PrintedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                TotalCopies = g.Sum(l => l.Copies),
                PrintCount = g.Count()
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        var weekly = await logs
            .GroupBy(l => new
            {
                Year = l.PrintedAt.Year,
                Week = (l.PrintedAt.DayOfYear - 1) / 7 + 1
            })
            .Select(g => new
            {
                Year = g.Key.Year,
                Week = g.Key.Week,
                TotalCopies = g.Sum(l => l.Copies),
                PrintCount = g.Count()
            })
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Week)
            .Take(12)
            .ToListAsync();

        var recent = await logs
            .OrderByDescending(l => l.PrintedAt)
            .Take(50)
            .Select(l => new
            {
                l.Id,
                l.ShopName,
                l.BookTitle,
                l.Copies,
                l.PrintedByUserName,
                l.IPAddress,
                l.PrintedAt
            })
            .ToListAsync();

        return Ok(new
        {
            perShop,
            daily,
            weekly,
            recent
        });
    }

    [HttpGet("print-trends")]
    public async Task<IActionResult> GetPrintTrends()
    {
        var now = DateTime.UtcNow;
        var thirtyDays = await _db.PrintLogs
            .AsNoTracking()
            .Where(l => l.PrintedAt >= now.AddDays(-30))
            .GroupBy(l => l.PrintedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                TotalCopies = g.Sum(l => l.Copies),
                PrintCount = g.Count()
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return Ok(thirtyDays);
    }
}
