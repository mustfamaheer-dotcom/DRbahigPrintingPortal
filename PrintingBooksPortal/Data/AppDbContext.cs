using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Bookshop> Bookshops => Set<Bookshop>();
    public DbSet<TeacherBookshopLink> TeacherBookshopLinks => Set<TeacherBookshopLink>();
    public DbSet<EducationalBoard> EducationalBoards => Set<EducationalBoard>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<PrintLog> PrintLogs => Set<PrintLog>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.HasOne(u => u.Teacher)
                .WithMany()
                .HasForeignKey(u => u.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Bookshop>(e =>
        {
            e.HasIndex(b => b.Name);
            e.HasOne(b => b.BookshopUser)
                .WithOne(u => u.Bookshop)
                .HasForeignKey<ApplicationUser>(u => u.BookshopId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TeacherBookshopLink>(e =>
        {
            e.HasIndex(l => l.UniqueApiKey).IsUnique();
            e.HasIndex(l => new { l.TeacherId, l.BookshopId }).IsUnique();
            e.HasOne(l => l.Teacher)
                .WithMany(t => t.BookshopLinks)
                .HasForeignKey(l => l.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Bookshop)
                .WithMany(b => b.TeacherLinks)
                .HasForeignKey(l => l.BookshopId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Invoice>(e =>
        {
            e.HasOne(i => i.Link)
                .WithMany(l => l.Invoices)
                .HasForeignKey(i => i.TeacherBookshopLinkId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.TeacherBookshopLinkId);
        });

        builder.Entity<EducationalBoard>(e =>
        {
            e.HasOne(b => b.Teacher)
                .WithMany(t => t.Boards)
                .HasForeignKey(b => b.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Book>(e =>
        {
            e.HasOne(b => b.Teacher)
                .WithMany(t => t.Books)
                .HasForeignKey(b => b.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(b => b.Board)
                .WithMany(bd => bd.Books)
                .HasForeignKey(b => b.BoardId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PrintLog>(e =>
        {
            e.HasOne(l => l.Teacher)
                .WithMany(t => t.PrintLogs)
                .HasForeignKey(l => l.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.TeacherBookshopLink)
                .WithMany(lk => lk.PrintLogs)
                .HasForeignKey(l => l.TeacherBookshopLinkId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(l => l.Book)
                .WithMany(b => b.PrintLogs)
                .HasForeignKey(l => l.BookId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(l => l.TeacherId);
            e.HasIndex(l => l.TeacherBookshopLinkId);
            e.HasIndex(l => l.PrintedAt);
        });

        builder.Entity<SystemSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();
    }
}
