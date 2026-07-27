using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;     // Security: HTTPS-only cookies
    options.Cookie.SameSite = SameSiteMode.Strict;                // Security: prevent CSRF
    options.Cookie.IsEssential = true;                            // Security: GDPR-compliant auth
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("ShopOnly", policy => policy.RequireRole("Shop"));
});

builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<PrintLoggingService>();
builder.Services.AddScoped<IWatermarkService, WatermarkService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddSingleton<PrintTokenService>();
builder.Services.AddSingleton<IPdfSecurityService, PdfSecurityService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.Configuration["AppUrl"] ?? "http://localhost:5035") });

var app = builder.Build();

// Security: trust X-Forwarded-Proto/X-Forwarded-Host from the reverse proxy (RunASP.NET load balancer)
// so that UseHttpsRedirection() and CookieSecurePolicy.Always work correctly behind SSL termination.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedFor
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();   // Security: HTTP Strict-Transport-Security header
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();  // Security: redirect HTTP → HTTPS
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
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
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply migrations automatically. The SettingsService will auto-create the SystemSettings table on first access.");
        }
    }
    else
    {
        try
        {
            await db.Database.MigrateAsync();
        }
        catch
        {
            await db.Database.EnsureCreatedAsync();
        }
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await DbSeeder.SeedAsync(db, userManager, roleManager);
    logger.LogInformation("Database initialization completed.");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Database initialization failed. The app will still start.");
}

app.Run();
