namespace ShortenerUrlApp.Shared.DTOs
{
    public record AuthResponseDto(string Token, DateTime Expiration, string UserName);
}
