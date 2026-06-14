namespace ProsperApp.Options;

public sealed class ReviewAuthOptions
{
    public const string ClaimType = "ProsperApp.ReviewAuth";

    public const string ClaimValue = "true";

    public bool Enabled { get; set; }

    public string Token { get; set; } = string.Empty;

    public string Email { get; set; } = "reviewer@prosperapp.local";

    public string DisplayName { get; set; } = "レビュー利用者";

    public int CookieHours { get; set; } = 8;

    public bool IsEnabled => Enabled && !string.IsNullOrWhiteSpace(Token);

    public bool IsValidToken(string? token)
    {
        return IsEnabled &&
               !string.IsNullOrWhiteSpace(token) &&
               string.Equals(Token, token, StringComparison.Ordinal);
    }

    public DateTimeOffset GetExpiresUtc()
    {
        return DateTimeOffset.UtcNow.AddHours(Math.Clamp(CookieHours, 1, 24));
    }
}
