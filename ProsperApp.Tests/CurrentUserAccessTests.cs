using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ProsperApp.Features.Settings;
using ProsperApp.Services;

namespace ProsperApp.Tests;

public sealed class CurrentUserAccessTests
{
    [Fact]
    public void TamperedDepartmentCookieFallsBackToAuthorizedDefault()
    {
        var context = CreateContext("administrator", [1], defaultDepartment: 1, cookieDepartment: 999);
        var access = new CurrentUserAccess(new HttpContextAccessor { HttpContext = context });
        var settings = new LocalSettingsProvider(
            new HttpContextAccessor { HttpContext = context },
            access).GetCurrent();

        Assert.Equal(1, access.CurrentDepartmentId);
        Assert.Equal(1, settings.StoreDepartmentId);
        Assert.False(access.CanAccessDepartment(999));
    }

    [Fact]
    public void AuthorizedPreferredDepartmentIsAccepted()
    {
        var context = CreateContext("operator", [1, 2], defaultDepartment: 1, cookieDepartment: 2);
        var access = new CurrentUserAccess(new HttpContextAccessor { HttpContext = context });
        Assert.Equal(2, access.CurrentDepartmentId);
        Assert.False(access.IsAdministrator);
    }

    [Fact]
    public void AdminModeRequiresAdministratorClaimAndExplicitSessionToggle()
    {
        var context = CreateContext("administrator", [1], 1, 1);
        context.Session = new DictionarySession();
        var accessor = new HttpContextAccessor { HttpContext = context };
        var service = new AdminModeService(accessor, new CurrentUserAccess(accessor));

        Assert.False(service.IsEnabled);
        service.SetEnabled(true);
        Assert.True(service.IsEnabled);
        service.SetEnabled(false);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void OperatorCannotForgeAdminModeThroughSession()
    {
        var context = CreateContext("operator", [1], 1, 1);
        context.Session = new DictionarySession();
        context.Session.SetString("ProsperApp.AdminMode", "1");
        var accessor = new HttpContextAccessor { HttpContext = context };
        var service = new AdminModeService(accessor, new CurrentUserAccess(accessor));

        Assert.False(service.IsEnabled);
        service.SetEnabled(true);
        Assert.False(service.IsEnabled);
    }

    private static DefaultHttpContext CreateContext(
        string role,
        long[] departments,
        long defaultDepartment,
        long cookieDepartment)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "user@example.com"),
            new(ProsperAccessClaims.GoogleSubject, "google-subject"),
            new(ProsperAccessClaims.DefaultDepartment, defaultDepartment.ToString())
        };
        claims.AddRange(departments.Select(value => new Claim(ProsperAccessClaims.Department, value.ToString())));
        claims.AddRange(departments.Select(value =>
            new Claim(ProsperAccessClaims.DepartmentRole, $"{value}:{role}")));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        var json = JsonSerializer.Serialize(new LocalSettings { StoreDepartmentId = cookieDepartment },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        context.Request.Headers.Cookie = $"{LocalSettings.CookieName}={Uri.EscapeDataString(json)}";
        return context;
    }

    private sealed class DictionarySession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = [];
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}
