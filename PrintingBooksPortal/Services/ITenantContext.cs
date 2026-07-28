using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;

namespace PrintingBooksPortal.Services;

public interface ITenantContext
{
    int? TeacherId { get; }
    bool IsAdmin { get; }
    string? UserId { get; }
    bool IsInitialized { get; }
    Task InitializeAsync();
}

public class TenantContext : ITenantContext
{
    private readonly AuthenticationStateProvider _authState;
    private readonly ILogger<TenantContext> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _initialized;
    private int? _teacherId;
    private bool _isAdmin;
    private string? _userId;

    public TenantContext(AuthenticationStateProvider authState, ILogger<TenantContext> logger, IServiceScopeFactory scopeFactory)
    {
        _authState = authState;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public int? TeacherId => _teacherId;

    public bool IsAdmin => _isAdmin;

    public string? UserId => _userId;

    public bool IsInitialized => _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            var state = await _authState.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated == true)
            {
                var user = state.User;
                var tidClaim = user.FindFirst("TeacherId");
                if (tidClaim != null && int.TryParse(tidClaim.Value, out var tid))
                    _teacherId = tid;
                _isAdmin = user.IsInRole("Admin");
                _userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (_teacherId == null)
                    _logger.LogWarning("InitializeAsync: TeacherId claim missing for user {UserId}. Attempting DB fallback.", _userId);
                else
                    _logger.LogInformation("InitializeAsync: resolved TeacherId {TeacherId} from claims for user {UserId}", _teacherId, _userId);

                if (_teacherId == null)
                    await TryResolveTeacherIdFromDb(user);

                if (_teacherId == null)
                    _logger.LogWarning("InitializeAsync: TeacherId could not be resolved from claims or DB for user {UserId}", _userId);
            }
            else
            {
                _logger.LogWarning("InitializeAsync: user is not authenticated");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitializeAsync failed to resolve authentication state");
        }
        _initialized = true;
    }

    private async Task TryResolveTeacherIdFromDb(ClaimsPrincipal user)
    {
        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appUser = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (appUser?.TeacherId != null)
            {
                _teacherId = appUser.TeacherId.Value;
                _logger.LogInformation("Fallback: resolved TeacherId {TeacherId} from DB for user {UserId}", _teacherId, userId);
            }
            else
            {
                _logger.LogWarning("Fallback: TeacherId is null in DB for user {UserId}", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback DB lookup for TeacherId failed");
        }
    }
}
