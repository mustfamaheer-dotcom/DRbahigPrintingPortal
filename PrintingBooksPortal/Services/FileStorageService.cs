namespace PrintingBooksPortal.Services;

public class FileStorageService
{
    private readonly string _storagePath;
    private readonly string _contentRoot;

    public FileStorageService(IWebHostEnvironment env, ITenantContext tenant)
    {
        _contentRoot = env.ContentRootPath;
        var tenantId = tenant.TenantId;
        // TenantId == 0 → SystemAdmin operation or feature-flag-off: fall back to legacy path (§8.2)
        var relative = tenantId > 0 ? Path.Combine("App_Data", tenantId.ToString(), "Books") : Path.Combine("App_Data", "Books");
        _storagePath = Path.Combine(env.ContentRootPath, relative);
        Directory.CreateDirectory(_storagePath);
    }

    public string StoragePath => _storagePath;

    public async Task<string> SaveFileAsync(IFormFile file)
    {
        // Security: only allow PDF uploads to prevent arbitrary file upload attacks
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext != ".pdf")
            throw new InvalidOperationException("Invalid file type. Only PDFs are allowed.");

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(_storagePath, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return fileName;
    }

    public string GetFilePath(string fileName)
    {
        var path = Path.Combine(_storagePath, fileName);
        if (File.Exists(path))
            return path;

        // Fallback: files uploaded before tenancy was wired (or while tenant context
        // resolved to 0) live in the shared legacy folder App_Data\Books. Serve them
        // transparently instead of failing with FileNotFoundException.
        var legacy = Path.Combine(_contentRoot, "App_Data", "Books", fileName);
        return File.Exists(legacy) ? legacy : Path.Combine(_storagePath, fileName);
    }

    public void DeleteFile(string fileName)
    {
        var path = GetFilePath(fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public long GetFileSize(string fileName)
    {
        var path = GetFilePath(fileName);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }
}