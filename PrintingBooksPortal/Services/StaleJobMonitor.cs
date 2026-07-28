namespace PrintingBooksPortal.Services;

public class StaleJobMonitor : BackgroundService
{
    private readonly ILogger<StaleJobMonitor> _logger;

    public StaleJobMonitor(ILogger<StaleJobMonitor> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StaleJobMonitor started: reverting stale Processing jobs every 60s.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-2);
                var staleCount = 0;

                foreach (var kv in Controllers.JobStatusTracker.Jobs
                    .Where(kv => kv.Value.Status == Controllers.JobStatus.Processing && kv.Value.LastUpdated < cutoff))
                {
                    if (Controllers.JobStatusTracker.Jobs.TryGetValue(kv.Key, out var info))
                    {
                        info.Status = Controllers.JobStatus.Queued;
                        info.Message = "Reset from stale Processing state (agent crash)";
                        info.LastUpdated = DateTime.UtcNow;
                        staleCount++;

                        if (!Controllers.PendingPrintJobs.Jobs.ContainsKey(kv.Key))
                        {
                            Controllers.PendingPrintJobs.Jobs.TryAdd(kv.Key, new Controllers.PendingJobInfo
                            {
                                TeacherId = 0,
                                Copies = 1,
                                CreatedAt = DateTime.UtcNow
                            });
                        }

                        _logger.LogWarning("Stale job {JobId} reverted to Queued and re-added to pending queue.", kv.Key);
                    }
                }

                if (staleCount > 0)
                    _logger.LogInformation("Reverted {Count} stale Processing jobs.", staleCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StaleJobMonitor error");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
