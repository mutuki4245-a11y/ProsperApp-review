using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ProsperApp.Options;

namespace ProsperApp.Services;

public interface IGoogleDriveAuthService
{
    bool IsGoogleAuthConfigured { get; }

    bool HasAccessRestriction { get; }

    string? ConfigurationErrorMessage { get; }

    Task<string?> GetAccessTokenAsync();

    Task<bool> HasAccessTokenAsync();

    void ClearAccessToken();
}

public sealed class GoogleDriveAuthService(
    IHttpContextAccessor httpContextAccessor,
    IOptions<GoogleDriveOptions> driveOptions,
    IOptions<GoogleAuthOptions> authOptions) : IGoogleDriveAuthService
{
    private const string GoogleDriveAccessTokenSessionKey = "GoogleDriveAccessToken";

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly GoogleDriveOptions _driveOptions = driveOptions.Value;
    private readonly GoogleAuthOptions _authOptions = authOptions.Value;

    public bool IsGoogleAuthConfigured =>
        !string.IsNullOrWhiteSpace(_driveOptions.ClientId) &&
        !string.IsNullOrWhiteSpace(_driveOptions.ClientSecret);

    public bool HasAccessRestriction => _authOptions.HasAccessRestriction;

    public string? ConfigurationErrorMessage
    {
        get
        {
            if (!IsGoogleAuthConfigured)
            {
                return "Google認証設定が未設定です。Azure App Serviceの環境変数 GoogleDrive__ClientId / GoogleDrive__ClientSecret を設定してください。";
            }

            if (!HasAccessRestriction)
            {
                return "Googleログインの許可アカウントが未設定です。GoogleAuth__AllowedEmails または GoogleAuth__AllowedDomains を設定してください。";
            }

            return null;
        }
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var sessionToken = httpContext.Session.GetString(GoogleDriveAccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            return sessionToken;
        }

        return await httpContext.GetTokenAsync("access_token");
    }

    public async Task<bool> HasAccessTokenAsync()
    {
        return !string.IsNullOrWhiteSpace(await GetAccessTokenAsync());
    }

    public void ClearAccessToken()
    {
        _httpContextAccessor.HttpContext?.Session.Remove(GoogleDriveAccessTokenSessionKey);
    }
}
