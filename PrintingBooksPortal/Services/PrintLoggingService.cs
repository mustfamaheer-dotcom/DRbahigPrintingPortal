using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class PrintLoggingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PrintLoggingService(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogPrintAsync(int teacherId, int? teacherBookshopLinkId, int bookId, int copies, string? userId, string? userName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var teacher = await db.Teachers.FindAsync(teacherId);
        var book = await db.Books.FindAsync(bookId);

        if (teacher == null || book == null) return;

        var log = new PrintLog
        {
            TeacherId = teacherId,
            TeacherBookshopLinkId = teacherBookshopLinkId,
            BookId = bookId,
            ShopName = teacher.Name,
            BookTitle = book.Title,
            Copies = copies,
            PrintedByUserId = userId,
            PrintedByUserName = userName,
            IPAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            PrintedAt = DateTime.UtcNow
        };

        db.PrintLogs.Add(log);
        await db.SaveChangesAsync();
    }

    public async Task<List<PrintLog>> GetRecentLogsAsync(int count = 50)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .OrderByDescending(l => l.PrintedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetTotalPrintsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs.SumAsync(l => l.Copies);
    }

    public async Task<Dictionary<string, int>> GetPrintsPerTeacherAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .GroupBy(l => l.ShopName)
            .Select(g => new { Teacher = g.Key, Total = g.Sum(l => l.Copies) })
            .ToDictionaryAsync(x => x.Teacher, x => x.Total);
    }

    public async Task<Dictionary<string, int>> GetPrintsPerBookAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .GroupBy(l => l.BookTitle)
            .Select(g => new { Book = g.Key, Total = g.Sum(l => l.Copies) })
            .ToDictionaryAsync(x => x.Book, x => x.Total);
    }

    public async Task<List<PrintLog>> GetTeacherLogsAsync(int teacherId, int count = 100)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .Where(l => l.TeacherId == teacherId)
            .OrderByDescending(l => l.PrintedAt)
            .Take(count)
            .ToListAsync();
    }
}
