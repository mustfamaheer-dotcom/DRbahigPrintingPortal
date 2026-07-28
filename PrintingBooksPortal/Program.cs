using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using PrintingBooksPortal.Components;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Hubs;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var isProduction = builder.Environment.IsProduction();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (isProduction)
        options.UseSqlServer(connectionString);
    else
        options.UseSqlite(connectionString);
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.IsEssential = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("TeacherOnly", policy => policy.RequireRole("Teacher"));
});

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<PrintLoggingService>();
builder.Services.AddScoped<IWatermarkService, WatermarkService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddSingleton<PrintTokenService>();
builder.Services.AddSingleton<IPdfSecurityService, PdfSecurityService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMudServices();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.Configuration["AppUrl"] ?? "http://localhost:5035") });
builder.Services.AddHostedService<StaleJobMonitor>();

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedFor
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapControllers();
app.MapHub<PrintHub>("/hubs/print");

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (isProduction)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                -- AspNetUsers: add multi-tenant columns if missing
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AspNetUsers' AND COLUMN_NAME='Role')
                    ALTER TABLE [AspNetUsers] ADD [Role] int NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AspNetUsers' AND COLUMN_NAME='TeacherId')
                    ALTER TABLE [AspNetUsers] ADD [TeacherId] int NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AspNetUsers' AND COLUMN_NAME='FullName')
                    ALTER TABLE [AspNetUsers] ADD [FullName] nvarchar(max) NULL;

                -- Bookshops table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Bookshops')
                    CREATE TABLE [Bookshops] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [Name] nvarchar(200) NOT NULL,
                        [ContactPerson] nvarchar(300) NULL,
                        [Phone] nvarchar(50) NULL,
                        [Address] nvarchar(500) NULL,
                        [IsActive] bit NOT NULL DEFAULT 1,
                        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT [PK_Bookshops] PRIMARY KEY ([Id])
                    );

                -- Teachers table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Teachers')
                    CREATE TABLE [Teachers] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [UserId] nvarchar(450) NOT NULL,
                        [Name] nvarchar(max) NOT NULL,
                        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT [PK_Teachers] PRIMARY KEY ([Id])
                    );

                -- TeacherBookshopLinks table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TeacherBookshopLinks')
                    CREATE TABLE [TeacherBookshopLinks] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [TeacherId] int NOT NULL,
                        [BookshopId] int NOT NULL,
                        [UniqueApiKey] nvarchar(128) NOT NULL,
                        [CopiesPrinted] int NOT NULL DEFAULT 0,
                        [LastResetDate] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        [IsActive] bit NOT NULL DEFAULT 1,
                        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT [PK_TeacherBookshopLinks] PRIMARY KEY ([Id])
                    );

                -- Invoices table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Invoices')
                    CREATE TABLE [Invoices] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [TeacherBookshopLinkId] int NOT NULL,
                        [PeriodStart] datetime2 NOT NULL,
                        [PeriodEnd] datetime2 NOT NULL,
                        [TotalCopies] int NOT NULL DEFAULT 0,
                        [Currency] nvarchar(50) NOT NULL DEFAULT 'EGP',
                        [TotalAmount] decimal(18,2) NOT NULL DEFAULT 0,
                        [Status] int NOT NULL DEFAULT 0,
                        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        [PaidAt] datetime2 NULL,
                        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id])
                    );

                -- Books: add TeacherId column if missing
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Books' AND COLUMN_NAME='TeacherId')
                    ALTER TABLE [Books] ADD [TeacherId] int NULL;

                -- EducationalBoards: add TeacherId column if missing
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EducationalBoards' AND COLUMN_NAME='TeacherId')
                    ALTER TABLE [EducationalBoards] ADD [TeacherId] int NULL;

                -- PrintLogs: add TeacherId and TeacherBookshopLinkId columns if missing
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='PrintLogs' AND COLUMN_NAME='TeacherId')
                    ALTER TABLE [PrintLogs] ADD [TeacherId] int NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='PrintLogs' AND COLUMN_NAME='TeacherBookshopLinkId')
                    ALTER TABLE [PrintLogs] ADD [TeacherBookshopLinkId] int NULL;

                -- Indexes for Bookshops
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Bookshops_Name' AND object_id=OBJECT_ID('Bookshops'))
                    PRINT 'IX_Bookshops_Name already exists'
                ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Bookshops')
                    CREATE INDEX [IX_Bookshops_Name] ON [Bookshops] ([Name]);

                -- Indexes for Teachers
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Teachers_UserId' AND object_id=OBJECT_ID('Teachers'))
                    PRINT 'IX_Teachers_UserId already exists'
                ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Teachers')
                    CREATE UNIQUE INDEX [IX_Teachers_UserId] ON [Teachers] ([UserId]);
            ");
            logger.LogInformation("Production schema sync completed.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Production schema sync could not be fully applied.");
        }

        try
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply pending migrations.");
        }
    }
    else
    {
        try
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply pending migrations.");
        }
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await DbSeeder.SeedAsync(db, userManager, roleManager, logger);
    logger.LogInformation("Database initialization completed.");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Database initialization failed. The app will still start.");
}

app.Run();
