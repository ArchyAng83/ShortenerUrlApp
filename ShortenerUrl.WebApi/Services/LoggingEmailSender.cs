using Microsoft.AspNetCore.Identity;
using ShortenerUrlApp.WebApi.Entities;

namespace ShortenerUrlApp.WebApi.Services
{
    // Default identity email sender used until a real SMTP provider is wired up.
    // It logs the confirmation link instead of delivering mail, so local/docker
    // development retains a working confirm-email flow without external services.
    public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender<ApplicationUser>
    {
        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            logger.LogInformation("Email confirmation for {Email}: {Link}", email, confirmationLink);
            return Task.CompletedTask;
        }

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            logger.LogInformation("Password reset link for {Email}: {Link}", email, resetLink);
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            logger.LogInformation("Password reset code for {Email}: {ResetCode}", email, resetCode);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            logger.LogInformation("Email to {Email} subject '{Subject}': {Message}", email, subject, htmlMessage);
            return Task.CompletedTask;
        }
    }
}