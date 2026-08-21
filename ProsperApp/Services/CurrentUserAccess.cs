using System.Security.Claims;

namespace ProsperApp.Services;

public interface ICurrentUserAccess
{
    bool IsAuthenticated { get; }
    string? Email { get; }
    string? GoogleSubject { get; }
    IReadOnlySet<long> AllowedDepartmentIds { get; }
    long CurrentDepartmentId { get; }
    string Role { get; }
    bool IsAdministrator { get; }
    bool CanAccessDepartment(long departmentId);
}

public static class ProsperAccessClaims
{
    public const string GoogleSubject = "prosper:google_subject";
    public const string Department = "prosper:department";
    public const string DefaultDepartment = "prosper:default_department";
    public const string DepartmentRole = "prosper:department_role";
}

public sealed class CurrentUserAccess(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccess
{
    private readonly ClaimsPrincipal? _principal = httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated == true;

    public string? Email => _principal?.FindFirst(ClaimTypes.Email)?.Value;

    public string? GoogleSubject => _principal?.FindFirst(ProsperAccessClaims.GoogleSubject)?.Value;

    public IReadOnlySet<long> AllowedDepartmentIds => _principal?
        .FindAll(ProsperAccessClaims.Department)
        .Select(claim => long.TryParse(claim.Value, out var value) ? value : 0)
        .Where(value => value > 0)
        .ToHashSet() ?? [];

    public long CurrentDepartmentId
    {
        get
        {
            var preferred = ReadPreferredDepartmentCookie();
            if (preferred > 0 && CanAccessDepartment(preferred))
            {
                return preferred;
            }

            var defaultClaim = _principal?.FindFirst(ProsperAccessClaims.DefaultDepartment)?.Value;
            if (long.TryParse(defaultClaim, out var defaultDepartment) && CanAccessDepartment(defaultDepartment))
            {
                return defaultDepartment;
            }

            return AllowedDepartmentIds.Order().FirstOrDefault();
        }
    }

    public string Role
    {
        get
        {
            var prefix = $"{CurrentDepartmentId}:";
            var value = _principal?.FindAll(ProsperAccessClaims.DepartmentRole)
                .Select(claim => claim.Value)
                .FirstOrDefault(claimValue => claimValue.StartsWith(prefix, StringComparison.Ordinal));
            return value is null ? string.Empty : value[prefix.Length..];
        }
    }

    public bool IsAdministrator => string.Equals(Role, "administrator", StringComparison.Ordinal);

    public bool CanAccessDepartment(long departmentId) =>
        departmentId > 0 && AllowedDepartmentIds.Contains(departmentId);

    private long ReadPreferredDepartmentCookie()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null ||
            !request.Cookies.TryGetValue(LocalSettings.CookieName, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        try
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<LocalSettings>(
                Uri.UnescapeDataString(value),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return settings?.StoreDepartmentId ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
