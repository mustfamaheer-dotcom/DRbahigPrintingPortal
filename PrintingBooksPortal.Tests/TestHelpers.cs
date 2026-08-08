using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PrintingBooksPortal.Tests;

/// <summary>Shared helpers for hitting the in-process app.</summary>
public static class TestHelpers
{
    public static HttpClient CreateClient(TestAppFactory factory, bool allowRedirects = false)
    {
        var options = new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowRedirects,
            HandleCookies = true
        };
        return factory.CreateClient(options);
    }

    /// <summary>Post the login form. Returns the redirect Location when not following redirects.</summary>
    public static async Task<string?> LoginAsync(HttpClient client, string email, string password)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("email", email),
            new KeyValuePair<string, string>("password", password),
            new KeyValuePair<string, string>("rememberMe", "false")
        });
        var response = await client.PostAsync("/api/login", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location?.ToString();
    }

    public static async Task<HttpResponseMessage> PostAgentAsync(
        HttpClient client, string route, string? apiKey, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route);
        if (apiKey != null) request.Headers.Add("X-Api-Key", apiKey);
        request.Content = body == null
            ? null
            : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> GetWithKeyAsync(HttpClient client, string route, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (apiKey != null) request.Headers.Add("X-Api-Key", apiKey);
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }
}