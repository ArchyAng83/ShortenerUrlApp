namespace ShortenerUrlApp.WebApi.Constants
{
    public class Constant
    {
        public const string ALPHABET = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int MAX_LENGTH_SHORT_URL = 7;

        // Custom aliases are up to 20 chars; generated codes stay at MAX_LENGTH_SHORT_URL.
        public const int MAX_LENGTH_CUSTOM_ALIAS = 20;
        public const int MIN_LENGTH_CUSTOM_ALIAS = 3;
        public const string CUSTOM_ALIAS_PATTERN = "^[a-zA-Z0-9_-]+$";

        // Expiration is capped at one year (in minutes).
        public const int MAX_TTL_MINUTES = 365 * 24 * 60;
    }
}
