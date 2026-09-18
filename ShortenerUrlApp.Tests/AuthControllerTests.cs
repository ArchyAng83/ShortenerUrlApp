using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ShortenerUrlApp.Shared.DTOs;
using ShortenerUrlApp.WebApi.Controllers;
using ShortenerUrlApp.WebApi.Services;

namespace ShortenerUrlApp.Tests
{
    public class AuthControllerTests
    {
        private static AuthResponseDto SampleAuth() =>
            new("header.payload.signature", DateTime.UtcNow.AddMinutes(60), "vasya");

        [Fact]
        public async Task Register_OnFailure_ShouldReturnBadRequest_WithErrors()
        {
            // Arrange
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthResultDto.Failure(["Password is too short."]));

            var controller = new AuthController(authMock.Object);

            // Act
            var result = await controller.RegisterAsync(
                new RegisterDto("vasya", "vasya@example.com", "ab1"), CancellationToken.None);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Register_OnSuccess_ShouldReturnOk_WithToken()
        {
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthResultDto.Success(SampleAuth()));

            var controller = new AuthController(authMock.Object);

            var result = await controller.RegisterAsync(
                new RegisterDto("vasya", "vasya@example.com", "Secret123"), CancellationToken.None);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Login_OnFailure_ShouldReturnUnauthorized()
        {
            // Arrange
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.LoginAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthResultDto.Failure(["Invalid email or password."]));

            var controller = new AuthController(authMock.Object);

            // Act
            var result = await controller.LoginAsync(
                new LoginDto("vasya@example.com", "wrong"), CancellationToken.None);

            // Assert
            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Login_OnSuccess_ShouldReturnOk_WithToken()
        {
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.LoginAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthResultDto.Success(SampleAuth()));

            var controller = new AuthController(authMock.Object);

            var result = await controller.LoginAsync(
                new LoginDto("vasya@example.com", "Secret123"), CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeOfType<AuthResponseDto>().Which.UserName.Should().Be("vasya");
        }

        [Fact]
        public async Task ForgotPassword_ForUnknownEmail_ShouldStillReturnOk()
        {
            // Anti-enumeration: the controller must not reveal whether the account exists.
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.ForgotPasswordAsync(It.IsAny<ForgotPasswordDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var controller = new AuthController(authMock.Object);

            var result = await controller.ForgotPasswordAsync(
                new ForgotPasswordDto("ghost@example.com"), CancellationToken.None);

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task ForgotPassword_ForKnownEmail_ShouldReturnOk()
        {
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.ForgotPasswordAsync(It.IsAny<ForgotPasswordDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var controller = new AuthController(authMock.Object);

            var result = await controller.ForgotPasswordAsync(
                new ForgotPasswordDto("vasya@example.com"), CancellationToken.None);

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task ResetPassword_OnSuccess_ShouldReturnOk()
        {
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var controller = new AuthController(authMock.Object);

            var result = await controller.ResetPasswordAsync(
                new ResetPasswordDto("vasya@example.com", "token", "B!c5d6e7f8g9h0"), CancellationToken.None);

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task ResetPassword_OnInvalidToken_ShouldReturnBadRequest()
        {
            var authMock = new Mock<IAuthService>();
            authMock
                .Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var controller = new AuthController(authMock.Object);

            var result = await controller.ResetPasswordAsync(
                new ResetPasswordDto("vasya@example.com", "bad-token", "B!c5d6e7f8g9h0"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}
