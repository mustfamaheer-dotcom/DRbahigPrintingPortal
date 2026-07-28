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

    public int? TeacherId
    {
        get
        {
            if (!_initialized) Initialize();
            return _teacherId;
        }
    }

    public bool IsAdmin
    {
        get
        {
            if (!_initialized) Initialize();
            return _isAdmin;
        }
    }

    public string? UserId
    {
        get
        {
            if (!_initialized) Initialize();
            return _userId;
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            var state = await _authState.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated == true)
            {
                InitializeFromPrincipal(state.User);
                if (_teacherId == null)
                    await TryResolveTeacherIdFromDb(state.User);
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

    public void InitializeFromPrincipal(ClaimsPrincipal user)
    {
        _initialized = true;
        var tidClaim = user.FindFirst("TeacherId");
        if (tidClaim != null && int.TryParse(tidClaim.Value, out var tid))
            _teacherId = tid;
        _isAdmin = user.IsInRole("Admin");
        _userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (_teacherId == null)
            _logger.LogWarning("InitializeFromPrincipal: TeacherId claim not found or not parseable for user {UserId}", _userId);
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback DB lookup for TeacherId failed for user {UserId}", _userId);
        }
    }

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var task = _authState.GetAuthenticationStateAsync();
            if (task.IsCompleted)
            {
                var user = task.Result.User;
                if (user.Identity?.IsAuthenticated == true)
                    InitializeFromPrincipal(user);
                else
                    _logger.LogWarning("Initialize (sync fallback): user is not authenticated");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize (sync fallback) failed to resolve authentication state");
        }
    }
}
