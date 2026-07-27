using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<EducationalBoard> EducationalBoards => Set<EducationalBoard>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<ShopBookAssignment> ShopBookAssignments => Set<ShopBookAssignment>();
    public DbSet<PrintLog> PrintLogs => Set<PrintLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ShopBookAssignment>()
            .HasIndex(a => new { a.ShopId, a.BookId })
            .IsUnique();

        builder.Entity<PrintLog>()
            .HasIndex(l => l.PrintedAt);

        builder.Entity<PrintLog>()
            .HasIndex(l => l.ShopId);

        builder.Entity<PrintLog>()
            .HasIndex(l => l.BookId);

        builder.Entity<SystemSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();
    }
}
