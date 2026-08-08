using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintingBooksPortal.Data;

namespace PrintingBooksPortal.Tests;

/// <summary>
/// Full-app test host over a shared in-memory SQLite database.
/// Program.cs runs in Development → SQLite + EnsureCreated + DbSeeder at startup,
/// giving every test a seeded, tenant-isolated backing store.
/// </summary>
public class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAlive;

    public TestAppFactory()
    {
        // Unique shared-memory database per factory instance. The name must be
        // unique so parallel test classes (each with its own factory) never see
        // each other's seed data. Must stay open for the factory's lifetime.
        _keepAlive = new SqliteConnection($"Data Source=bp-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("MultiTenancy:Enabled", "true");
        // TestServer speaks plain HTTP → relax the HTTPS-only cookie policy and
        // the HTTPS redirect, otherwise auth cookies are never sent back.
        builder.UseSetting("Security:CookieSecurePolicy", "SameAsRequest");
        builder.UseSetting("Security:RequireHttps", "false");

        builder.ConfigureServices(services =>
        {
            // Point the app's DbContext at our shared in-memory DB, replacing
            // the SQLite file-based registration from Program.cs.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_keepAlive.ConnectionString));
        });
    }

    /// <summary>Open a scoped AppDbContext (global query filters active).</summary>
    public AppDbContext CreateScopedDb() =>
        Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    protected override void Dispose(bool disposing)
    {
        _keepAlive.Dispose();
        base.Dispose(disposing);
    }
}