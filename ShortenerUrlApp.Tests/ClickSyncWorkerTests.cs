using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ShortenerUrlApp.WebApi.Services;

namespace ShortenerUrlApp.Tests
{
    public class ClickSyncWorkerTests
    {
        private static ClickSyncWorker CreateWorker(IShortenerUrlService service)
        {
            var services = new ServiceCollection();
            services.AddSingleton(service);
            return new ClickSyncWorker(services.BuildServiceProvider());
        }

        [Fact]
        public async Task FlushOnceAsync_ShouldCallSyncClicksToDb()
        {
            var service = new Mock<IShortenerUrlService>();
            var worker = CreateWorker(service.Object);

            await worker.FlushOnceAsync(CancellationToken.None);

            service.Verify(s => s.SyncClicksToDbAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FlushOnceAsync_ShouldSwallowExceptions()
        {
            var service = new Mock<IShortenerUrlService>();
            service.Setup(s => s.SyncClicksToDbAsync(It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("boom"));
            var worker = CreateWorker(service.Object);

            await worker.FlushOnceAsync(CancellationToken.None);

            service.Verify(s => s.SyncClicksToDbAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FlushOnceAsync_ShouldStopCleanly_WhenCancelled()
        {
            var service = new Mock<IShortenerUrlService>();
            service.Setup(s => s.SyncClicksToDbAsync(It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new OperationCanceledException());
            var worker = CreateWorker(service.Object);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await worker.FlushOnceAsync(cts.Token);
        }
    }
}
