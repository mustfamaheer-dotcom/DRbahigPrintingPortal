using Microsoft.AspNetCore.Http;

namespace PrintingBooksPortal.Services;

/// <summary>
/// Blazor Server circuits execute HttpClient calls on the server, where no
/// browser cookie is sent. This handler forwards the current HTTP context's
/// auth cookie (and antiforgery header, if present) so controller endpoints
/// called from components see the signed-in user.
/// </summary>
public class ServerAuthenticationMessageHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ServerAuthenticationMessageHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            if (httpContext.Request.Headers.TryGetValue("Cookie", out var cookie) && request.Headers.Contains("Cookie") == false)
                request.Headers.Add("Cookie", cookie.ToString());

            if (httpContext.Request.Headers.TryGetValue("RequestVerificationToken", out var token))
                request.Headers.TryAddWithoutValidation("RequestVerificationToken", token.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
