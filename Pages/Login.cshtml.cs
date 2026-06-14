using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

[AllowAnonymous]
public class LoginModel(
    IGoogleDriveAuthService googleDriveAuthService) : PageModel
{
    private readonly IGoogleDriveAuthService _googleDriveAuthService = googleDriveAuthService;

    public string? ErrorMessage { get; private set; }

    public string ReturnUrl { get; private set; } = "/";

    public bool CanStartGoogleLogin { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl, string? error, bool forceGoogle = false)
    {
        ReturnUrl = GetSafeReturnUrl(returnUrl);
        CanStartGoogleLogin = string.IsNullOrWhiteSpace(_googleDriveAuthService.ConfigurationErrorMessage);

        if (!string.IsNullOrWhiteSpace(error))
        {
            ErrorMessage = error;
            return Page();
        }

        if (!CanStartGoogleLogin)
        {
            ErrorMessage = _googleDriveAuthService.ConfigurationErrorMessage;
            return Page();
        }

        if (forceGoogle)
        {
            _googleDriveAuthService.ClearAccessToken();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        if (User.Identity?.IsAuthenticated == true &&
            !forceGoogle &&
            await _googleDriveAuthService.HasAccessTokenAsync())
        {
            return LocalRedirect(ReturnUrl);
        }

        return Challenge(
            new AuthenticationProperties
            {
                RedirectUri = ReturnUrl,
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddYears(20),
                AllowRefresh = true
            },
            GoogleDefaults.AuthenticationScheme);
    }

    private string GetSafeReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return Url.Page("/Index") ?? "/";
    }
}
