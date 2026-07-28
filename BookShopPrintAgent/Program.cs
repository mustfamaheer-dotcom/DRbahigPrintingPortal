using BookShopPrintAgent.Controllers;
using BookShopPrintAgent.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

// Set up file logging (use process path for single-file publish)
var agentDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
var logDir = Path.Combine(agentDir, "logs");
Directory.CreateDirectory(logDir);
var logFile = Path.Combine(logDir, $"agent_{DateTime.Now:yyyyMMdd}.log");
void Log(string msg)
{
    var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
    Console.WriteLine(line);
    try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
}

// Single-instance guard
using var mutex = new Mutex(true, "BookShopPrintAgent", out var isFirst);
if (!isFirst)
{
    Console.WriteLine("[BookShopPrintAgent] Already running. Exiting.");
    return;
}

// Force-kill any process holding port 8080 (even SYSTEM-level from scheduled task)
try
{
    var pid = Environment.ProcessId;
    var psi = new ProcessStartInfo
    {
        FileName = "powershell",
        Arguments = $"-NoProfile -Command \"$p=Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; if ($p -and $p -ne {pid}) {{ Stop-Process -Id $p -Force; Write-Host ('Freed port 8080 from PID '+$p) }}\"",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var p = Process.Start(psi);
    var outText = p?.StandardOutput.ReadToEnd();
    p?.WaitForExit(5000);
    if (!string.IsNullOrWhiteSpace(outText))
        Console.WriteLine("[BookShopPrintAgent] " + outText.Trim());
}
catch (Exception ex)
{
    Console.WriteLine($"[BookShopPrintAgent] Port cleanup: {ex.Message}");
}
Thread.Sleep(1000);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, 8080);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<PdfPrintService>();
builder.Services.AddControllers();

var app = builder.Build();

app.UseCors();
app.MapControllers();

var baseUrl = app.Configuration.GetValue<string>("ServerSettings:BaseUrl") ?? "https://drbaheegbook.runasp.net";
var apiKey = app.Configuration.GetValue<string>("ServerSettings:ApiKey") ?? "";
var defaultPrinter = app.Configuration.GetValue<string>("PrinterSettings:DefaultPrinterName") ?? "";
var useSignalR = app.Configuration.GetValue<bool>("ServerSettings:UseSignalR", true);

Console.WriteLine($"[BookShopPrintAgent] Listening on http://localhost:8080");
Console.WriteLine($"[BookShopPrintAgent] Server: {baseUrl}");

// ─── Startup Validation ───
if (string.IsNullOrEmpty(apiKey))
    Log("WARNING: ApiKey is missing in appsettings.json. Claiming jobs will fail.");
if (string.IsNullOrEmpty(app.Configuration.GetValue<string>("ServerSettings:OwnerPassword")))
    Log("WARNING: OwnerPassword is missing in appsettings.json. PDF decryption will fail.");

if (useSignalR)
{
    _ = RunSignalRClient(baseUrl, apiKey, defaultPrinter, app.Services);
}
else
{
    Console.WriteLine($"[BookShopPrintAgent] Polling for jobs every 3 seconds...");
    _ = RunPollingFallback(baseUrl, apiKey, defaultPrinter, app.Services);
}

app.Run();

// ═══════════════════════════════════════════════════════════
//  SIGNALR CLIENT
// ═══════════════════════════════════════════════════════════
async Task RunSignalRClient(string baseUrl, string apiKey, string defaultPrinter, IServiceProvider services)
{
    Log("Starting SignalR client...");

    var hubConnection = new HubConnectionBuilder()
        .WithUrl($"{baseUrl.TrimEnd('/')}/hubs/print", options =>
        {
            if (!string.IsNullOrEmpty(apiKey))
                options.Headers.Add("X-Api-Key", apiKey);
            options.HttpMessageHandlerFactory = _ => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
        })
        .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) })
        .Build();

    hubConnection.On<PrintJobRequest>("NewPrintJob", async (request) =>
    {
        Log($"Received job via SignalR: {request.JobId}");
        try
        {
            var printService = services.GetRequiredService<PdfPrintService>();

            var copies = Math.Max(1, request.Copies);
            var jobPrinter = request.PrinterName;
            var effectivePrinter = !string.IsNullOrWhiteSpace(jobPrinter) ? jobPrinter : defaultPrinter;

            var settings = new PrintSettings
            {
                PrinterName = effectivePrinter,
                Copies = copies,
                ScalingMode = request.ScalingMode ?? "actual",
                CustomScale = request.CustomScale,
                Orientation = request.Orientation ?? "portrait",
                PaperSize = request.PaperSize ?? "A4",
                MarginUnit = request.MarginUnit ?? "mm",
                MarginTop = request.MarginTop ?? 0,
                MarginBottom = request.MarginBottom ?? 0,
                MarginLeft = request.MarginLeft ?? 0,
                MarginRight = request.MarginRight ?? 0
            };

            Log($"Processing job {request.JobId}, {copies} copy(ies), printer: {settings.PrinterName}, scaling: {settings.ScalingMode}");

            await hubConnection.InvokeAsync("UpdateJobStatus", request.JobId, "processing", "Agent is processing the print job.");

            await printService.DownloadAndPrintAsync(request.JobId, settings);

            Log($"Job {request.JobId} completed successfully");
            await hubConnection.InvokeAsync("UpdateJobStatus", request.JobId, "completed", "Print job completed successfully.");
        }
        catch (Exception ex)
        {
            Log($"Job {request.JobId} FAILED: {ex.Message}");
            try
            {
                await hubConnection.InvokeAsync("UpdateJobStatus", request.JobId, "failed", $"Print failed: {ex.Message}");
            }
            catch { }
        }
    });

    hubConnection.Closed += async (error) =>
    {
        Log($"SignalR connection closed: {error?.Message ?? "unknown reason"}");
        Log("Falling back to polling...");

        await RunPollingFallback(baseUrl, apiKey, defaultPrinter, services);
    };

    hubConnection.Reconnecting += (error) =>
    {
        Log($"SignalR reconnecting: {error?.Message ?? ""}");
        return Task.CompletedTask;
    };

    hubConnection.Reconnected += async (connectionId) =>
    {
        Log($"SignalR reconnected: {connectionId}");
        try
        {
            await hubConnection.InvokeAsync("RegisterAsAgent");
        }
        catch { }
    };

    try
    {
        await hubConnection.StartAsync();
        Log("Connected to PrintHub via SignalR.");

        await hubConnection.InvokeAsync("RegisterAsAgent");
        Log("Registered as print agent.");
    }
    catch (Exception ex)
    {
        Log($"SignalR connection failed: {ex.Message}");
        Log("Falling back to polling...");
        await RunPollingFallback(baseUrl, apiKey, defaultPrinter, services);
    }

    await Task.Delay(-1);
}

// ═══════════════════════════════════════════════════════════
//  STATE-MACHINE POLLING FALLBACK
// ═══════════════════════════════════════════════════════════
async Task RunPollingFallback(string baseUrl, string apiKey, string defaultPrinter, IServiceProvider services)
{
    Log("Starting stateful polling fallback...");
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    var printService = services.GetRequiredService<PdfPrintService>();

    while (true)
    {
        try
        {
            var pendingResponse = await client.GetAsync($"{baseUrl}/api/pdf/print-agent/pending");
            if (pendingResponse.IsSuccessStatusCode)
            {
                var json = await pendingResponse.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PendingResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (result?.Jobs != null)
                {
                    foreach (var jobId in result.Jobs)
                    {
                        Log($"Found pending job: {jobId}");

                        var claimResponse = await client.PostAsync($"{baseUrl}/api/pdf/print-agent/claim/{jobId}", null);
                        if (!claimResponse.IsSuccessStatusCode)
                        {
                            Log($"Claim failed for {jobId}, skipping");
                            continue;
                        }

                        var claimJson = await claimResponse.Content.ReadAsStringAsync();
                        var claimResult = JsonSerializer.Deserialize<ClaimResponse>(claimJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        var copies = claimResult?.Copies ?? 1;
                        var jobPrinter = claimResult?.PrinterName;
                        var effectivePrinter = !string.IsNullOrWhiteSpace(jobPrinter) ? jobPrinter : defaultPrinter;

                        var settings = new PrintSettings
                        {
                            PrinterName = effectivePrinter,
                            Copies = copies,
                            ScalingMode = claimResult?.ScalingMode ?? "actual",
                            CustomScale = claimResult?.CustomScale ?? 100,
                            Orientation = claimResult?.Orientation ?? "portrait",
                            PaperSize = claimResult?.PaperSize ?? "A4",
                            MarginUnit = claimResult?.MarginUnit ?? "mm",
                            MarginTop = claimResult?.MarginTop ?? 0,
                            MarginBottom = claimResult?.MarginBottom ?? 0,
                            MarginLeft = claimResult?.MarginLeft ?? 0,
                            MarginRight = claimResult?.MarginRight ?? 0
                        };

                        Log($"Printing job {jobId}, {copies} copy(ies), printer: {settings.PrinterName}, scaling: {settings.ScalingMode}");

                        try
                        {
                            await printService.DownloadAndPrintAsync(jobId, settings);
                            Log($"Job {jobId} completed successfully");
                        }
                        catch (Exception ex)
                        {
                            Log($"Job {jobId} FAILED: {ex.Message}");
                            try
                            {
                                var release = await client.PostAsync($"{baseUrl}/api/pdf/print-agent/release/{jobId}", null);
                                Log($"Released job {jobId} back to queue");
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Polling error: {ex.Message}");
        }

        await Task.Delay(3000);
    }
}

// ═══════════════════════════════════════════════════════════
//  DTOs
// ═══════════════════════════════════════════════════════════

public class PendingResponse
{
    public List<string> Jobs { get; set; } = new();
}

public class ClaimResponse
{
    public bool Success { get; set; }
    public string JobId { get; set; } = "";
    public int Copies { get; set; } = 1;
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Orientation { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}

public class PrintJobRequest
{
    public string JobId { get; set; } = "";
    public int BookId { get; set; }
    public int Copies { get; set; } = 1;
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Orientation { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}
