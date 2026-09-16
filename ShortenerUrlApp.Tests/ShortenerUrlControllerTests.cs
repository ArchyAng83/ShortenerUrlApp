using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Controllers;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using System.Security.Claims;

namespace ShortenerUrlApp.Tests
{
    public class ShortenerUrlControllerTests
    {
        private static ShortenerUrlController CreateController(Mock<IShortenerUrlService> svc)
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user-1") }));
            return new ShortenerUrlController(svc.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        Request = { Scheme = "https", Host = new HostString("s.tld") },
                        User = user
                    }
                }
            };
        }

        [Fact]
        public async Task GetAllUrlsAsync_ShouldReturnDtosWithPendingClicks()
        {
            // Arrange
            var svc = new Mock<IShortenerUrlService>();
            var url1 = new ShortenerUrl { Id = Guid.NewGuid(), LongUrl = "https://a.com", ShortCode = "code1", CountOfClick = 3 };
            var url2 = new ShortenerUrl { Id = Guid.NewGuid(), LongUrl = "https://b.com", ShortCode = "code2", CountOfClick = 1, ExpiresAt = DateTime.UtcNow.AddDays(-1) };
            svc.Setup(s => s.GetAllUrlsAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ShortenerUrl> { url1, url2 });
            svc.Setup(s => s.GetPendingClicksAsync("code1", It.IsAny<CancellationToken>())).ReturnsAsync(4);
            svc.Setup(s => s.GetPendingClicksAsync("code2", It.IsAny<CancellationToken>())).ReturnsAsync(0);

            var controller = CreateController(svc);

            // Act
            var result = await controller.GetAllUrlsAsync(CancellationToken.None);

            // Assert
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var list = ok.Value.Should().BeAssignableTo<IEnumerable<UrlResponseDto>>().Which.ToList();
            list.Should().HaveCount(2);
            list[0].CountOfClick.Should().Be(7); // 3 persisted + 4 pending
            list[0].ShortUrl.Should().Be("https://s.tld/code1");
            list[0].IsExpired.Should().BeFalse();
            list[1].IsExpired.Should().BeTrue();
        }

        [Fact]
        public async Task CreateShortUrlAsync_InvalidUrl_ShouldReturnBadRequest()
        {
            // Arrange
            var svc = new Mock<IShortenerUrlService>();
            var controller = CreateController(svc);

            // Act
            var result = await controller.CreateShortUrlAsync(
                new CreateShortUrlDto { LongUrl = "not-a-url" }, CancellationToken.None);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
            svc.Verify(s => s.ShortenUrlAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateShortUrlAsync_ReservedAlias_ShouldReturnBadRequest()
        {
            // Arrange
            var svc = new Mock<IShortenerUrlService>();
            var controller = CreateController(svc);

            // Act
            var result = await controller.CreateShortUrlAsync(
                new CreateShortUrlDto { LongUrl = "https://a.com", CustomAlias = "health" }, CancellationToken.None);

            // Assert
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().Be("This alias is reserved.");
        }

        [Fact]
        public async Task CreateShortUrlAsync_InvalidAliasFormat_ShouldReturnBadRequest()
        {
            // Arrange
            var svc = new Mock<IShortenerUrlService>();
            var controller = CreateController(svc);

            // Act
            var result = await controller.CreateShortUrlAsync(
                new CreateShortUrlDto { LongUrl = "https://a.com", CustomAlias = "bad alias!" }, CancellationToken.None);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task CreateShortUrlAsync_Success_ShouldReturnCode()
        {
            // Arrange
            var svc = new Mock<IShortenerUrlService>();
            svc.Setup(s => s.ShortenUrlAsync("https://a.com", "user-1", null, null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync("abc123");
            var controller = CreateController(svc);

            // Act
            var result = await controller.CreateShortUrlAsync(
                new CreateShortUrlDto { LongUrl = "https://a.com" }, CancellationToken.None);

            // Assert
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().Be("abc123");
        }

        [Fact]
        public async Task CreateShortUrlAsync_AliasInUse_ShouldReturnConflict()
        {
            // Arrange
            var svc = new Mock<IShortenerUrlService>();
            svc.Setup(s => s.ShortenUrlAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new AliasAlreadyInUseException("dup"));
            var controller = CreateController(svc);

            // Act
            var result = await controller.CreateShortUrlAsync(
                new CreateShortUrlDto { LongUrl = "https://a.com", CustomAlias = "dup" }, CancellationToken.None);

            // Assert
            result.Should().BeOfType<ConflictObjectResult>();
        }

        [Fact]
        public async Task UpdateLongUrlAsync_InvalidUrl_ShouldReturnBadRequest()
        {
            // Arrange
            var svc = new Mock<IShortenerUrlService>();
            var controller = CreateController(svc);

            // Act
            var result = await controller.UpdateLongUrlAsync(
                new UpdateLongUrlDto { Id = Guid.NewGuid(), LongUrl = "ftp://no" }, CancellationToken.None);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task UpdateLongUrlAsync_Success_ShouldReturnOk()
        {
            // Arrange
            var dto = new UpdateLongUrlDto { Id = Guid.NewGuid(), LongUrl = "https://new.com" };
            var svc = new Mock<IShortenerUrlService>();
            svc.Setup(s => s.UpdateUrlAsync(dto.Id, dto.LongUrl, "user-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var controller = CreateController(svc);

            // Act
            var result = await controller.UpdateLongUrlAsync(dto, CancellationToken.None);

            // Assert
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(dto);
        }

        [Fact]
        public async Task UpdateLongUrlAsync_NotFound_ShouldReturnNotFound()
        {
            // Arrange
            var dto = new UpdateLongUrlDto { Id = Guid.NewGuid(), LongUrl = "https://new.com" };
            var svc = new Mock<IShortenerUrlService>();
            svc.Setup(s => s.UpdateUrlAsync(dto.Id, dto.LongUrl, "user-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var controller = CreateController(svc);

            // Act
            var result = await controller.UpdateLongUrlAsync(dto, CancellationToken.None);

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task DeleteUrlAsync_NotFound_ShouldReturnNotFound()
        {
            // Arrange
            var id = Guid.NewGuid();
            var svc = new Mock<IShortenerUrlService>();
            svc.Setup(s => s.DeleteUrlAsync(id, "user-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var controller = CreateController(svc);

            // Act
            var result = await controller.DeleteUrlAsync(id, CancellationToken.None);

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task DeleteUrlAsync_Success_ShouldReturnNoContent()
        {
            // Arrange
            var id = Guid.NewGuid();
            var svc = new Mock<IShortenerUrlService>();
            svc.Setup(s => s.DeleteUrlAsync(id, "user-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var controller = CreateController(svc);

            // Act
            var result = await controller.DeleteUrlAsync(id, CancellationToken.None);

            // Assert
            result.Should().BeOfType<NoContentResult>();
        }
    }
}
