using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using BookShopPrintAgent.Controllers;
using iText.Kernel.Pdf;

namespace BookShopPrintAgent.Services;

[SupportedOSPlatform("windows")]

public class PdfPrintService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<PdfPrintService> _logger;

    public PdfPrintService(HttpClient httpClient, IConfiguration config, ILogger<PdfPrintService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task DownloadAndPrintAsync(string jobId, PrintSettings settings)
    {
        var baseUrl = _config.GetValue<string>("ServerSettings:BaseUrl")
            ?? throw new InvalidOperationException("ServerSettings:BaseUrl not configured.");
        var ownerPassword = _config.GetValue<string>("ServerSettings:OwnerPassword")
            ?? throw new InvalidOperationException("ServerSettings:OwnerPassword not configured.");

        var downloadUrl = $"{baseUrl.TrimEnd('/')}/api/pdf/download-secured/{jobId}";
        _logger.LogInformation("Downloading encrypted PDF from {Url}", downloadUrl);

        var apiKey = _config.GetValue<string>("ServerSettings:ApiKey");
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("X-Api-Key", apiKey);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var encryptedBytes = await response.Content.ReadAsByteArrayAsync();
        _logger.LogInformation("Downloaded {Bytes} bytes for job {JobId}", encryptedBytes.Length, jobId);

        var decryptedBytes = DecryptPdf(encryptedBytes, ownerPassword);

        var tempFile = Path.Combine(Path.GetTempPath(), $"print_{jobId}_{DateTime.Now.Ticks}.pdf");
        await File.WriteAllBytesAsync(tempFile, decryptedBytes);

        try
        {
            var sumatraExe = FindPdfReader();
            if (string.IsNullOrEmpty(sumatraExe))
                throw new InvalidOperationException("SumatraPDF not found. Please run a fresh install of BookShopAgent.");

            _logger.LogInformation("Using SumatraPDF: {Exe}", sumatraExe);

            // Build print-settings argument
            // SumatraPDF print-settings: noscale, shrink, fit
            var scalingArg = settings.ScalingMode switch
            {
                "fit" => "fit",
                "shrink" or "custom" => "shrink",
                _ => "noscale"
            };

            for (int i = 0; i < settings.Copies; i++)
            {
                _logger.LogInformation("Printing copy {Copy}/{Copies} for job {JobId}, printer={Printer}, scaling={Scaling}",
                    i + 1, settings.Copies, jobId, settings.PrinterName, scalingArg);

                var printArg = string.IsNullOrWhiteSpace(settings.PrinterName)
                    ? $"-print-to-default -print-settings \"{scalingArg}\" -silent \"{tempFile}\""
                    : $"-print-to \"{settings.PrinterName}\" -print-settings \"{scalingArg}\" -silent \"{tempFile}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = sumatraExe,
                    Arguments = printArg,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                _logger.LogInformation("Process start: {Exe} {Args}", sumatraExe, printArg);

                using var process = Process.Start(psi);
                if (process != null)
                {
                    if (process.WaitForExit(60000))
                        _logger.LogInformation("SumatraPDF printed successfully (exit code {Code})", process.ExitCode);
                    else
                        _logger.LogWarning("SumatraPDF did not exit within 60 seconds for job {JobId}", jobId);
                }
            }
        }
        finally
        {
            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                try { File.Delete(tempFile); } catch { }
            });
        }
    }

    private static string? FindPdfReader()
    {
        var appDir = AppContext.BaseDirectory;
        var exe = Path.Combine(appDir, "SumatraPDF-3.6.1-64.exe");
        return File.Exists(exe) ? exe : null;
    }

    private byte[] DecryptPdf(byte[] encryptedBytes, string ownerPassword)
    {
        using var inputStream = new MemoryStream(encryptedBytes);
        using var outputStream = new MemoryStream();

        var readerProperties = new ReaderProperties()
            .SetPassword(Encoding.UTF8.GetBytes(ownerPassword));

        var reader = new PdfReader(inputStream, readerProperties);
        var writer = new PdfWriter(outputStream);

        using var pdfDoc = new PdfDocument(reader, writer);
        pdfDoc.Close();

        var decrypted = outputStream.ToArray();
        _logger.LogInformation("Decrypted PDF: {Bytes} bytes", decrypted.Length);
        return decrypted;
    }
}
