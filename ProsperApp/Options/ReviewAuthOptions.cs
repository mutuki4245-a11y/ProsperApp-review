using System.Security.Cryptography;
using System.Text;

namespace ProsperApp.Options;

public sealed class ReviewAuthOptions
{
    public const string ClaimType = "ProsperApp.ReviewAuth";
    public const string ClaimValue = "true";

    public bool Enabled { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "レビュー利用者";
    public string Subject { get; set; } = "prosper-review-auth";
    public int CookieHours { get; set; } = 8;

    public bool IsEnabled =>
        Enabled &&
        !string.IsNullOrWhiteSpace(Token) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(Subject);

    public bool IsValidToken(string? token)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Token);
        var supplied = Encoding.UTF8.GetBytes(token);
        return expected.Length == supplied.Length &&
               CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    public DateTimeOffset GetExpiresUtc() =>
        DateTimeOffset.UtcNow.AddHours(Math.Clamp(CookieHours, 1, 24));
}
