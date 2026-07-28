using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

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
    private bool _initialized;
    private int? _teacherId;
    private bool _isAdmin;
    private string? _userId;

    public TenantContext(AuthenticationStateProvider authState)
    {
        _authState = authState;
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
                InitializeFromPrincipal(state.User);
        }
        catch
        {
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
            }
        }
        catch
        {
        }
    }
}