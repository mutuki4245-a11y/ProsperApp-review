using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ProsperApp.Features.Admin;

public sealed class AdminAuthorizationService(
    IHttpContextAccessor httpContextAccessor,
    IDataProtectionProvider dataProtectionProvider) : IAdminAuthorizationService
{
    private const string CookieName = "ProsperApp.AdminSession";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("ProsperApp.AdminAuthorization.v1");
    private bool? _currentRequestMode;

    public bool IsAdminMode
    {
        get
        {
            if (_currentRequestMode is not null)
            {
                return _currentRequestMode.Value;
            }

            var context = _httpContextAccessor.HttpContext;
            if (context is null ||
                !context.Request.Cookies.TryGetValue(CookieName, out var cookieValue) ||
                string.IsNullOrWhiteSpace(cookieValue))
            {
                return false;
            }

            var currentUserKey = GetCurrentUserKey(context);
            if (string.IsNullOrWhiteSpace(currentUserKey) ||
                !TryReadSession(cookieValue, out var session) ||
                session.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                !string.Equals(session.UserKey, currentUserKey, StringComparison.Ordinal))
            {
                DeleteCookie(context);
                return false;
            }

            return true;
        }
    }

    public void SetAdminMode(bool enabled)
    {
        _currentRequestMode = enabled;

        var context = _httpContextAccessor.HttpContext;
        if (context is null)
        {
            return;
        }

        if (!enabled)
        {
            DeleteCookie(context);
            return;
        }

        var userKey = GetCurrentUserKey(context);
        if (string.IsNullOrWhiteSpace(userKey))
        {
            DeleteCookie(context);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var session = new AdminSessionEnvelope
        {
            UserKey = userKey,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(SessionLifetime)
        };
        var protectedValue = _protector.Protect(JsonSerializer.Serialize(session, JsonOptions));
        context.Response.Cookies.Append(CookieName, protectedValue, CreateCookieOptions(context.Request, session.ExpiresAtUtc));
    }

    private bool TryReadSession(string protectedValue, out AdminSessionEnvelope session)
    {
        session = new AdminSessionEnvelope();

        try
        {
            var json = _protector.Unprotect(protectedValue);
            session = JsonSerializer.Deserialize<AdminSessionEnvelope>(json, JsonOptions) ?? new AdminSessionEnvelope();
            return !string.IsNullOrWhiteSpace(session.UserKey);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetCurrentUserKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return context.User.FindFirstValue(ClaimTypes.Email) ??
               context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
               context.User.Identity?.Name;
    }

    private static void DeleteCookie(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Cookies.Delete(CookieName, CreateCookieOptions(context.Request, null));
    }

    private static CookieOptions CreateCookieOptions(HttpRequest request, DateTimeOffset? expires)
    {
        return new CookieOptions
        {
            Expires = expires,
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = request.IsHttps,
            Path = "/"
        };
    }

    private sealed class AdminSessionEnvelope
    {
        public string? UserKey { get; init; }

        public DateTimeOffset IssuedAtUtc { get; init; }

        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
