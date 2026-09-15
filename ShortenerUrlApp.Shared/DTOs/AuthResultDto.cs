namespace ShortenerUrlApp.Shared.DTOs
{
    // Service-layer auth outcome: success carries the token, failure carries error descriptions.
    public sealed record AuthResultDto(bool Succeeded, AuthResponseDto? Auth, IReadOnlyList<string>? Errors)
    {
        public static AuthResultDto Success(AuthResponseDto auth) => new(true, auth, null);

        public static AuthResultDto Failure(IReadOnlyList<string> errors) => new(false, null, errors);
    }
}
