using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace PrintingBooksPortal.Services;

/// <summary>
/// Resolves the current tenant for DB queries/inserts.
/// Controllers (and prerender) use the HTTP request claims; interactive
/// circuits have no HttpContext, so the AuthenticationStateProvider is used
/// — it is a scoped service kept up to date per circuit.
/// </summary>
public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    private readonly AuthenticationStateProvider _authState;
    private readonly bool _multiTenancyEnabled;
    private ClaimsPrincipal? _circuitUser;
    private readonly object _lock = new();

    public TenantContext(
        IHttpContextAccessor http,
        AuthenticationStateProvider authState,
        IConfiguration configuration)
    {
        _http = http;
        _authState = authState;
        _multiTenancyEnabled = configuration.GetValue<bool?>("MultiTenancy:Enabled") ?? true;

        // Keep the circuit's principal cached — each circuit has its own scoped
        // instance, and the provider raises events whenever the user changes.
        _authState.AuthenticationStateChanged += t =>
        {
            t.ContinueWith(state => { if (state.IsCompletedSuccessfully) SetUser(state.Result.User); },
                TaskScheduler.Default);
        };
    }

    private void SetUser(ClaimsPrincipal? user)
    {
        lock (_lock)
        {
            _circuitUser = user;
        }
    }

    private ClaimsPrincipal? CurrentUser
    {
        get
        {
            if (_http.HttpContext?.User?.Identity?.IsAuthenticated == true)
                return _http.HttpContext.User;

            lock (_lock)
            {
                if (_circuitUser?.Identity?.IsAuthenticated == true)
                    return _circuitUser;
            }

            // First access inside a circuit: pull the state synchronously.
            // Identity's server-side provider returns the cached task quickly.
            try
            {
                var state = _authState.GetAuthenticationStateAsync().GetAwaiter().GetResult();
                if (state?.User?.Identity?.IsAuthenticated == true)
                {
                    SetUser(state.User);
                    return state.User;
                }
            }
            catch
            {
                // no auth state available (design-time, unauthenticated)
            }
            return null;
        }
    }

    /// <summary>Sets the tenant explicitly for the current scope (circuit or request).</summary>
    public void Initialize(ClaimsPrincipal user) => SetUser(user);

    public int TenantId
    {
        get
        {
            if (!_multiTenancyEnabled)
                return 1;                 // feature flag off → legacy single-tenant behavior (§14.2)

            var user = CurrentUser;
            return user == null ? 0 : ParseTenantId(user);
        }
    }

    public bool IsSystemAdmin
    {
        get
        {
            var user = CurrentUser;
            if (user == null)
                return false;

            return user.IsInRole("SystemAdmin");
        }
    }

    private static int ParseTenantId(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true) return 0;
        var claim = user.FindFirstValue("TenantId");
        return int.TryParse(claim, out var id) ? id : 0;
    }
}