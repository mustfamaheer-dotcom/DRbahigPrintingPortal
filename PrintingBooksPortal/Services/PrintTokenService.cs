using System.Collections.Concurrent;

namespace PrintingBooksPortal.Services;

public class PrintTokenService
{
    private readonly ConcurrentDictionary<string, (int BookId, int TenantId, string UserId, string ShopName, string UserName, DateTime Expires)> _tokens = new();
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    public string GenerateToken(int bookId, int tenantId, string userId, string shopName, string userName)
    {
        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = (bookId, tenantId, userId, shopName, userName, DateTime.UtcNow.Add(TokenLifetime));
        return token;
    }

    public bool ValidateToken(string token, out int bookId, out int tenantId, out string userId, out string shopName, out string userName)
    {
        if (_tokens.TryRemove(token, out var entry) && entry.Expires > DateTime.UtcNow)
        {
            bookId = entry.BookId;
            tenantId = entry.TenantId;
            userId = entry.UserId;
            shopName = entry.ShopName;
            userName = entry.UserName;
            return true;
        }
        bookId = 0;
        tenantId = 0;
        userId = null!;
        shopName = null!;
        userName = null!;
        return false;
    }
}