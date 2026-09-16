using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ShortenerUrlApp.WebApi.Services;

namespace ShortenerUrlApp.Tests
{
    public class GeoIpServiceTests
    {
        private static readonly string TestDatabasePath =
            Path.Combine(AppContext.BaseDirectory, "GeoIp", "GeoIP2-City-Test.mmdb");

        private static GeoIpService CreateService(string databasePath) =>
            new(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["GeoIp:DatabasePath"] = databasePath
                    })
                    .Build(),
                NullLogger<GeoIpService>.Instance);

        private static string RequiredTestDatabasePath()
        {
            if (!File.Exists(TestDatabasePath))
                throw new FileNotFoundException($"Missing GeoIP test database asset: {TestDatabasePath}");
            return TestDatabasePath;
        }

        [Fact]
        public void Resolve_WhenConfiguredPathIsMissing_ReturnsNullWithoutThrowing()
        {
            var service = CreateService(Path.Combine(Path.GetTempPath(), "does-not-exist.mmdb"));

            service.Resolve("81.2.69.142").Should().BeNull();
        }

        [Fact]
        public void Resolve_WhenDatabaseFileIsCorrupt_ReturnsNullWithoutThrowing()
        {
            var corruptPath = Path.Combine(Path.GetTempPath(), $"corrupt-{Guid.NewGuid():N}.mmdb");
            try
            {
                File.WriteAllBytes(corruptPath, [1, 2, 3, 4]);
                var service = CreateService(corruptPath);

                service.Resolve("81.2.69.142").Should().BeNull();
            }
            finally
            {
                File.Delete(corruptPath);
            }
        }

        [Fact]
        public void Resolve_WhenIpAddressIsNullOrMalformed_ReturnsNull()
        {
            var service = CreateService(RequiredTestDatabasePath());

            service.Resolve(null).Should().BeNull();
            service.Resolve("").Should().BeNull();
            service.Resolve("   ").Should().BeNull();
            service.Resolve("not-an-ip").Should().BeNull();
        }

        [Fact]
        public void Resolve_PrivateAndReservedAddresses_ReturnNull()
        {
            var service = CreateService(RequiredTestDatabasePath());

            var privateAndReserved = new[]
            {
                "127.0.0.1", "10.1.2.3", "192.168.0.1", "172.16.5.5", "172.31.5.5",
                "169.254.1.1", "100.64.0.1", "0.0.0.1", "::1", "fe80::1", "fc00::1"
            };

            foreach (var ip in privateAndReserved)
            {
                service.Resolve(ip).Should().BeNull(ip);
            }
        }

        [Fact]
        public void Resolve_KnownTestAddress_ReturnsCountryAndCity()
        {
            var service = CreateService(RequiredTestDatabasePath());

            var location = service.Resolve("81.2.69.142");

            location.Should().NotBeNull();
            location!.CountryCode.Should().Be("GB");
            location.CityName.Should().Be("London");
        }

        [Fact]
        public void Resolve_KnownAddressInIpv4MappedForm_ReturnsCountry()
        {
            var service = CreateService(RequiredTestDatabasePath());

            var location = service.Resolve("::ffff:81.2.69.142");

            location.Should().NotBeNull();
            location!.CountryCode.Should().Be("GB");
        }

        [Fact]
        public void Resolve_AddressNotMappedInDatabase_ReturnsNull()
        {
            var service = CreateService(RequiredTestDatabasePath());

            // TEST-NET-2 documentation range: never present in real or test geo databases.
            service.Resolve("198.51.100.42").Should().BeNull();
        }
    }
}
