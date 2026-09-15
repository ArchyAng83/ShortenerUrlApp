namespace ShortenerUrlApp.WebApi.Services
{
    // Periodically removes links whose ExpiresAt has passed and evicts their Redis keys.
    // Without this worker expired rows would linger in PostgreSQL forever; redirects are
    // already blocked at read time, so this is pure storage hygiene.
    public class ExpiredLinksCleanupWorker(IServiceProvider serviceProvider) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(Interval, stoppingToken);

                using var scope = serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IShortenerUrlService>();

                try
                {
                    var deleted = await service.DeleteExpiredUrlsAsync(stoppingToken);

                    if (deleted > 0)
                    {
                        Console.WriteLine($"ExpiredLinksCleanup: removed {deleted} expired link(s).");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Host is shutting down mid-sweep; nothing to log.
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error ExpiredCleanup: {ex.Message}");
                }
            }
        }
    }
}
