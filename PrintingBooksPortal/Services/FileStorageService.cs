namespace PrintingBooksPortal.Services;

public class FileStorageService
{
    private readonly string _storagePath;

    public FileStorageService(IWebHostEnvironment env)
    {
        _storagePath = Path.Combine(env.ContentRootPath, "App_Data", "Books");
        Directory.CreateDirectory(_storagePath);
    }

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
        return Path.Combine(_storagePath, fileName);
    }

    public void DeleteFile(string fileName)
    {
        var path = Path.Combine(_storagePath, fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public long GetFileSize(string fileName)
    {
        var path = Path.Combine(_storagePath, fileName);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }
}
