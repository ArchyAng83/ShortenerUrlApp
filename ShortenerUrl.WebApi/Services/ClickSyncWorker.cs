namespace ShortenerUrlApp.WebApi.Services
{
    /// <summary>
    /// Flushes the per-code integer counters ("clicks:{shortCode}") into the
    /// ShortenerUrl.CountOfClick column once a minute using ExecuteUpdate, so the
    /// 10k req/s redirect hot path only ever touches Redis.
    /// Distinct from <see cref="ClickEventSyncWorker"/>, which drains the rich
    /// JSON click metadata ("click-events:{shortCode}") into the ClickEvents table.
    /// </summary>
    public class ClickSyncWorker(IServiceProvider serviceProvider) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await FlushOnceAsync(stoppingToken);
            }
        }

        // Per-tick flush, isolated so unit tests can exercise it without a minute-long delay.
        internal async Task FlushOnceAsync(CancellationToken ct)
        {
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IShortenerUrlService>();

            try
            {
                await service.SyncClicksToDbAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Worker is shutting down mid-flush; nothing to log.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Sync: {ex.Message}");
            }
        }
    }
}
