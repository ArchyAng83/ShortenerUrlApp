using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ShortenerUrlApp.WebApi.Services;

namespace ShortenerUrlApp.Tests
{
    public class ExpiredLinksCleanupWorkerTests
    {
        private static ExpiredLinksCleanupWorker CreateWorker(IShortenerUrlService service)
        {
            var services = new ServiceCollection();
            services.AddSingleton(service);
            return new ExpiredLinksCleanupWorker(services.BuildServiceProvider());
        }

        [Fact]
        public async Task CleanupOnceAsync_ShouldCallDeleteExpiredUrls()
        {
            var service = new Mock<IShortenerUrlService>();
            service.Setup(s => s.DeleteExpiredUrlsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(3);
            var worker = CreateWorker(service.Object);

            await worker.CleanupOnceAsync(CancellationToken.None);

            service.Verify(s => s.DeleteExpiredUrlsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CleanupOnceAsync_NoDeleted_ShouldNotLog()
        {
            var service = new Mock<IShortenerUrlService>();
            service.Setup(s => s.DeleteExpiredUrlsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(0);
            var worker = CreateWorker(service.Object);

            await worker.CleanupOnceAsync(CancellationToken.None);

            service.Verify(s => s.DeleteExpiredUrlsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CleanupOnceAsync_ShouldSwallowExceptions()
        {
            var service = new Mock<IShortenerUrlService>();
            service.Setup(s => s.DeleteExpiredUrlsAsync(It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("boom"));
            var worker = CreateWorker(service.Object);

            await worker.CleanupOnceAsync(CancellationToken.None);

            service.Verify(s => s.DeleteExpiredUrlsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
