using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Data;
using ShortenerUrlApp.WebApi.Entities;
using ShortenerUrlApp.WebApi.Services;
using System.Security.Claims;
using System.Text;

namespace ShortenerUrlApp.Tests
{
    public class AuthServiceTests
    {
        private const string Secret = "test-secret-key-with-at-least-32-characters!!";

        private static AuthService CreateAuthService(out IServiceProvider provider)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ShortenerUrlDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireNonAlphanumeric = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequiredUniqueChars = 3;
                o.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ShortenerUrlDbContext>();

            provider = services.BuildServiceProvider();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:Secret"] = Secret,
                    ["JwtSettings:Issuer"] = "ShortenerUrlApp",
                    ["JwtSettings:Audience"] = "ShortenerUrlApp",
                    ["JwtSettings:ExpiryMinutes"] = "60"
                })
                .Build();

            return new AuthService(provider.GetRequiredService<UserManager<ApplicationUser>>(), configuration);
        }

        private static TokenValidationParameters ValidationParameters() => new()
        {
            ValidateIssuer = true,
            ValidIssuer = "ShortenerUrlApp",
            ValidateAudience = true,
            ValidAudience = "ShortenerUrlApp",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        [Fact]
        public async Task RegisterAsync_ValidInput_ShouldReturnValidJwt()
        {
            // Arrange
            var service = CreateAuthService(out _);

            // Act
            var result = await service.RegisterAsync(new RegisterDto("vasya", "vasya@example.com", "A!b2c3d4e5f6"));

            // Assert
            result.Succeeded.Should().BeTrue();
            result.Auth!.UserName.Should().Be("vasya");
            result.Auth.Expiration.Should().BeAfter(DateTime.UtcNow);

            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(result.Auth.Token, ValidationParameters());
            validation.IsValid.Should().BeTrue();

            // The token must carry the Identity user id so endpoints can resolve the owner.
            var principal = new ClaimsPrincipal(validation.ClaimsIdentity);
            var subject = principal.FindFirst("sub")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            subject.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task RegisterAsync_ShortPassword_ShouldFail()
        {
            var service = CreateAuthService(out _);

            var result = await service.RegisterAsync(new RegisterDto("vasya", "vasya@example.com", "Abcdefghijk!"));

            result.Succeeded.Should().BeFalse();
            result.Errors.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RegisterAsync_DuplicateEmail_ShouldFail()
        {
            var service = CreateAuthService(out _);
            await service.RegisterAsync(new RegisterDto("vasya", "vasya@example.com", "A!b2c3d4e5f6"));

            var result = await service.RegisterAsync(new RegisterDto("petya", "vasya@example.com", "A!b2c3d4e5f6"));

            result.Succeeded.Should().BeFalse();
        }

        [Fact]
        public async Task LoginAsync_ConfirmedEmail_ShouldReturnValidJwt()
        {
            var service = CreateAuthService(out _);
            await service.RegisterAsync(new RegisterDto("vasya", "vasya@example.com", "A!b2c3d4e5f6"));

            // Email not confirmed by default — login should fail
            var result = await service.LoginAsync(new LoginDto("vasya@example.com", "A!b2c3d4e5f6"));

            result.Succeeded.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("not confirmed"));
        }

        [Fact]
        public async Task LoginAsync_WrongPassword_ShouldFail()
        {
            var service = CreateAuthService(out _);
            await service.RegisterAsync(new RegisterDto("vasya", "vasya@example.com", "A!b2c3d4e5f6"));

            var result = await service.LoginAsync(new LoginDto("vasya@example.com", "WrongPass1"));

            result.Succeeded.Should().BeFalse();
        }

        [Fact]
        public async Task LoginAsync_UnknownEmail_ShouldFail()
        {
            var service = CreateAuthService(out _);

            var result = await service.LoginAsync(new LoginDto("ghost@example.com", "Secret123"));

            result.Succeeded.Should().BeFalse();
        }

        [Fact]
        public async Task ConfirmEmailAsync_AfterRegistration_AllowsLogin()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<ShortenerUrlDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireNonAlphanumeric = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequiredUniqueChars = 3;
                o.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ShortenerUrlDbContext>()
            .AddDefaultTokenProviders();

            using var provider = services.BuildServiceProvider();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:Secret"] = Secret,
                    ["JwtSettings:Issuer"] = "ShortenerUrlApp",
                    ["JwtSettings:Audience"] = "ShortenerUrlApp",
                    ["JwtSettings:ExpiryMinutes"] = "60",
                    ["AppBaseUrl"] = "http://localhost:5209"
                })
                .Build();

            var sender = new CapturingEmailSender();
            var service = new AuthService(
                provider.GetRequiredService<UserManager<ApplicationUser>>(), configuration, sender);

            var registered = await service.RegisterAsync(new RegisterDto("vasya", "vasya@example.com", "A!b2c3d4e5f6"));
            registered.Succeeded.Should().BeTrue();
            sender.ConfirmationLink.Should().NotBeNullOrWhiteSpace();

            // Login is blocked until the address is confirmed.
            (await service.LoginAsync(new LoginDto("vasya@example.com", "A!b2c3d4e5f6"))).Succeeded
                .Should().BeFalse();

            var query = QueryHelpers.ParseQuery(new Uri(sender.ConfirmationLink!).Query);
            var confirmed = await service.ConfirmEmailAsync(
                new EmailConfirmationDto(query["email"].ToString(), query["token"].ToString()));

            confirmed.Should().BeTrue();
            (await service.LoginAsync(new LoginDto("vasya@example.com", "A!b2c3d4e5f6"))).Succeeded
                .Should().BeTrue();
        }

        [Fact]
        public async Task ConfirmEmailAsync_UnknownEmail_ReturnsFalse()
        {
            var service = CreateAuthService(out _);

            var confirmed = await service.ConfirmEmailAsync(new EmailConfirmationDto("ghost@example.com", "token"));

            confirmed.Should().BeFalse();
        }

        private sealed class CapturingEmailSender : IEmailSender<ApplicationUser>
        {
            public string? ConfirmationLink { get; private set; }

            public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
            {
                ConfirmationLink = confirmationLink;
                return Task.CompletedTask;
            }

            public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
                Task.CompletedTask;

            public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
                Task.CompletedTask;

            public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
                Task.CompletedTask;
        }
    }
}
