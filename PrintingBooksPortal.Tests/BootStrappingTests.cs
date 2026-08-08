using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PrintingBooksPortal.Tests;

/// <summary>
/// Exercises the seeded identity world: default tenant, roles, the two
/// official accounts and per-role sign-in redirect targets.
/// </summary>
public class BootStrappingTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public BootStrappingTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Seed_CreatesDefaultTenantRolesAndAccounts()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == 1);
            Assert.NotNull(tenant);
            Assert.Equal("Default Tenant", tenant!.Name);
            Assert.True(tenant.IsActive);

            var roles = await db.Roles.Select(r => r.Name).ToListAsync();
            foreach (var expected in new[] { "Admin", "Teacher", "Shop", "SystemAdmin" })
                Assert.Contains(expected, roles);

            var admin = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == "admin@printingbooks.com");
            Assert.NotNull(admin);
            Assert.Equal(1, admin!.TenantId);   // legacy account backfilled onto tenant 1

            var sysAdmin = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == DbSeeder.SysAdminEmail);
            Assert.NotNull(sysAdmin);
            Assert.Null(sysAdmin!.TenantId);    // platform-level: no tenant
        }
    }

[Fact]
    public async Task Login_TeacherRedirectsToAdminDashboard()
    {
        using var client = TestHelpers.CreateClient(_factory, allowRedirects: false);
        var loc = await TestHelpers.LoginAsync(client, "admin@printingbooks.com", "Admin@123");
        Assert.EndsWith("/admin/dashboard", loc);
    }

    [Fact]
    public async Task Login_SystemAdminRedirectsToSaDashboard()
    {
        using var client = TestHelpers.CreateClient(_factory, allowRedirects: false);
        var loc = await TestHelpers.LoginAsync(client, DbSeeder.SysAdminEmail, DbSeeder.SysAdminPassword);
        Assert.EndsWith("/sa/dashboard", loc);
    }

    [Fact]
    public async Task Login_WrongPassword_RedirectBackWithError()
    {
        using var client = TestHelpers.CreateClient(_factory, allowRedirects: false);
        var loc = await TestHelpers.LoginAsync(client, "admin@printingbooks.com", "wrong-pw-123");
        Assert.Contains("error=", loc);
    }

    [Fact]
    public async Task SystemAdminApi_RequiresRole()
    {
        using var anonymous = TestHelpers.CreateClient(_factory);
        var response = await anonymous.GetAsync("/api/sa/teachers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var teacher = TestHelpers.CreateClient(_factory);
        await TestHelpers.LoginAsync(teacher, "admin@printingbooks.com", "Admin@123");
        response = await teacher.GetAsync("/api/sa/teachers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}