using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Tests;

/// <summary>
/// Two-tenant world built through the real SystemAdmin API (source of truth):
/// tenant A = legacy default (Id 1), tenant B = created here. Verifies that a
/// tenant's data stays invisible to the other tenant and that agent API keys
/// are strictly per-tenant.
/// </summary>
public class TenantIsolationTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public TenantIsolationTests(TestAppFactory factory) => _factory = factory;

    private const string TenantBName = "Second School";
    private const string TenantBEmail = "b@secondschool.local";

    private async Task<(string apiKeyB, string teacherBEmail)> ProvisionTenantBAsync()
    {
        using var sa = TestHelpers.CreateClient(_factory);
        await TestHelpers.LoginAsync(sa, DbSeeder.SysAdminEmail, DbSeeder.SysAdminPassword);

        var create = await sa.PostAsJsonAsync("/api/sa/teachers", new
        {
            name = TenantBName,
            ownerName = "Owner B",
            contactEmail = TenantBEmail,
            password = "TeacherB@123",
            plan = "Standard"
        });

        int tenantBId;
        if (create.StatusCode == HttpStatusCode.Created)
        {
            var created = await TestHelpers.ReadJsonAsync(create);
            tenantBId = created.GetProperty("id").GetInt32();
        }
        else
        {
            // Already provisioned by a sibling test sharing this factory's DB
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
            var existing = await TestHelpers.ReadJsonAsync(await sa.GetAsync("/api/sa/teachers"));
            tenantBId = existing.GetProperty("teachers").EnumerateArray()
                .First(t => t.GetProperty("contactEmail").GetString() == TenantBEmail)
                .GetProperty("id").GetInt32();
        }

        var keyResp = await sa.PostAsync($"/api/sa/tenants/{tenantBId}/apikeys", null);
        Assert.Equal(HttpStatusCode.Created, keyResp.StatusCode);
        var keyBody = await TestHelpers.ReadJsonAsync(keyResp);
        return (keyBody.GetProperty("apiKey").GetString()!, TenantBEmail);
    }

    [Fact]
    public async Task AgentApiKey_PendingQueue_IsScopedToItsTenant()
    {
        var (keyB, _) = await ProvisionTenantBAsync();
        Assert.StartsWith("bpk_", keyB);

        // Put a job into the pending queue for tenant 2
        var jobId = "job-" + Guid.NewGuid().ToString("N")[..8];
        PendingPrintJobs.Jobs[jobId] = new PendingJobInfo { TenantId = 2, ShopId = 9, Copies = 2, CreatedAt = DateTime.UtcNow };

        try
        {
            using var agentB = TestHelpers.CreateClient(_factory);
            var pendingB = await TestHelpers.GetWithKeyAsync(agentB, "/api/pdf/print-agent/pending", keyB);
            Assert.Equal(HttpStatusCode.OK, pendingB.StatusCode);
            var bodyB = await TestHelpers.ReadJsonAsync(pendingB);
            Assert.Equal(2, bodyB.GetProperty("tenantId").GetInt32());       // key resolves to tenant 2
            Assert.Contains(jobId, bodyB.GetProperty("jobs").EnumerateArray().Select(j => j.GetString()));

            // Tenant A's key must NOT see tenant B's jobs
            using var sa = TestHelpers.CreateClient(_factory);
            await TestHelpers.LoginAsync(sa, DbSeeder.SysAdminEmail, DbSeeder.SysAdminPassword);
            var keyAResp = await sa.PostAsync("/api/sa/tenants/1/apikeys", null);
            var keyABody = await TestHelpers.ReadJsonAsync(keyAResp);
            var keyA = keyABody.GetProperty("apiKey").GetString()!;

            using var agentA = TestHelpers.CreateClient(_factory);
            var pendingA = await TestHelpers.GetWithKeyAsync(agentA, "/api/pdf/print-agent/pending", keyA);
            var bodyA = await TestHelpers.ReadJsonAsync(pendingA);
            Assert.Equal(1, bodyA.GetProperty("tenantId").GetInt32());  // key resolves to tenant 1
            Assert.DoesNotContain(jobId, bodyA.GetProperty("jobs").EnumerateArray().Select(j => j.GetString()));
        }
        finally
        {
            PendingPrintJobs.Jobs.TryRemove(jobId, out _);
        }
    }

    [Fact]
    public async Task Analytics_OnlyShowOwnTenantPrints()
    {
        var (_, _) = await ProvisionTenantBAsync();   // tenant 2 must exist before FK inserts

        using var sa = TestHelpers.CreateClient(_factory);
        await TestHelpers.LoginAsync(sa, DbSeeder.SysAdminEmail, DbSeeder.SysAdminPassword);

        // Seed print logs on both tenants
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tx = await db.Database.BeginTransactionAsync();

        var shopA = new Shop { Name = "Shop A", TenantId = 1 };
        var shopB = new Shop { Name = "Shop B", TenantId = 2 };
        db.Shops.AddRange(shopA, shopB);
        await db.SaveChangesAsync();

        var bookA = new Book { Title = "Math 101", FilePath = "/pdfs/book-a.pdf", TenantId = 1, BoardId = 1 };
        var bookB = new Book { Title = "Physics", FilePath = "/pdfs/book-b.pdf", TenantId = 2, BoardId = 1 };
        db.Books.AddRange(bookA, bookB);
        await db.SaveChangesAsync();

        db.PrintLogs.Add(new PrintLog
        {
            ShopId = shopA.Id, BookId = bookA.Id, ShopName = "Shop A", BookTitle = "Math 101",
            Copies = 3, TenantId = 1, PrintedAt = DateTime.UtcNow
        });
        db.PrintLogs.Add(new PrintLog
        {
            ShopId = shopB.Id, BookId = bookB.Id, ShopName = "Shop B", BookTitle = "Physics",
            Copies = 42, TenantId = 2, PrintedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        // Tenant A teacher only sees A's prints
        using var teacherA = TestHelpers.CreateClient(_factory);
        await TestHelpers.LoginAsync(teacherA, "admin@printingbooks.com", "Admin@123");
        var sumA = await TestHelpers.ReadJsonAsync(await teacherA.GetAsync("/api/analytics/print-summary"));
        var perShopA = sumA.GetProperty("perShop");
        Assert.Single(perShopA.EnumerateArray());
        Assert.Equal("Shop A", perShopA[0].GetProperty("shopName").GetString());

        // SystemAdmin sees both
        var sumSA = await TestHelpers.ReadJsonAsync(await sa.GetAsync("/api/analytics/print-summary"));
        Assert.Equal(2, sumSA.GetProperty("perShop").EnumerateArray().Count());
    }

    [Fact]
    public async Task DeactivatedTenant_CannotLogIn()
    {
        var (_, teacherB) = await ProvisionTenantBAsync();

        using var sa = TestHelpers.CreateClient(_factory);
        await TestHelpers.LoginAsync(sa, DbSeeder.SysAdminEmail, DbSeeder.SysAdminPassword);
        var list = await TestHelpers.ReadJsonAsync(await sa.GetAsync("/api/sa/teachers"));
        var b = list.GetProperty("teachers").EnumerateArray()
            .First(t => t.GetProperty("contactEmail").GetString() == teacherB);

        // Login works while active
        using var client = TestHelpers.CreateClient(_factory, allowRedirects: false);
        var loc = await TestHelpers.LoginAsync(client, teacherB, "TeacherB@123");
        Assert.EndsWith("/admin/dashboard", loc);

        // Deactivate → login must bounce back with a message
        var deact = await sa.PostAsync($"/api/sa/teachers/{b.GetProperty("id").GetInt32()}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deact.StatusCode);

        using var client2 = TestHelpers.CreateClient(_factory, allowRedirects: false);
        var loc2 = await TestHelpers.LoginAsync(client2, teacherB, "TeacherB@123");
        Assert.Contains("error=", loc2);
        Assert.DoesNotContain("/dashboard", loc2);

        // Reactivate → works again
        await sa.PostAsync($"/api/sa/teachers/{b.GetProperty("id").GetInt32()}/activate", null);
        using var client3 = TestHelpers.CreateClient(_factory, allowRedirects: false);
        var loc3 = await TestHelpers.LoginAsync(client3, teacherB, "TeacherB@123");
        Assert.EndsWith("/admin/dashboard", loc3);
    }
}