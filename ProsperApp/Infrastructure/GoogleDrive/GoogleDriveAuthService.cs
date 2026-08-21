using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using ProsperApp.Options;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProsperApp.Infrastructure.GoogleDrive;

public interface IGoogleDriveAuthService
{
    bool IsGoogleAuthConfigured { get; }

    string? ConfigurationErrorMessage { get; }

    Task<string?> GetAccessTokenAsync();

    Task<bool> HasAccessTokenAsync();

    void ClearAccessToken();
}

public sealed class GoogleDriveAuthService(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IOptions<GoogleDriveOptions> driveOptions,
    ILogger<GoogleDriveAuthService> logger) : IGoogleDriveAuthService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly GoogleDriveOptions _driveOptions = driveOptions.Value;
    private readonly ILogger<GoogleDriveAuthService> _logger = logger;

    public bool IsGoogleAuthConfigured =>
        !string.IsNullOrWhiteSpace(_driveOptions.ClientId) &&
        !string.IsNullOrWhiteSpace(_driveOptions.ClientSecret);

    public string? ConfigurationErrorMessage
    {
        get
        {
            if (!IsGoogleAuthConfigured)
            {
                return "Google認証設定が未設定です。Azure App Serviceの環境変数 GoogleDrive__ClientId / GoogleDrive__ClientSecret を設定してください。";
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

        var authentication = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var accessToken = authentication.Properties?.GetTokenValue("access_token");
        var expiresAtText = authentication.Properties?.GetTokenValue("expires_at");
        if (!string.IsNullOrWhiteSpace(accessToken) &&
            DateTimeOffset.TryParse(expiresAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt) &&
            expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return accessToken;
        }

        var refreshToken = authentication.Properties?.GetTokenValue("refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken) || authentication.Principal is null || authentication.Properties is null)
        {
            await RejectTicketAsync(httpContext);
            return null;
        }

        try
        {
            using var response = await _httpClient.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _driveOptions.ClientId,
                    ["client_secret"] = _driveOptions.ClientSecret,
                    ["refresh_token"] = refreshToken,
                    ["grant_type"] = "refresh_token"
                }),
                httpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google token refresh failed with status {Status}", (int)response.StatusCode);
                await RejectTicketAsync(httpContext);
                return null;
            }

            var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
                cancellationToken: httpContext.RequestAborted);
            if (string.IsNullOrWhiteSpace(token?.AccessToken) || token.ExpiresIn <= 0)
            {
                await RejectTicketAsync(httpContext);
                return null;
            }

            authentication.Properties.UpdateTokenValue("access_token", token.AccessToken);
            authentication.Properties.UpdateTokenValue(
                "expires_at",
                DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn).ToString("O", CultureInfo.InvariantCulture));
            authentication.Properties.UpdateTokenValue("refresh_token", refreshToken);
            authentication.Properties.IsPersistent = true;
            authentication.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(90);
            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                authentication.Principal,
                authentication.Properties);
            return token.AccessToken;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Google token refresh request failed");
            await RejectTicketAsync(httpContext);
            return null;
        }
    }

    public async Task<bool> HasAccessTokenAsync()
    {
        return !string.IsNullOrWhiteSpace(await GetAccessTokenAsync());
    }

    public void ClearAccessToken()
    {
        _httpContextAccessor.HttpContext?.Session.Clear();
    }

    private static async Task RejectTicketAsync(HttpContext context)
    {
        context.Session.Clear();
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private sealed class GoogleTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
