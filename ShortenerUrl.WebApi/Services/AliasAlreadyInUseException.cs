namespace ShortenerUrlApp.WebApi.Services
{
    // Thrown when a user-supplied custom alias collides with an existing short code.
    public class AliasAlreadyInUseException(string alias)
        : Exception($"Alias '{alias}' is already taken.")
    {
        public string Alias { get; } = alias;
    }
}
