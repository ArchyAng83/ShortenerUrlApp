using System.ComponentModel.DataAnnotations;

namespace ShortenerUrlApp.Shared.Validation
{
    // Stricter than [Url]: DataAnnotations' [Url] accepts relative links and non-http schemes,
    // while the URL shortener only ever forwards absolute http/https targets.
    public sealed class HttpUrlAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value is not string url)
            {
                return false;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }
    }
}
