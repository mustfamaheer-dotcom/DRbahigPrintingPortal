# DR. Bahig Printing Portal — SaaS Technical Specification

> Complete, implementation-ready technical specification to convert the current single-tenant system into a fully functional multi-tenant SaaS. Every section is mapped to the **actual files in this repository** — copy-paste ready code, DDL, API contracts, migration SQL, and a deployment runbook.

| | |
|---|---|
| **Product** | DR. Bahig Printing Portal |
| **Repository root** | `E:\WORK\FreeLance\ENG Baheeg\BooksPortal\` |
| **Web app** | `PrintingBooksPortal\` (.NET 10, Blazor Server) |
| **Agent** | `BookShopPrintAgent\` (Kestrel + SumatraPDF polling agent) |
| **Production** | `https://drbaheegbook.runasp.net` (RunASP.NET, IIS `site79455`, SQL Server `db59750.databaseasp.net`) |
| **Status** | Spec complete — implementation not started |

---

## Table of Contents

1. [Locked Decisions](#1-locked-decisions)
2. [Target Architecture](#2-target-architecture)
3. [Database Specification (DDL + EF)](#3-database-specification-ddl--ef)
4. [Tenant Context & Authentication](#4-tenant-context--authentication)
5. [API Endpoint Contracts](#5-api-endpoint-contracts)
6. [SignalR Hub Design](#6-signalr-hub-design)
7. [Job Queue & Agent Endpoints](#7-job-queue--agent-endpoints)
8. [Service Changes (code-level)](#8-service-changes-code-level)
9. [Blazor UI Changes](#9-blazor-ui-changes)
10. [SystemAdmin Area (new)](#10-systemadmin-area-new)
11. [Agent Changes](#11-agent-changes)
12. [EF Migrations & Backfill SQL](#12-ef-migrations--backfill-sql)
13. [Test Project Layout](#13-test-project-layout)
14. [Feature Flag & Rollback](#14-feature-flag--rollback)
15. [Deployment Runbook](#15-deployment-runbook)
16. [Documentation Updates](#16-documentation-updates)
17. [Acceptance Criteria](#17-acceptance-criteria)
18. [Risk Register](#18-risk-register)

---

## 1. Locked Decisions

| Decision | Choice | Why |
|---|---|---|
| Tenant resolution | Session/claim-based. Single URL. `TenantId` from authenticated user's claims. | RunASP.NET shared hosting: no wildcard subdomains/TLS. |
| Database | Single SQL Server DB, `TenantId` columns + EF global query filters | Shared hosting limit; row-level isolation is standard. |
| SystemAdmin access to tenant data | `.IgnoreQueryFilters()` ONLY inside `/sa` endpoints; read-only drill-down | Fail-closed default; global visibility is explicit. |
| Agent communication | **Polling remains the baseline** (current agent works). SignalR is optional enhancement, not required for SaaS. | Zero agent rewrite needed for multi-tenancy; only portal-side tenant scoping. |
| Billing / Email / Self-signup | Deferred (schema prepared) | Core SaaS first. |
| Roles | `SystemAdmin` (global), `Teacher` (tenant), `Shop` (tenant) | |
| Feature flag | `MultiTenancy:Enabled` in appsettings — instant rollback path | |

---

## 2. Target Architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│                       PORTAL (single deployment)                         │
│                                                                          │
│  REST API (Controllers)      Blazor Server (Components/Pages)            │
│  /api/pdf/*                  /sa/*          SystemAdmin (global)         │
│  /api/sa/*        (NEW)      /admin/*       Teacher (tenant)             │
│  /api/analytics/*            /shop/*        Shop (tenant)                │
│  /api/admin/*                                                             │
│                                                                          │
│  Identity: SystemAdmin / Teacher / Shop  +  "TenantId" claim             │
│  ITenantContext (scoped, claims-based)                                   │
│  AppDbContext: global query filters on TenantId                          │
│  PrintHub (/hubs/print)  → groups: Shop_{tid}, PrintAgents_{tid}         │
└──────────────────────────────────────────────────────────────────────────┘
        │                                  │
   SQL Server (1 DB)                  Local Agents (per shop)
   Tenants, TenantApiKeys (NEW)       GET /api/pdf/print-agent/pending
   +TenantId on 7 tables              X-Api-Key: <tenant key> (SHA-256)
```

### 2.1 Tenant isolation rules

| Rule | Enforcement |
|---|---|
| Tenant-scoped queries | EF `HasQueryFilter` — automatic, cannot be forgotten |
| TenantId spoofing | Never read TenantId from request body/query — claims only |
| SystemAdmin | No TenantId claim → filters return nothing (fail closed) → SystemAdmin queries use `.IgnoreQueryFilters()` explicitly |
| Deactivated tenant | Login blocked at sign-in; per-request check in `TenantActivityMiddleware` (all roles) |
| Agent isolation | API key → TenantId; job queue endpoints filter by it |

---

## 3. Database Specification (DDL + EF)

### 3.1 New entity: `Tenant`

```csharp
// Models/Tenant.cs
public class Tenant
{
    public int Id { get; set; }
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;          // workspace name
    [MaxLength(200)] public string? OwnerName { get; set; }   // teacher's real name
    [MaxLength(200)] public string? ContactEmail { get; set; }
    [MaxLength(50)]  public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Future billing fields (reserved)
    public int? MaxShops { get; set; }
    public int? MaxBooks { get; set; }
    [MaxLength(50)] public string? Plan { get; set; }

    public ICollection<Shop> Shops { get; set; } = new List<Shop>();
    public ICollection<Book> Books { get; set; } = new List<Book>();
    public ICollection<EducationalBoard> Boards { get; set; } = new List<EducationalBoard>();
    public ICollection<ShopBookAssignment> Assignments { get; set; } = new List<ShopBookAssignment>();
    public ICollection<PrintLog> PrintLogs { get; set; } = new List<PrintLog>();
    public ICollection<SystemSetting> Settings { get; set; } = new List<SystemSetting>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<TenantApiKey> ApiKeys { get; set; } = new List<TenantApiKey>();
}
```

### 3.2 New entity: `TenantApiKey` (per-tenant agent key)

```csharp
// Models/TenantApiKey.cs
public class TenantApiKey
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    [Required, MaxLength(64)] public string KeyHash { get; set; } = string.Empty;  // SHA-256 hex
    [Required, MaxLength(8)]  public string KeyPrefix { get; set; } = string.Empty; // display only, e.g. "bpk_ab12"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Key format: `bpk_{Guid:N}` (e.g. `bpk_9f3c...`). Store only `SHA256(key)` in `KeyHash`. `KeyPrefix` = first 8 chars after `bpk_` for admin display.

### 3.3 Modified entities — add `TenantId`

| File | Add | Notes |
|---|---|---|
| `Models/ApplicationUser.cs` | `public int? TenantId { get; set; }` + `public Tenant? Tenant { get; set; }` | Null for SystemAdmin; required for Teacher/Shop |
| `Models/Shop.cs` | `public int TenantId { get; set; }` + nav | |
| `Models/Book.cs` | `public int TenantId { get; set; }` + nav | |
| `Models/EducationalBoard.cs` | `public int TenantId { get; set; }` + nav | |
| `Models/ShopBookAssignment.cs` | `public int TenantId { get; set; }` + nav | UK stays `(ShopId, BookId)` — ShopIds are globally unique identity values |
| `Models/PrintLog.cs` | `public int TenantId { get; set; }` + nav | Needed for global analytics |
| `Models/SystemSetting.cs` | `public int TenantId { get; set; }` + nav | **UK changes** to `(TenantId, Key)` — Key collides across tenants |

### 3.4 DDL (SQL Server — production)

```sql
-- 1. Tenants table
CREATE TABLE [Tenants] (
    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Name] nvarchar(200) NOT NULL,
    [OwnerName] nvarchar(200) NULL,
    [ContactEmail] nvarchar(200) NULL,
    [Phone] nvarchar(50) NULL,
    [IsActive] bit NOT NULL DEFAULT 1,
    [CreatedAt] datetime2 NOT NULL DEFAULT SYSDATETIME(),
    [MaxShops] int NULL,
    [MaxBooks] int NULL,
    [Plan] nvarchar(50) NULL
);

-- 2. TenantApiKeys table
CREATE TABLE [TenantApiKeys] (
    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [TenantId] int NOT NULL,
    [KeyHash] nvarchar(64) NOT NULL,
    [KeyPrefix] nvarchar(8) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT 1,
    [CreatedAt] datetime2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT [FK_TenantApiKeys_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]) ON DELETE CASCADE
);
CREATE UNIQUE INDEX [IX_TenantApiKeys_KeyHash] ON [TenantApiKeys] ([KeyHash]);
CREATE INDEX [IX_TenantApiKeys_TenantId] ON [TenantApiKeys] ([TenantId]);

-- 3. Add TenantId to existing tables (default 1 = default tenant; backfill in §12)
ALTER TABLE [Shops]                  ADD [TenantId] int NOT NULL DEFAULT 1;
ALTER TABLE [Books]                  ADD [TenantId] int NOT NULL DEFAULT 1;
ALTER TABLE [EducationalBoards]      ADD [TenantId] int NOT NULL DEFAULT 1;
ALTER TABLE [ShopBookAssignments]    ADD [TenantId] int NOT NULL DEFAULT 1;
ALTER TABLE [PrintLogs]              ADD [TenantId] int NOT NULL DEFAULT 1;
ALTER TABLE [SystemSettings]         ADD [TenantId] int NOT NULL DEFAULT 1;
ALTER TABLE [AspNetUsers]            ADD [TenantId] int NULL;

ALTER TABLE [Shops]               ADD CONSTRAINT [FK_Shops_Tenants]              FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);
ALTER TABLE [Books]               ADD CONSTRAINT [FK_Books_Tenants]              FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);
ALTER TABLE [EducationalBoards]   ADD CONSTRAINT [FK_EducationalBoards_Tenants]  FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);
ALTER TABLE [ShopBookAssignments] ADD CONSTRAINT [FK_ShopBookAssignments_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);
ALTER TABLE [PrintLogs]           ADD CONSTRAINT [FK_PrintLogs_Tenants]          FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);
ALTER TABLE [SystemSettings]      ADD CONSTRAINT [FK_SystemSettings_Tenants]     FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);
ALTER TABLE [AspNetUsers]         ADD CONSTRAINT [FK_AspNetUsers_Tenants]        FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);

-- 4. Indexes on TenantId (query performance)
CREATE INDEX [IX_Shops_TenantId]                  ON [Shops] ([TenantId]);
CREATE INDEX [IX_Books_TenantId]                  ON [Books] ([TenantId]);
CREATE INDEX [IX_EducationalBoards_TenantId]      ON [EducationalBoards] ([TenantId]);
CREATE INDEX [IX_ShopBookAssignments_TenantId]    ON [ShopBookAssignments] ([TenantId]);
CREATE INDEX [IX_PrintLogs_TenantId]              ON [PrintLogs] ([TenantId]);
CREATE INDEX [IX_SystemSettings_TenantId]         ON [SystemSettings] ([TenantId]);
CREATE INDEX [IX_AspNetUsers_TenantId]            ON [AspNetUsers] ([TenantId]);

-- 5. SystemSettings unique key becomes per-tenant
DROP INDEX [IX_SystemSettings_Key] ON [SystemSettings];
CREATE UNIQUE INDEX [IX_SystemSettings_TenantId_Key] ON [SystemSettings] ([TenantId], [Key]);

-- 6. Roles
INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
SELECT NEWID(), 'SystemAdmin', 'SYSTEMADMIN', NEWID()
WHERE NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Name] = 'SystemAdmin');
INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
SELECT NEWID(), 'Teacher', 'TEACHER', NEWID()
WHERE NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Name] = 'Teacher');
```

### 3.5 `AppDbContext` changes (code)

```csharp
// Data/AppDbContext.cs — full replacement of OnModelCreating + ctor
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext? _tenantContext;   // scoped; null in design-time
    private readonly bool _multiTenancy;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null, bool multiTenancy = true)
        : base(options)
    {
        _tenantContext = tenantContext;
        _multiTenancy = multiTenancy;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<EducationalBoard> EducationalBoards => Set<EducationalBoard>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<ShopBookAssignment> ShopBookAssignments => Set<ShopBookAssignment>();
    public DbSet<PrintLog> PrintLogs => Set<PrintLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── existing config ──
        builder.Entity<ShopBookAssignment>().HasIndex(a => new { a.ShopId, a.BookId }).IsUnique();
        builder.Entity<PrintLog>().HasIndex(l => l.PrintedAt);
        builder.Entity<PrintLog>().HasIndex(l => l.ShopId);
        builder.Entity<PrintLog>().HasIndex(l => l.BookId);

        // ── new config ──
        builder.Entity<SystemSetting>().HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
        builder.Entity<TenantApiKey>().HasIndex(k => k.KeyHash).IsUnique();
        builder.Entity<TenantApiKey>().HasIndex(k => k.TenantId);
        builder.Entity<PrintLog>().HasIndex(l => l.TenantId);
        builder.Entity<Book>().HasIndex(b => b.TenantId);
        builder.Entity<Shop>().HasIndex(s => s.TenantId);
        builder.Entity<EducationalBoard>().HasIndex(b => b.TenantId);
        builder.Entity<ShopBookAssignment>().HasIndex(a => a.TenantId);
        builder.Entity<SystemSetting>().HasIndex(s => s.TenantId);
        builder.Entity<ApplicationUser>().HasIndex(u => u.TenantId);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Tenant).WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);

        // ── global query filters (multi-tenancy on) ──
        if (_multiTenancy && _tenantContext != null)
        {
            builder.Entity<Shop>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
            builder.Entity<EducationalBoard>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
            builder.Entity<Book>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
            builder.Entity<ShopBookAssignment>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
            builder.Entity<PrintLog>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
            builder.Entity<SystemSetting>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        }
    }
}
```

> **EF gotcha (important):** the query filter expression captures the scoped `_tenantContext` instance. It is re-evaluated per query against the **current** context's instance — this is the documented multi-tenant pattern and works correctly with scoped DbContext + scoped ITenantContext. Do **not** cache the TenantId in a static field.

> **Design-time factory:** `Data/DesignTimeDbContextFactory.cs` must pass `tenantContext: null, multiTenancy: false` to `AppDbContext` (filters skipped at design time), or `dotnet ef` commands fail.

---

## 4. Tenant Context & Authentication

### 4.1 `ITenantContext` + implementation

```csharp
// Services/ITenantContext.cs
public interface ITenantContext
{
    int TenantId { get; }                 // 0 = no tenant (SystemAdmin or unauthenticated)
    bool IsSystemAdmin { get; }
    void Initialize(ClaimsPrincipal user); // called by Blazor circuits (claims only, no DB)
}

// Services/TenantContext.cs
public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    private int? _circuitTenantId;

    public TenantContext(IHttpContextAccessor http) => _http = http;

    public void Initialize(ClaimsPrincipal user)
    {
        _circuitTenantId = ParseTenantId(user);
    }

    public int TenantId
    {
        get
        {
            if (_circuitTenantId.HasValue) return _circuitTenantId.Value;
            var user = _http.HttpContext?.User;
            return user == null ? 0 : ParseTenantId(user);
        }
    }

    public bool IsSystemAdmin
    {
        get
        {
            var user = _http.HttpContext?.User;
            return user != null && user.IsInRole("SystemAdmin");
        }
    }

    private static int ParseTenantId(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true) return 0;
        var claim = user.FindFirstValue("TenantId");
        return int.TryParse(claim, out var id) ? id : 0;
    }
}
```

### 4.2 Claims factory — adds `TenantId` claim at sign-in

```csharp
// Services/TenantClaimsPrincipalFactory.cs
public class TenantClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public TenantClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.TenantId.HasValue)
            identity.AddClaim(new Claim("TenantId", user.TenantId.Value.ToString()));
        return identity;
    }
}
```

Register in `Program.cs`:
```csharp
builder.Services.AddIdentityCore<ApplicationUser>(options => { /* existing options */ })
    .AddRoles<IdentityRole>()
    .AddClaimsPrincipalFactory<TenantClaimsPrincipalFactory>()
    .AddEntityFrameworkStores<AppDbContext>();
```

### 4.3 Tenant activity enforcement (deactivation)

```csharp
// Middleware/TenantActivityMiddleware.cs
public class TenantActivityMiddleware
{
    private readonly RequestDelegate _next;
    public TenantActivityMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantId = int.TryParse(user.FindFirstValue("TenantId"), out var tid) ? tid : 0;
            var isTenantUser = !user.IsInRole("SystemAdmin");
            if (isTenantUser && tenantId > 0)
            {
                var active = await db.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive);
                if (!active)
                {
                    // Deactivated tenant → kill access now (login already blocked at sign-in)
                    context.Response.Redirect("/access-denied");
                    return;
                }
            }
        }
        await _next(context);
    }
}
```

Register after `UseAuthentication()` / before `UseAuthorization()`:
```csharp
app.UseAuthentication();
app.UseMiddleware<TenantActivityMiddleware>();
app.UseAuthorization();
```

### 4.4 Policies (replaces `AdminOnly`/`ShopOnly`)

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",   policy => policy.RequireRole("Admin")); // legacy — see 4.5
    options.AddPolicy("ShopOnly",    policy => policy.RequireRole("Shop"));
    options.AddPolicy("SystemAdminOnly", policy => policy.RequireRole("SystemAdmin"));
    options.AddPolicy("TenantAdmin",     policy => policy.RequireRole("Teacher", "SystemAdmin"));
    options.AddPolicy("TenantUser",      policy => policy.RequireRole("Teacher", "Shop"));
});
```

### 4.5 Role migration strategy (existing Admin → Teacher)

- Add role `Teacher`; keep `Admin` role in code during transition.
- `DbSeeder` promotes every existing `Admin` user to `Teacher` and sets their `TenantId = 1`; then (after acceptance) `Admin` role can be removed.
- Page attributes change from `[Authorize(Roles = "Admin")]` → `[Authorize(Roles = "Teacher,SystemAdmin")]` — the string literal is the role list; no separate policy needed for pages.

### 4.6 `LoginController` changes (code)

```csharp
[HttpPost("login")]
[IgnoreAntiforgeryToken]
public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] bool rememberMe)
{
    var result = await _signInManager.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: false);
    if (!result.Succeeded)
        return Redirect("/login?error=" + Uri.EscapeDataString("Invalid email or password"));

    var user = await _userManager.FindByEmailAsync(email);
    if (user == null) return Redirect("/");

    // Block deactivated tenant users at sign-in
    if (user.TenantId.HasValue)
    {
        var db = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.FindAsync(user.TenantId.Value);
        if (tenant == null || !tenant.IsActive)
        {
            await _signInManager.SignOutAsync();
            return Redirect("/login?error=" + Uri.EscapeDataString("Account is disabled. Contact your administrator."));
        }
    }

    if (await _userManager.IsInRoleAsync(user, "SystemAdmin")) return Redirect("/sa/dashboard");
    if (await _userManager.IsInRoleAsync(user, "Teacher"))    return Redirect("/admin/dashboard");
    if (await _userManager.IsInRoleAsync(user, "Shop"))       return Redirect("/shop/mybooks");
    return Redirect("/");
}
```

### 4.7 DI registrations (`Program.cs`)

```csharp
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<AppDbContext>();          // already registered via AddDbContext
// note: AddDbContext is called BEFORE; AppDbContext ctor resolves ITenantContext automatically
builder.Services.AddSingleton<IApiKeyService, ApiKeyService>();   // §7.1
```

---

## 5. API Endpoint Contracts

### 5.1 New SystemAdmin API — `Controllers/SystemAdminController.cs`

Route: `/api/sa` — `[Authorize(Roles = "SystemAdmin")]` on the whole controller. All responses `application/json`.

**GET `/api/sa/teachers`** — list all teachers with stats
```json
200 OK
{
  "teachers": [
    {
      "id": 3,
      "name": "Ahmed's Academy",
      "ownerName": "Ahmed Hassan",
      "contactEmail": "ahmed@example.com",
      "phone": "+201234567890",
      "isActive": true,
      "createdAt": "2026-07-01T10:00:00Z",
      "stats": { "shops": 4, "books": 12, "boards": 3, "prints": 145 }
    }
  ],
  "totalCount": 7
}
```

**POST `/api/sa/teachers`** — create teacher + account
```json
Request:
{
  "name": "Ahmed's Academy",              // required
  "ownerName": "Ahmed Hassan",
  "contactEmail": "ahmed@example.com",    // required; must be unique (Identity)
  "phone": "+201234567890",
  "password": "Temp@1234",                // required, min 6 chars + digit
  "maxShops": null,
  "maxBooks": null,
  "plan": null
}
201 Created
{
  "id": 3,
  "name": "Ahmed's Academy",
  "contactEmail": "ahmed@example.com",
  "userName": "ahmed@example.com",
  "message": "Teacher created. Account: ahmed@example.com"
}
409 Conflict  { "error": "A user with this email already exists." }
400 BadRequest { "error": "Name and contact email are required." }
```

**PUT `/api/sa/teachers/{id}`** — update profile
```json
Request: { "name": "...", "ownerName": "...", "contactEmail": "...", "phone": "...", "maxShops": 10, "maxBooks": 50, "plan": "Pro" }
200 OK { "id": 3, "name": "...", "message": "Teacher updated." }
404 { "error": "Teacher not found." }
```

**POST `/api/sa/teachers/{id}/deactivate`** → `200 { "success": true }` (blocks all users of tenant via §4.3)
**POST `/api/sa/teachers/{id}/activate`** → `200 { "success": true }`

**POST `/api/sa/teachers/{id}/reset-password`**
```json
Request: { "newPassword": "New@1234" }
200 OK { "success": true, "message": "Password updated." }
400 { "error": "Password does not meet requirements." }
```

**DELETE `/api/sa/teachers/{id}`**
```json
200 OK { "success": true, "deleted": true }
409 { "error": "Tenant has data (shops/books/print logs). Deactivate instead." }
```

**GET `/api/sa/analytics/summary`**
```json
{
  "totals": { "tenants": 7, "activeTenants": 6, "shops": 23, "books": 41, "boards": 9, "prints": 1204 },
  "printTrends30d": [ { "date": "2026-07-30", "copies": 34 } ],
  "perTenant": [ { "tenantId": 3, "tenantName": "Ahmed's Academy", "shops": 4, "prints": 145 } ]
}
```

**GET `/api/sa/tenants/{id}`** — read-only drill-down
```json
{
  "tenant": { "id": 3, "name": "...", "ownerName": "...", "isActive": true, "createdAt": "..." },
  "shops": [ { "id": 9, "name": "Cairo Bookstore", "users": 2, "prints": 61 } ],
  "books": [ { "id": 22, "title": "Math Grade 9", "pageCount": 128, "prints": 12 } ],
  "printLogs": [ { "id": 5001, "shopName": "Cairo Bookstore", "bookTitle": "Math Grade 9", "copies": 2, "printedAt": "2026-07-30T12:00:00Z" } ],
  "apiKeys": [ { "id": 2, "prefix": "bpk_ab12", "isActive": true, "createdAt": "2026-07-20T08:00:00Z" } ]
}
```

**GET `/api/sa/tenants/{id}/apikeys`** → `{ "apiKeys": [...] }`
**POST `/api/sa/tenants/{id}/apikeys`** — create new key
```json
201 Created
{ "apiKey": "bpk_9f3c1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a",
  "prefix": "bpk_9f3c",
  "message": "Store this key now — it is shown only once." }
```
**POST `/api/sa/tenants/{id}/apikeys/{keyId}/revoke`** → `200 { "success": true }`

### 5.2 Modified existing endpoints

| Endpoint | Change |
|---|---|
| `GET /api/pdf/view-secure/{bookId}` | `[Authorize(Roles = "Shop,Teacher,SystemAdmin")]`; `ValidateAccess` scoped by `_tenantContext.TenantId` |
| `POST /api/pdf/process-print` | Shop-only (unchanged); job stores `TenantId` (§7) |
| `GET /api/pdf/print-file/{jobId}` | `IsJobOwnerAsync` also verifies `info.TenantId == _tenantContext.TenantId` |
| `GET /api/pdf/download-secured/{jobId}` | Agent path: key → tenant; reject if job.TenantId != key.TenantId |
| `GET /api/pdf/print-agent/pending` | Filters jobs by agent's tenant |
| `POST /api/pdf/print-agent/claim/{jobId}` | Reject if job.TenantId != key.TenantId |
| `POST /api/pdf/print-agent/release/{jobId}` | Re-add with original info incl. TenantId (fix current bug: release recreates with ShopId=0) |
| `GET /api/admin/shop-receipt/{shopId}` | Tenant-scoped via filters; policy `TenantAdmin` |
| `POST /api/admin/reset-shop-stats/{shopId}` | Tenant-scoped; policy `TenantAdmin` |
| `GET /api/analytics/print-summary` | Filters apply (Teacher sees own); SystemAdmin uses new `/api/sa` endpoints |
| `GET /api/analytics/print-trends` | same |

---

## 6. SignalR Hub Design

### 6.1 Group naming

| Group | Membership | Purpose |
|---|---|---|
| `Shop_{tenantId}` | Browser clients (authenticated Shop role, same tenant) | Job status updates to the shop that submitted |
| `PrintAgents_{tenantId}` | Agents with valid tenant API key | `NewPrintJob` push notification (optional; polling is baseline) |

### 6.2 `Hubs/PrintHub.cs` (replacement)

```csharp
[AllowAnonymous] // enforcement is in OnConnectedAsync (agents have no cookie)
public class PrintHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IApiKeyService _apiKeys;
    private readonly ILogger<PrintHub> _logger;

    public PrintHub(AppDbContext db, IApiKeyService apiKeys, ILogger<PrintHub> logger)
    {
        _db = db; _apiKeys = apiKeys; _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Path 1: Agent (API key in query "access_token" or header "X-Api-Key")
        var key = Context.GetHttpContext()?.Request.Query["access_token"].FirstOrDefault()
                  ?? Context.GetHttpContext()?.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(key))
        {
            var tenantId = _apiKeys.ResolveTenant(key);
            if (tenantId > 0)
            {
                Context.Items["TenantId"] = tenantId;
                await Groups.AddToGroupAsync(Context.ConnectionId, $"PrintAgents_{tenantId}");
                _logger.LogInformation("Agent connected for tenant {TenantId}: {ConnectionId}", tenantId, Context.ConnectionId);
                await base.OnConnectedAsync();
                return;
            }
            Context.Abort();
            return;
        }

        // Path 2: Browser (cookie auth)
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated == true && user.IsInRole("Shop"))
        {
            var tid = int.TryParse(user.FindFirstValue("TenantId"), out var t) ? t : 0;
            if (tid > 0)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Shop_{tid}");
                await base.OnConnectedAsync();
                return;
            }
        }
        Context.Abort(); // reject everything else
    }
}
```

### 6.3 Server-side broadcast (used by `ProcessPrint` / `JobStatusTracker`)

```csharp
// from SecurePdfController.ProcessPrint after job added:
await _hubContext.Clients.Group($"PrintAgents_{tenantId}")
    .SendAsync("NewPrintJob", jobId, book.Title, shopName);

// job status (Queued → Processing → Completed/Failed):
await _hubContext.Clients.Group($"Shop_{tenantId}")
    .SendAsync("PrintStatusChanged", jobId, status, message);
```

### 6.4 Browser client (`wwwroot/js/pdfViewer.js`)

```js
// connect with cookie auth (same origin) — no change to connection string
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/print')        // cookie sent automatically
  .withAutomaticReconnect()
  .build();
connection.on('PrintStatusChanged', (jobId, status, message) => {
  if (jobId === currentJobId) updatePrintModal(status, message);
});
```

> Agent SignalR client (optional enhancement, not required): connect with `.withUrl(`${baseUrl}/hubs/print?access_token=${apiKey}`, { transport: HttpTransportType.WebSockets })` on the .NET client; on `NewPrintJob(jobId)` trigger immediate poll. Keep the 3s polling loop as fallback.

---

## 7. Job Queue & Agent Endpoints

### 7.1 `IApiKeyService` (new, singleton)

```csharp
// Services/IApiKeyService.cs
public interface IApiKeyService
{
    string GenerateKey(int tenantId);          // "bpk_" + Guid:N; stores SHA-256 hash; returns plaintext once
    int ResolveTenant(string apiKey);          // 0 if invalid/inactive
    void RevokeKey(int keyId);
    List<TenantApiKey> ListKeys(int tenantId);
}
```

Implementation notes:
- `SHA256.HashData(Encoding.UTF8.GetBytes(key))` → hex string → `KeyHash`.
- Lookup: `TenantApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive)` → `TenantId`.
- Cache `Dictionary<string,int>` in memory (ConcurrentDictionary) with TTL 60s — optional; scale is tiny, direct DB lookup is fine.

### 7.2 `PendingJobInfo` + queue (update existing class)

```csharp
public class PendingJobInfo
{
    public int TenantId { get; set; }          // NEW — required
    public int ShopId { get; set; }
    public int Copies { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Duplex { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}
```

### 7.3 Agent endpoint logic (pseudocode with tenant checks)

```
GET /api/pdf/print-agent/pending
  tenantId = _apiKeys.ResolveTenant(X-Api-Key)         // 0 → 401
  if tenantId == 0: 401
  jobs = PendingPrintJobs.Jobs.Where(j => j.Value.TenantId == tenantId && not expired)
  return { jobs: [ids] }

POST /api/pdf/print-agent/claim/{jobId}
  tenantId = resolve(key); if 0 → 401
  if !Jobs.TryGetValue(jobId, out info) → 404
  if info.TenantId != tenantId → 403   // cross-tenant claim blocked
  Jobs.TryRemove(jobId, out info)
  return { success, jobId, copies, printerName, paperSize, duplex, scalingMode, customScale,
           marginTop, marginBottom, marginLeft, marginRight, marginUnit }

POST /api/pdf/print-agent/release/{jobId}
  tenantId = resolve(key); if 0 → 401
  if !Jobs.ContainsKey(jobId):
     Jobs.TryAdd(jobId, originalInfo)   // MUST reuse claimed info (incl. TenantId + settings) — not ShopId=0 stub
  return { success }

GET /api/pdf/download-secured/{jobId}
  tenantId = resolve(key) OR authenticated shop/teacher user
  if agent path: verify job.TenantId == tenantId (needs queue lookup; file deleted after first download → 404)
  serve file from SecurePrints/{tenantId}/{jobId}.pdf (per-tenant folder, §8.3)
```

### 7.4 `SecurePrints` per-tenant folder

```
SecurePrints/{tenantId}/{jobId}.pdf
```
- `ProcessPrint` writes to `SecurePrints/{tenant.TenantId}/`.
- `GetPrintFile`/`DownloadSecured` read from the job's tenant folder (from queue info or TenantId claim).

---

## 8. Service Changes (code-level)

### 8.1 `SettingsService` — per-tenant keys

All queries change from `s.Key == key` to `s.TenantId == _tenantContext.TenantId && s.Key == key`. Constructor gains `ITenantContext`. Default watermark text becomes tenant-aware:

```csharp
private readonly ITenantContext _tenant;
// DefaultWatermarkText = "LICENSED TO: {shopName}\nUSER: {userName}\nDATE: {date}\nDO NOT DISTRIBUTE"
// → "LICENSED TO: {tenantName} / {shopName}\nUSER: {userName}\nDATE: {date}\nDO NOT DISTRIBUTE"
```

> Note: `EnsureTableCreatedAsync` DDL fallback (lines 159-226) must also create `TenantId` column and the new unique index — replace `CREATE UNIQUE INDEX [IX_SystemSettings_Key]` with `CREATE UNIQUE INDEX [IX_SystemSettings_TenantId_Key] ON [SystemSettings] ([TenantId], [Key]);` (SQL Server + SQLite variants).

### 8.2 `FileStorageService` — tenant folders

```csharp
public FileStorageService(IWebHostEnvironment env, ITenantContext tenant)
{
    _storagePath = Path.Combine(env.ContentRootPath, "App_Data", tenant.TenantId.ToString(), "Books");
    Directory.CreateDirectory(_storagePath);
}
```
(If `TenantId == 0`, fall back to legacy path — covers SystemAdmin operations / feature-flag-off.)

### 8.3 `SecurePrints` path helper (new static or in `PdfSecurityService`)

```csharp
public static string GetSecureDir(int tenantId)
    => Path.Combine(Directory.GetCurrentDirectory(), "SecurePrints", tenantId.ToString());
```

### 8.4 `PrintLoggingService` — record tenant

- `LogPrintAsync(...)` gains `tenantId` param; `PrintLog.TenantId = tenantId`.
- `GetRecentLogsAsync`, `GetTotalPrintsAsync`, `GetPrintsPerShopAsync`, `GetPrintsPerBookAsync` — filters apply automatically (query filters); SystemAdmin global aggregates use new `/api/sa` endpoint with `.IgnoreQueryFilters()`.
- `GetShopLogsAsync` unchanged (ShopId is globally unique).

### 8.5 `WatermarkService` — tenant placeholder

`ApplyWatermark(..., string shopName, ...)` signature unchanged. New placeholder `{tenantName}` in text; `SecurePdfController`/`ProcessPrint` pass `tenant.Name` (loaded via `TenantId`). Add `{tenantName}` to `WatermarkService` placeholder replacement switch.

### 8.6 `PrintTokenService` — embed tenant

```csharp
// tuple gains TenantId
private readonly ConcurrentDictionary<string, (int BookId, int TenantId, string UserId, string ShopName, string UserName, DateTime Expires)> _tokens = new();
// GenerateToken(bookId, tenantId, userId, shopName, userName)
// ValidateToken → out int tenantId; PrintPdf(token) checks book.TenantId == tenantId
```

### 8.7 `PdfSecurityService` — unchanged logic; path resolution only

Encryption/decryption logic unchanged. Callers pass per-tenant paths.

---

## 9. Blazor UI Changes

### 9.1 `Components/Layout/NavMenu.razor` — role-based rendering

```razor
@if (role == "SystemAdmin")
{
    <div class="nav-group">Platform</div>
    <a href="/sa/dashboard">Dashboard</a>
    <a href="/sa/teachers">Teachers</a>
    <a href="/sa/analytics">Analytics</a>
}
else if (role == "Teacher")
{
    <!-- existing Admin menu items — unchanged URLs /admin/* -->
}
else if (role == "Shop")
{
    <!-- existing Shop menu items -->
}
```

### 9.2 `MainLayout.razor` — initialize tenant context

```razor
@inject ITenantContext TenantContext
@inject AuthenticationStateProvider AuthState
@code {
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        TenantContext.Initialize(state.User);
    }
}
```

### 9.3 Page attribute sweep

| File(s) | Change |
|---|---|
| `Components/Pages/Admin/*.razor` (8 pages) | `@attribute [Authorize(Roles = "Teacher,SystemAdmin")]` |
| `Components/Pages/Shop/*.razor` | `[Authorize(Roles = "Shop")]` unchanged |
| `Components/Pages/Public/Login.razor` | unchanged |
| `RedirectToLogin.razor` | unchanged (LoginController handles role redirect) |

### 9.4 Tenant badge (optional, in `MainLayout`)

```
👤 {TenantName}   ← from ITenantContext + SettingsService lookup on first render
```

### 9.5 Shop pages — `ShopId` stays globally unique

No changes needed to `MyBooks.razor`, `PrintHistory.razor`, `Viewer.razor` beyond the API changes above.

---

## 10. SystemAdmin Area (new)

### 10.1 Pages (Blazor Server, `Components/Pages/SystemAdmin/`)

| Page | Route | Features |
|---|---|---|
| `Dashboard.razor` | `/sa/dashboard` | Stat cards (tenants, active tenants, shops, books, prints) via `GET /api/sa/analytics/summary`; recent activity list |
| `Teachers.razor` | `/sa/teachers` | Table: name, contact, shops/books/prints, active toggle; buttons: Edit, Reset Password, Deactivate/Activate, Delete |
| `TeacherCreate.razor` | `/sa/teachers/create` | Form: name, ownerName, email, phone, password; POST /api/sa/teachers |
| `TeacherEdit.razor` | `/sa/teachers/{id}` | Profile edit + API key management (list, create w/ one-time copy, revoke) + tenant drill-down stats |
| `Analytics.razor` | `/sa/analytics` | Global trends chart (30d), per-tenant bar chart, totals |

All use `[Authorize(Roles = "SystemAdmin")]`.

### 10.2 Controller

`Controllers/SystemAdminController.cs` — `[Authorize(Roles = "SystemAdmin")]`, endpoints in §5.1. All cross-tenant queries use `.IgnoreQueryFilters()` **explicitly**. Read-only drill-down: no update/delete paths into tenant data.

---

## 11. Agent Changes

### 11.1 Baseline: **no agent code changes required** for SaaS

The agent authenticates with `X-Api-Key` and polls. Once the portal scopes the queue by key → tenant, the existing agent works unchanged for every tenant. The only per-shop config change is `appsettings.json`:

```json
{
  "ServerSettings": {
    "BaseUrl": "https://drbaheegbook.runasp.net",
    "ApiKey": "bpk_<tenant-specific-key-from-/api/sa/tenants/{id}/apikeys>",
    "OwnerPassword": "<unchanged>",
    "UseSignalR": false
  },
  "PrinterSettings": {
    "DefaultPrinterName": "",
    "Copies": 1
  }
}
```

### 11.2 Optional: `TenantId` in agent logs

`BookShopPrintAgent/Program.cs` — log line change only:
```csharp
Console.WriteLine($"[BookShopPrintAgent] Server: {baseUrl} (tenant key configured)");
```

### 11.3 `SetupBootstrapper/InstallerForm.cs` — add an "Agent Configuration" step

New installer page between "Install" and "Finish":
- Fields: **Portal URL** (pre-filled), **API Key** (required, validate format `bpk_*`), **Default Printer** (optional).
- On finish: writes `appsettings.json` with these values before starting the scheduled task.

### 11.4 Agent validation at startup (already present, extend)

Current startup validates `ApiKey` and `OwnerPassword` are set; extend to warn (not fail) if key doesn't start with `bpk_`.

---

## 12. EF Migrations & Backfill SQL

### 12.1 Generate migrations (dev machine)

```
cd PrintingBooksPortal
dotnet ef migrations add AddTenantAndTenantId
dotnet ef migrations add UpdateIdentityForTenant        # if ApplicationUser.TenantId added later
```

### 12.2 Migration content requirements

In `AddTenantAndTenantId.Up()` (order matters):

```
1. migrationBuilder.CreateTable("Tenants", ...)
2. migrationBuilder.Sql("INSERT INTO Tenants (Name, IsActive, CreatedAt) VALUES ('Default Tenant', 1, SYSDATETIME());")
   -- captures identity? No: ALTERs below use DEFAULT 1 constant, so tenant row must be Id=1.
   -- Ensure by using explicit Id: INSERT INTO Tenants (Id, Name, ...) VALUES (1, 'Default Tenant', ...)
3. migrationBuilder.AddColumn<int>("TenantId", "Shops", nullable: false, defaultValue: 1);
   ... (all 6 tables, defaultValue: 1)
4. migrationBuilder.AddColumn<int?>("TenantId", "AspNetUsers", nullable: true);
5. migrationBuilder.CreateIndex(...) for all TenantId columns
6. migrationBuilder.DropIndex("IX_SystemSettings_Key", "SystemSettings");
7. migrationBuilder.CreateIndex("IX_SystemSettings_TenantId_Key", "SystemSettings", columns: ["TenantId","Key"], unique: true);
8. migrationBuilder.CreateTable("TenantApiKeys", ...) + indexes
9. migrationBuilder.CreateIndex("IX_AspNetUsers_TenantId", "AspNetUsers", "TenantId");
10. migrationBuilder.Sql("INSERT INTO AspNetRoles ... 'SystemAdmin' / 'Teacher' (idempotent)")
```

> `INSERT INTO Tenants` **must** use explicit `Id = 1` so the `DEFAULT 1` backfill of existing rows points at the right tenant.

### 12.3 Backfill: promote existing Admin → Teacher (run after migration)

```sql
-- One-time: assign existing Shop users to tenant 1 (already default) — no-op, kept for completeness
-- Promote all existing Admins to Teacher role and ensure TenantId = 1
INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
SELECT u.[Id], r.[Id]
FROM [AspNetUsers] u
JOIN [AspNetUserRoles] ur ON ur.[UserId] = u.[Id]
JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId] AND r.[Name] = 'Admin'
WHERE NOT EXISTS (SELECT 1 FROM [AspNetUserRoles] ur2
                  JOIN [AspNetRoles] r2 ON r2.[Id] = ur2.[RoleId] AND r2.[Name] = 'Teacher'
                  WHERE ur2.[UserId] = u.[Id]);

UPDATE [AspNetUsers] SET [TenantId] = 1
WHERE [Id] IN (SELECT [UserId] FROM [AspNetUserRoles] ur JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId] WHERE r.[Name] = 'Teacher');
```

### 12.4 Validation SQL (run on staging copy before production)

```sql
SELECT COUNT(*) AS ShopsWithNoTenant   FROM Shops WHERE TenantId IS NULL OR TenantId = 0;
SELECT COUNT(*) AS BooksWithNoTenant   FROM Books WHERE TenantId IS NULL OR TenantId = 0;
SELECT COUNT(*) AS LogsWithNoTenant    FROM PrintLogs WHERE TenantId IS NULL OR TenantId = 0;
SELECT COUNT(*) AS AdminNotPromoted    FROM AspNetUsers u
  JOIN AspNetUserRoles ur ON ur.UserId = u.Id JOIN AspNetRoles r ON r.Id = ur.RoleId
  WHERE r.Name = 'Admin' AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur2
    JOIN AspNetRoles r2 ON r2.Id = ur2.RoleId WHERE ur2.UserId = u.Id AND r2.Name = 'Teacher');
SELECT COUNT(*) AS DupSettings        FROM SystemSettings GROUP BY TenantId, Key HAVING COUNT(*) > 1;
-- All five queries must return 0.
```

### 12.5 SQLite (dev) note

Add `TenantId` to the SQLite fallback DDL in `SettingsService.EnsureTableCreatedAsync` and dev `appsettings.Development.json` DB is recreated via `EnsureCreated` — dev DB can simply be deleted and re-seeded.

---

## 13. Test Project Layout

### 13.1 New project

```
PrintingBooksPortal.Tests/                (xUnit, net10.0)
├── PrintingBooksPortal.Tests.csproj      # refs Web app; packages: xunit, Microsoft.AspNetCore.Mvc.Testing 10.0.9,
│                                         # Microsoft.EntityFrameworkCore.Sqlite 10.0.9, Microsoft.NET.Test.Sdk
├── TestHelpers/
│   ├── TestAppFactory.cs                 # WebApplicationFactory<Program>; SQLite in-memory; seeds 2 tenants
│   ├── TestDataSeeder.cs                 # CreateTenant(name), CreateShop(tenantId, name), CreateBook(...)
│   └── TestAuth.cs                       # SignInAs(user) via cookie; or direct controller-level tests
└── Tests/
    ├── TenantIsolationTests.cs
    ├── SystemAdminApiTests.cs
    ├── AuthAndRoleTests.cs
    ├── AgentEndpointTests.cs
    └── MigrationBackfillTests.cs
```

### 13.2 `TestAppFactory` skeleton

```csharp
public class TestAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (d != null) services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=:memory:")); // shared in-memory
            // override ITenantContext per test via scoped service replacement
        });
    }
}
```

### 13.3 Test cases (names)

**TenantIsolationTests**
1. `Shop_FromTenantA_CannotSeeTenantB_Books`
2. `Shop_FromTenantA_CannotPrintTenantB_Book`
3. `TeacherA_CannotListTeacherB_Shops`
4. `TeacherA_CannotEditTeacherB_Book`
5. `Analytics_AreScoped_ToOwnTenant`
6. `Settings_AreScoped_ToOwnTenant` (two tenants, different watermark text)
7. `ShopBookAssignment_IsScoped_ToOwnTenant`

**SystemAdminApiTests**
8. `SystemAdmin_CanCreateTeacher` (201 + login works)
9. `Teacher_CannotCallSaEndpoints` (403)
10. `SystemAdmin_SeesAllTenants_InGlobalAnalytics`
11. `DeactivatedTenant_LoginBlocked`
12. `DeactivatedTenant_ExistingCookieDenied` (per-request middleware)
13. `DeleteTenant_WithData_Returns409`
14. `ResetTeacherPassword_Works`

**AgentEndpointTests**
15. `AgentWithTenantAKey_CannotClaim_TenantBJob` (403)
16. `Pending_OnlyReturns_OwnTenantJobs`
17. `ReleaseJob_RestoresOriginalSettings_AndTenant`
18. `InvalidApiKey_Returns401`

**AuthAndRoleTests**
19. `AdminUser_PromotedToTeacher_StillAccessesAdminPages`
20. `NoTenantIdClaim_QueriesReturnEmpty` (fail-closed)

**MigrationBackfillTests**
21. `BackfillSql_AssignsExistingData_ToTenant1`

### 13.4 Run command

```
dotnet test PrintingBooksPortal.Tests
```

---

## 14. Feature Flag & Rollback

### 14.1 `appsettings.Production.json`

```json
"MultiTenancy": { "Enabled": true }
```

### 14.2 Behavior

| `Enabled` | Query filters | TenantContext | Result |
|---|---|---|---|
| `true` | Active | Claims-based | Full SaaS |
| `false` | Off | Always returns 1 | Legacy single-tenant behavior |

Wiring: `Program.cs` reads flag at startup → passes to `AppDbContext` (`multiTenancy` param) → `ITenantContext` returns 1 when flag off (read via injected `IConfiguration`).

### 14.3 Rollback procedure

1. Deploy previous build (or set `MultiTenancy:Enabled=false`).
2. If DB schema migrated: restore DB backup (migrations are additive — `ALTER TABLE ADD` with default values are backward compatible, but the unique index drop is not; restore backup is the safe path).
3. Verify shop login + print smoke test.

---

## 15. Deployment Runbook

### 15.0 Pre-flight (do once, on a staging copy of prod DB)

1. `BACKUP DATABASE db59750 TO DISK = '...'` (or RunASP.NET control panel backup).
2. Restore to staging SQL Server instance; run DDL §3.4; run validation SQL §12.4; all zeros.
3. Run UAT script (below) against staging.

### 15.1 Code deployment

```
set DEPLOY_PASSWORD=<password>
cd PrintingBooksPortal
dotnet publish -c Release -p:PublishProfile=MonsterASP -p:Password=%DEPLOY_PASSWORD%
```

### 15.2 DB migration (maintenance window)

1. Backup prod DB.
2. `dotnet ef migrations script` (or run DDL §3.4 manually via SSMS with `SET XACT_ABORT ON;` inside a transaction).
3. Run backfill §12.3.
4. Run validation §12.4 — **all zeros required**.
5. Deploy code §15.1 (migrations also auto-run on startup; idempotent).
6. Verify roles exist (`SystemAdmin`, `Teacher`).

### 15.3 Smoke test (post-deploy)

| # | Action | Expected |
|---|---|---|
| 1 | Login as legacy admin | Redirect `/sa/dashboard` (or `/admin/dashboard` as Teacher) |
| 2 | Create Teacher via `/sa/teachers` | 201; new user can log in |
| 3 | Teacher uploads book, creates shop+user | Works; files under `App_Data/2/Books/` |
| 4 | Login as tenant's shop user | Only tenant's books listed |
| 5 | Agent with tenant key polls | Only tenant jobs returned; print completes |
| 6 | Second tenant created | Zero visibility of tenant A data |
| 7 | Deactivate tenant | All its users get access-denied on next request |
| 8 | `GET /api/pdf/print-agent/debug` | Queue entries show correct `tenantId` |

### 15.4 Rollback

See §14.3.

---

## 16. Documentation Updates

| File | Update |
|---|---|
| `PrintingBooksPortal/wwwroot/docs/index.html` | Add "SaaS Architecture" section: tenant model, roles (SystemAdmin/Teacher/Shop), per-tenant API keys, agent setup per tenant |
| `README.md` | Roles, SaaS overview, quick start with 2 tenants |
| `PrintingBooksPortal/database&server.md` | New tables DDL, migration steps, backfill |
| `SAAS-PLAN.md` (this file) | Keep as the working spec; mark phases complete as implemented |

---

## 17. Acceptance Criteria

1. `SystemAdmin` can create/edit/deactivate/reset-password for Teachers; create/revoke per-tenant agent API keys.
2. `Teacher` can perform every current Admin action, isolated to their tenant.
3. Two tenants share the server with **zero data leakage** — proven by tests §13.3 and UAT.
4. Shop users see/print only their tenant's assigned books.
5. Agent with tenant key only sees/claims/downloads that tenant's jobs.
6. Watermark contains tenant name; per-tenant watermark toggle/text works.
7. Deactivated tenant: login blocked + existing sessions denied.
8. SystemAdmin global analytics correct; drill-down read-only.
9. Production data migrated losslessly (validation SQL all zeros).
10. Rollback path tested (§14.3).
11. `dotnet test` green; UAT script §15.3 fully passed.
12. Docs updated; deployment runbook executed cleanly.

---

## 18. Risk Register

| Risk | Severity | Mitigation |
|---|---|---|
| Query filter leak | Critical | Fail-closed (`TenantId` 0 → nothing); `.IgnoreQueryFilters()` only in `/sa`; automated isolation tests |
| Backfill corruption | High | Staging validation; backup; validation SQL gate |
| Blazor circuit tenant context stale | Medium | `MainLayout.OnInitializedAsync` initializes per circuit; controllers use claims directly |
| Existing `Admin` role users confused | Medium | Auto-promote to Teacher + tenant 1; keep `Admin` role working during transition |
| RunASP.NET memory limits | Low | Per-tenant filters are cheap; indexed TenantId; monitor |
| Agent version spread | Low | Portal scoping is server-side; old agents keep working; re-key via installer config step |

---

*Spec complete. Implementation order: §12 (migrations) → §3/§4 (models + context + auth) → §7/§8 (endpoints/services) → §9/§10 (UI) → §5 (SA API) → §13 (tests) → §15 (deploy).*
