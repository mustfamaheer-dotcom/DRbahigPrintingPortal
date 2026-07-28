using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using BookShopPrintAgent.Controllers;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;
using Path = System.IO.Path;

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

        var printReadyPdf = ApplyPdfSettings(decryptedBytes, settings);

        // Debug: save pre-processed PDF to logs folder for inspection
        try
        {
            var debugDir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(debugDir);
            var debugFile = Path.Combine(debugDir, $"pre_{jobId}_{DateTime.Now:yyyyMMddHHmmss}.pdf");
            File.WriteAllBytes(debugFile, printReadyPdf);
            _logger.LogInformation("Saved pre-processed PDF to {File} ({Size} bytes)", debugFile, printReadyPdf.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save debug PDF");
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"print_{jobId}_{DateTime.Now.Ticks}.pdf");
        await File.WriteAllBytesAsync(tempFile, printReadyPdf);

        try
        {
            var sumatraExe = FindPdfReader();
            if (string.IsNullOrEmpty(sumatraExe))
                throw new InvalidOperationException("SumatraPDF not found. Please run a fresh install of BookShopAgent.");

            _logger.LogInformation("Using SumatraPDF: {Exe}", sumatraExe);

            // PDF is already pre-sized with correct scale/margins/orientation — use noscale
            var printSettingsParts = new List<string> { "noscale" };
            if (settings.Duplex == "long")
                printSettingsParts.Add("duplexvertical");
            else if (settings.Duplex == "short")
                printSettingsParts.Add("duplexhorizontal");

            var combinedPrintSettings = string.Join(",", printSettingsParts);

            for (int i = 0; i < settings.Copies; i++)
            {
                _logger.LogInformation("Printing copy {Copy}/{Copies} for job {JobId}, printer={Printer}, duplex={Duplex}, margins=({Ml},{Mr},{Mt},{Mb}), scale={Scale}%",
                    i + 1, settings.Copies, jobId, settings.PrinterName, settings.Duplex,
                    settings.MarginLeft, settings.MarginRight, settings.MarginTop, settings.MarginBottom,
                    settings.ScalingMode == "custom" ? settings.CustomScale?.ToString() ?? "100" : settings.ScalingMode);

                var printArg = string.IsNullOrWhiteSpace(settings.PrinterName)
                    ? $"-print-to-default -print-settings \"{combinedPrintSettings}\" -silent \"{tempFile}\""
                    : $"-print-to \"{settings.PrinterName}\" -print-settings \"{combinedPrintSettings}\" -silent \"{tempFile}\"";

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

    private byte[] ApplyPdfSettings(byte[] pdfBytes, PrintSettings settings)
    {
        var targetSize = GetPageSize(settings.PaperSize);

        double mt = ToPoints(settings.MarginTop, settings.MarginUnit);
        double mb = ToPoints(settings.MarginBottom, settings.MarginUnit);
        double ml = ToPoints(settings.MarginLeft, settings.MarginUnit);
        double mr = ToPoints(settings.MarginRight, settings.MarginUnit);

        using var srcStream = new MemoryStream(pdfBytes);
        using var srcDoc = new PdfDocument(new PdfReader(srcStream));

        using var destStream = new MemoryStream();
        using var destDoc = new PdfDocument(new PdfWriter(destStream));

        for (int i = 1; i <= srcDoc.GetNumberOfPages(); i++)
        {
            var srcPage = srcDoc.GetPage(i);
            var origSize = srcPage.GetPageSize();

            var availW = targetSize.GetWidth() - (float)(ml + mr);
            var availH = targetSize.GetHeight() - (float)(mt + mb);

            float scale;
            if (settings.ScalingMode == "custom" && settings.CustomScale.HasValue)
            {
                scale = settings.CustomScale.Value / 100f;
            }
            else if (settings.ScalingMode == "fit")
            {
                scale = Math.Min(availW / origSize.GetWidth(), availH / origSize.GetHeight());
            }
            else if (settings.ScalingMode == "shrink")
            {
                scale = Math.Min(1f, Math.Min(availW / origSize.GetWidth(), availH / origSize.GetHeight()));
            }
            else
            {
                scale = 1f;
            }

            var contentW = origSize.GetWidth() * scale;
            var contentH = origSize.GetHeight() * scale;
            var ox = (float)ml + (availW - contentW) / 2f;
            var oy = (float)mb + (availH - contentH) / 2f;

            var formXObj = srcPage.CopyAsFormXObject(destDoc);

            var destPage = destDoc.AddNewPage(targetSize);
            var canvas = new PdfCanvas(destPage);
            canvas.SaveState()
                  .ConcatMatrix(scale, 0, 0, scale, ox, oy)
                  .AddXObject(formXObj)
                  .RestoreState();
        }

        srcDoc.Close();
        destDoc.Close();

        return destStream.ToArray();
    }

    private static float ToPoints(double value, string unit)
    {
        return unit?.ToLower() switch
        {
            "inch" or "in" => (float)(value * 72),
            "cm" => (float)(value * 72 / 2.54),
            _ => (float)(value * 72 / 25.4)
        };
    }

    private static PageSize GetPageSize(string? paperSize)
    {
        return paperSize?.ToLower() switch
        {
            "letter" => PageSize.LETTER,
            "legal" => PageSize.LEGAL,
            "a3" => PageSize.A3,
            "a5" => PageSize.A5,
            "jisb4" => new PageSize(728.5f, 1031.8f),
            "jisb5" => new PageSize(516.1f, 728.5f),
            "executive" => PageSize.EXECUTIVE,
            "tabloid" or "ledger" => PageSize.TABLOID,
            _ => PageSize.A4
        };
    }

    private static string? FindPdfReader()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null)
        {
            var exe = Path.Combine(exeDir, "SumatraPDF-3.6.1-64.exe");
            if (File.Exists(exe)) return exe;
        }
        var fallback = Path.Combine(AppContext.BaseDirectory, "SumatraPDF-3.6.1-64.exe");
        if (File.Exists(fallback)) return fallback;
        return null;
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
