using System.Drawing.Printing;
using System.Management;
using System.Runtime.Versioning;
using BookShopPrintAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookShopPrintAgent.Controllers;

[ApiController]
[Route("api/print-job")]
[SupportedOSPlatform("windows")]
public class PrintJobController : ControllerBase
{
    private readonly PdfPrintService _printService;
    private readonly IConfiguration _config;
    private readonly ILogger<PrintJobController> _logger;

    public PrintJobController(PdfPrintService printService, IConfiguration config, ILogger<PrintJobController> logger)
    {
        _printService = printService;
        _config = config;
        _logger = logger;
    }

    // Known virtual/software printers to exclude — they cannot produce physical paper output
    private static readonly string[] VirtualPrinterPatterns = [
        "ONENOTE", "XPS", "FAX", "ANYDESK", "PDF", "ADOBE PDF", "CUTEPDF",
        "BULLZIP", "DOPDF", "PRINTR", "FINE PRINT", "UNIVERSAL PRINT"
    ];

    private static bool IsPhysicalPrinter(string name, string port, bool isNetwork, string driver)
    {
        var upperName = name.ToUpperInvariant();
        var upperDriver = driver.ToUpperInvariant();

        foreach (var pattern in VirtualPrinterPatterns)
            if (upperName.Contains(pattern) || upperDriver.Contains(pattern))
                return false;

        // Port-based filtering
        var upperPort = port.ToUpperInvariant();
        if (upperPort == "NUL:" || upperPort == "PORTPROMPT:" || upperPort.StartsWith("SHRFAX") ||
            upperPort.Contains("ONENOTE") || upperPort.Contains("AD_"))
            return false;

        // Must be a physical connection type
        var connType = isNetwork ? "Network" :
            port.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? "USB" :
            port.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ? "Bluetooth" :
            port.StartsWith("WS", StringComparison.OrdinalIgnoreCase) ? "WiFi" :
            port.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) ? "Local" :
            port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ? "Local" : null;

        return connType != null;
    }

    [HttpGet("printers")]
    public IActionResult GetPrinters()
    {
        var printers = new List<object>();
        string defaultName = new PrinterSettings().PrinterName;
        bool hasPhysicalDefault = false; // reserved for future use

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                var port = mo["PortName"]?.ToString() ?? "";
                var isNetwork = (bool)(mo["Network"] ?? false);
                var driver = mo["DriverName"]?.ToString() ?? "";

                if (!IsPhysicalPrinter(name, port, isNetwork, driver))
                    continue;

                var isDefault = (bool)(mo["Default"] ?? false);
                if (isDefault) hasPhysicalDefault = true;
                var location = mo["Location"]?.ToString() ?? "";
                var comment = mo["Comment"]?.ToString() ?? "";
                var rawStatus = mo["PrinterStatus"] != null ? (ushort)mo["PrinterStatus"] : 0;

                printers.Add(new
                {
                    name,
                    port,
                    status = rawStatus switch
                    {
                        1 => "Paused",
                        2 => "Error",
                        3 => "Idle",
                        4 => "Printing",
                        5 => "Offline",
                        _ => "Unknown"
                    },
                    isOnline = rawStatus == 3 || rawStatus == 4,
                    isDefault,
                    connectionType = isNetwork ? "Network" : port.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? "USB" : port.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ? "Bluetooth" : port.StartsWith("WS", StringComparison.OrdinalIgnoreCase) ? "WiFi" : "Local",
                    driver,
                    location,
                    comment
                });
            }
        }
        catch
        {
            foreach (string name in PrinterSettings.InstalledPrinters)
            {
                if (!IsPhysicalPrinter(name, "", false, ""))
                    continue;
                printers.Add(new
                {
                    name,
                    port = "",
                    status = "Unknown",
                    isOnline = true,
                    isDefault = string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase),
                    connectionType = "Local",
                    driver = "",
                    location = "",
                    comment = ""
                });
            }
        }

        return Ok(new { printers, defaultPrinter = defaultName });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitPrintJob([FromBody] PrintJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            return BadRequest(new { success = false, error = "JobId is required." });

        _logger.LogInformation("Received print job: {JobId}, Copies: {Copies}, Printer: {Printer}, Scaling: {Scaling}, Duplex: {Duplex}, PaperSize: {PaperSize}",
            request.JobId, request.Copies, request.PrinterName, request.ScalingMode, request.Duplex, request.PaperSize);

        try
        {
            var copies = Math.Max(1, request.Copies);
            var printerName = !string.IsNullOrWhiteSpace(request.PrinterName)
                ? request.PrinterName
                : (_config.GetValue<string>("PrinterSettings:DefaultPrinterName") ?? "");

            // Validate the printer is a physical printer (not virtual/software)
            if (!string.IsNullOrWhiteSpace(printerName))
            {
                bool isValidPhysical = false;
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer WHERE Name='" + printerName.Replace("'", "''") + "'");
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var port = mo["PortName"]?.ToString() ?? "";
                        var isNetwork = (bool)(mo["Network"] ?? false);
                        var driver = mo["DriverName"]?.ToString() ?? "";
                        isValidPhysical = IsPhysicalPrinter(printerName, port, isNetwork, driver);
                    }
                }
                catch { isValidPhysical = true; } // If WMI fails, allow it
                if (!isValidPhysical)
                    return BadRequest(new { success = false, error = "Selected printer is not a physical printer. Only real printers (USB, Network, WiFi, Bluetooth) are allowed." });
            }

            var printSettings = new PrintSettings
            {
                PrinterName = printerName,
                Copies = copies,
                ScalingMode = request.ScalingMode ?? "actual",
                CustomScale = request.CustomScale,
                Duplex = request.Duplex ?? "off",
                PaperSize = request.PaperSize ?? "A4",
                MarginUnit = request.MarginUnit ?? "mm",
                MarginTop = request.MarginTop ?? 0,
                MarginBottom = request.MarginBottom ?? 0,
                MarginLeft = request.MarginLeft ?? 0,
                MarginRight = request.MarginRight ?? 0
            };

            await _printService.DownloadAndPrintAsync(request.JobId, printSettings);

            _logger.LogInformation("Print job completed: {JobId}", request.JobId);

            return Ok(new
            {
                success = true,
                jobId = request.JobId,
                message = $"Print job {request.JobId} sent to printer ({copies} copy(ies))."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Print job failed: {JobId}", request.JobId);
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "running", timestamp = DateTime.UtcNow });
    }
}

public class PrintJobRequest
{
    public string JobId { get; set; } = "";
    public int Copies { get; set; } = 1;
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Duplex { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}

public class PrintSettings
{
    public string PrinterName { get; set; } = "";
    public int Copies { get; set; } = 1;
    public string ScalingMode { get; set; } = "actual";
    public int? CustomScale { get; set; }
    public string Duplex { get; set; } = "off";
    public string PaperSize { get; set; } = "A4";
    public string MarginUnit { get; set; } = "mm";
    public double MarginTop { get; set; }
    public double MarginBottom { get; set; }
    public double MarginLeft { get; set; }
    public double MarginRight { get; set; }
}
