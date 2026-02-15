namespace ShortenerUrlApp.WebApi.Services
{
    //Для синхронизации кликов с БД в фоновом режиме
    public class ClickSyncWorker(IServiceProvider serviceProvider) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Интервал синхронизации (1 минута)
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                using var scope = serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IShortenerUrlService>();

                try
                {
                    await service.SyncClicksToDbAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error Sync: {ex.Message}");
                }
            }
        }
    }
}
