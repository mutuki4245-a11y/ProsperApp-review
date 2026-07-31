namespace ProsperApp.Services;

public sealed class AdminModeService(IHttpContextAccessor httpContextAccessor) : IAdminModeService
{
    private const string SessionKey = "ProsperApp.AdminMode";
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public bool IsEnabled =>
        string.Equals(
            _httpContextAccessor.HttpContext?.Session.GetString(SessionKey),
            "1",
            StringComparison.Ordinal);

    public void SetEnabled(bool enabled)
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session is null)
        {
            return;
        }

        if (enabled)
        {
            session.SetString(SessionKey, "1");
        }
        else
        {
            session.Remove(SessionKey);
        }
    }
}
