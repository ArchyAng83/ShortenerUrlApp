namespace ShortenerUrlApp.WebApi.Services
{
    public enum RedirectStatus
    {
        Ok,
        NotFound,
        Expired,
        LimitReached
    }

    // Outcome of resolving a short code on the public redirect path.
    public sealed record RedirectResult(RedirectStatus Status, string? LongUrl = null)
    {
        public bool IsNotFound => Status == RedirectStatus.NotFound;
        public bool IsExpired => Status == RedirectStatus.Expired;
        public bool IsLimitReached => Status == RedirectStatus.LimitReached;

        public static RedirectResult Redirect(string longUrl) => new(RedirectStatus.Ok, longUrl);
        public static RedirectResult NotFound() => new(RedirectStatus.NotFound);
        public static RedirectResult Expired() => new(RedirectStatus.Expired);
        public static RedirectResult LimitReached() => new(RedirectStatus.LimitReached);
    }
}
