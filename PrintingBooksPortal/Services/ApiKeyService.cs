using System.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public interface IApiKeyService
{
    string GenerateKey(int tenantId);          // "bpk_" + Guid:N; stores SHA-256 hash; returns plaintext once
    Task<int> ResolveTenantAsync(string apiKey);          // 0 if invalid/inactive
    Task<bool> RevokeKeyAsync(int keyId);
    Task<List<TenantApiKey>> ListKeysAsync(int tenantId);
}

public class ApiKeyService : IApiKeyService
{
    private readonly AppDbContext _db;

    public ApiKeyService(AppDbContext db)
    {
        _db = db;
    }

    public string GenerateKey(int tenantId)
    {
        var plain = "bpk_" + Guid.NewGuid().ToString("N");
        _db.TenantApiKeys.Add(new TenantApiKey
        {
            TenantId = tenantId,
            KeyHash = HashKey(plain),
            KeyPrefix = plain[4..12]      // first 8 chars after "bpk_"
        });
        _db.SaveChanges();
        return plain;
    }

    public async Task<int> ResolveTenantAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        var hash = HashKey(key);
        var entry = await _db.TenantApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);
        return entry?.TenantId ?? 0;
    }

    public async Task<bool> RevokeKeyAsync(int keyId)
    {
        var entry = await _db.TenantApiKeys.FindAsync(keyId);
        if (entry == null) return false;
        entry.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<TenantApiKey>> ListKeysAsync(int tenantId)
    {
        return await _db.TenantApiKeys
            .AsNoTracking()
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    private static string HashKey(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}