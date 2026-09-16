using FluentAssertions;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.Shared.Validation;

namespace ShortenerUrlApp.Tests
{
    public class SharedContractTests
    {
        [Fact]
        public void ClickEventDto_ShouldSupportRecordSemantics()
        {
            // Arrange
            var id = Guid.NewGuid();
            var clickedAt = DateTime.UtcNow;
            var a = new ClickEventDto(id, clickedAt, "1.2.3.4", "ua", "GB", "London", "https://ref");
            var b = new ClickEventDto(id, clickedAt, "1.2.3.4", "ua", "GB", "London", "https://ref");
            var c = a with { Country = "DE" };

            // Act & Assert
            a.Should().Be(b);
            a.GetHashCode().Should().Be(b.GetHashCode());
            a.Should().NotBe(c);
            a.ToString().Should().Contain("London");

            // Deconstruct is part of the positional record contract consumed by analytics projections.
            var (dtoId, _, ip, userAgent, country, city, _) = a;
            dtoId.Should().Be(id);
            ip.Should().Be("1.2.3.4");
            userAgent.Should().Be("ua");
            country.Should().Be("GB");
            city.Should().Be("London");
        }

        [Theory]
        [InlineData("https://example.com", true)]
        [InlineData("http://example.com", true)]
        [InlineData("ftp://example.com", false)]
        [InlineData("/relative", false)]
        [InlineData("not a url", false)]
        public void HttpUrlAttribute_ShouldValidateOnlyAbsoluteHttpUrls(string url, bool expected)
        {
            // Arrange
            var attribute = new HttpUrlAttribute();

            // Act & Assert
            attribute.IsValid(url).Should().Be(expected);
        }

        [Fact]
        public void HttpUrlAttribute_ShouldRejectNonStringValues()
        {
            // Arrange
            var attribute = new HttpUrlAttribute();

            // Act & Assert
            attribute.IsValid(null).Should().BeFalse();
            attribute.IsValid(42).Should().BeFalse();
        }
    }
}
