using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.Options;
using ProsperApp.Endpoints;
using ProsperApp.Features.BusinessHome;
using ProsperApp.Features.Attendance;
using ProsperApp.Features.Orders;
using ProsperApp.Features.Closing;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Features.Synchronization;
using ProsperApp.Infrastructure.Caching;
using ProsperApp.Infrastructure.Security;
using ProsperApp.Options;
using ProsperApp.Services;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection("Supabase"));
builder.Services.Configure<GoogleDriveOptions>(builder.Configuration.GetSection("GoogleDrive"));
builder.Services.Configure<ReceiptPrinterOptions>(builder.Configuration.GetSection("ReceiptPrinter"));
builder.Services.Configure<ReviewAuthOptions>(builder.Configuration.GetSection("ReviewAuth"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ApplicationMemoryCache>();
builder.Services.AddSingleton<IApplicationCache>(
    services => services.GetRequiredService<ApplicationMemoryCache>());
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFeatureGate, FeatureGate>();
builder.Services.AddSingleton<IStoreClock, StoreClock>();
builder.Services.AddScoped<ICurrentUserAccess, CurrentUserAccess>();
builder.Services.AddScoped<IAdminModeService, AdminModeService>();
builder.Services.AddScoped<IOrderEntryApplicationService, OrderEntryApplicationService>();
builder.Services.AddScoped<ILocalSettingsProvider, LocalSettingsProvider>();
builder.Services.AddScoped<IBusinessHomeApplicationService, BusinessHomeApplicationService>();
builder.Services.AddScoped<IAttendanceApplicationService, AttendanceApplicationService>();
builder.Services.AddScoped<IClosingApplicationService, ClosingApplicationService>();
builder.Services.AddScoped<IDailyReportApplicationService, DailyReportApplicationService>();
builder.Services.AddScoped<ISalesHistoryApplicationService, SalesHistoryApplicationService>();
builder.Services.AddScoped<IStoreMasterBootstrapper, SupabaseStoreMasterBootstrapper>();
builder.Services.AddScoped<IManagementMasterSynchronization, SupabaseManagementMasterSynchronization>();
builder.Services.AddHttpClient<ISupabaseRpcClient, SupabaseRpcClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<IGoogleDriveAuthService, GoogleDriveAuthService>();
builder.Services.AddScoped<IStoreSettingsRepository, SupabaseStoreSettingsRepository>();
builder.Services.AddScoped<IReceiptRepository, SupabaseReceiptRepository>();
builder.Services.AddScoped<IDailyReportRepository, SupabaseDailyReportRepository>();
builder.Services.AddScoped<ISalesHistoryRepository, SupabaseSalesHistoryRepository>();
builder.Services.AddScoped<IBusinessDayRepository, SupabaseBusinessDayRepository>();
builder.Services.AddScoped<ICastSalesAdjustmentRepository, SupabaseCastSalesAdjustmentRepository>();
builder.Services.AddScoped<IDrinkBackRepository, SupabaseDrinkBackRepository>();
builder.Services.AddScoped<IStoreSlipRepository, SupabaseStoreSlipRepository>();
builder.Services.AddScoped<IStoreOrderRepository, SupabaseStoreOrderRepository>();
builder.Services.AddScoped<ICheckoutRepository, SupabaseCheckoutRepository>();
builder.Services.AddHttpClient<IDriveFileService, GoogleDriveFileService>();

var googleDriveOptions = builder.Configuration.GetSection("GoogleDrive").Get<GoogleDriveOptions>() ?? new();
var googleAuthConfigured = !string.IsNullOrWhiteSpace(googleDriveOptions.ClientId) &&
                           !string.IsNullOrWhiteSpace(googleDriveOptions.ClientSecret);

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = googleAuthConfigured
            ? GoogleDefaults.AuthenticationScheme
            : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "ProsperApp.Auth.v2";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(90);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = context =>
        {
            var driveOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<GoogleDriveOptions>>()
                .Value;
            var reviewAuthOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<ReviewAuthOptions>>()
                .Value;
            var isReviewAuth = reviewAuthOptions.IsEnabled &&
                               context.Principal?.HasClaim(
                                   ReviewAuthOptions.ClaimType,
                                   ReviewAuthOptions.ClaimValue) == true &&
                               context.Principal.HasClaim(
                                   claim => claim.Type == ProsperAccessClaims.GoogleSubject &&
                                            claim.Value == reviewAuthOptions.Subject) &&
                               context.Principal.HasClaim(
                                   claim => claim.Type == ProsperAccessClaims.Department);
            if (isReviewAuth)
            {
                return Task.CompletedTask;
            }

            var configured = !string.IsNullOrWhiteSpace(driveOptions.ClientId) &&
                             !string.IsNullOrWhiteSpace(driveOptions.ClientSecret);
            var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
            var subject = context.Principal?.FindFirst(ProsperAccessClaims.GoogleSubject)?.Value;
            var departments = context.Principal?.FindAll(ProsperAccessClaims.Department).Any() == true;

            if (!configured || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(subject) || !departments)
            {
                context.RejectPrincipal();
                context.HttpContext.Session.Clear();
            }

            return Task.CompletedTask;
        };
    });

if (googleAuthConfigured)
{
    authBuilder
        .AddGoogle(options =>
        {
            options.ClientId = googleDriveOptions.ClientId;
            options.ClientSecret = googleDriveOptions.ClientSecret;
            options.SaveTokens = true;
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.AccessType = "offline";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Scope.Add("https://www.googleapis.com/auth/drive.readonly");

            options.Events.OnCreatingTicket = async context =>
            {
                var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                if (string.IsNullOrWhiteSpace(email) &&
                    context.User.TryGetProperty("email", out var emailProperty))
                {
                    email = emailProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(email) &&
                        context.Principal?.Identity is ClaimsIdentity identity &&
                        !identity.HasClaim(claim => claim.Type == ClaimTypes.Email))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Email, email));
                    }
                }

                var subject = context.User.TryGetProperty("sub", out var subjectProperty)
                    ? subjectProperty.GetString()
                    : context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var emailVerified =
                    (context.User.TryGetProperty("email_verified", out var emailVerifiedProperty) &&
                     emailVerifiedProperty.ValueKind == System.Text.Json.JsonValueKind.True) ||
                    (context.User.TryGetProperty("verified_email", out var verifiedEmailProperty) &&
                     verifiedEmailProperty.ValueKind == System.Text.Json.JsonValueKind.True);

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(subject) || !emailVerified)
                {
                    context.HttpContext.Items["GoogleAuthErrorMessage"] =
                        "このGoogleアカウントはこのアプリの利用を許可されていません。";
                    return;
                }

                var rpcClient = context.HttpContext.RequestServices.GetRequiredService<ISupabaseRpcClient>();
                var access = await rpcClient.BindUserAccessAsync(email, subject, true, context.HttpContext.RequestAborted);
                if (!access.Succeeded || context.Principal?.Identity is not ClaimsIdentity claimsIdentity)
                {
                    context.HttpContext.Items["GoogleAuthErrorMessage"] =
                        "このGoogleアカウントはこのアプリの利用を許可されていません。";
                    return;
                }

                claimsIdentity.AddClaim(new Claim(ProsperAccessClaims.GoogleSubject, subject));
                foreach (var row in access.Rows)
                {
                    if (!row.TryGetProperty("department_id", out var departmentProperty))
                    {
                        continue;
                    }

                    var departmentId = departmentProperty.ValueKind == System.Text.Json.JsonValueKind.String
                        ? departmentProperty.GetString()
                        : departmentProperty.GetRawText();
                    if (string.IsNullOrWhiteSpace(departmentId))
                    {
                        continue;
                    }

                    claimsIdentity.AddClaim(new Claim(ProsperAccessClaims.Department, departmentId));
                    if (row.TryGetProperty("is_default", out var defaultProperty) && defaultProperty.GetBoolean())
                    {
                        claimsIdentity.AddClaim(new Claim(ProsperAccessClaims.DefaultDepartment, departmentId));
                    }
                    if (row.TryGetProperty("access_role", out var roleProperty) &&
                        !string.IsNullOrWhiteSpace(roleProperty.GetString()))
                    {
                        claimsIdentity.AddClaim(new Claim(
                            ProsperAccessClaims.DepartmentRole,
                            $"{departmentId}:{roleProperty.GetString()}"));
                    }
                }

                if (!claimsIdentity.HasClaim(claim => claim.Type == ProsperAccessClaims.Department))
                {
                    context.HttpContext.Items["GoogleAuthErrorMessage"] =
                        "このGoogleアカウントはこのアプリの利用を許可されていません。";
                }
            };

            options.Events.OnTicketReceived = context =>
            {
                if (context.HttpContext.Items.TryGetValue("GoogleAuthErrorMessage", out var errorMessage) &&
                    errorMessage is string message)
                {
                    context.Response.Redirect($"/Login?error={Uri.EscapeDataString(message)}");
                    context.HandleResponse();
                }

                if (context.Properties is { } properties)
                {
                    properties.IsPersistent = true;
                    properties.AllowRefresh = true;
                    properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(90);
                }

                return Task.CompletedTask;
            };

            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                var message = "Google認証に失敗しました。";
                context.Response.Redirect($"/Login?error={Uri.EscapeDataString(message)}");
                return Task.CompletedTask;
            };
        });
}

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var nonce = CspNonce.Create();
    context.Items[CspNonce.HttpContextItemKey] = nonce;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.ContentSecurityPolicy =
            $"default-src 'self'; script-src 'self' 'nonce-{nonce}' https://www.sii-ps.com; " +
            "style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
            "connect-src 'self' ws://localhost:* wss://localhost:*; frame-src 'self'; " +
            "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "SAMEORIGIN";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        return Task.CompletedTask;
    });
    await next();
});

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapDrivePreviewEndpoints();
app.MapRazorPages()
   .WithStaticAssets()
   .RequireAuthorization();

app.MapGet("/review-login", async (
        HttpContext context,
        IOptions<ReviewAuthOptions> reviewAuthOptions,
        ISupabaseRpcClient rpcClient,
        string? token,
        string? returnUrl) =>
    {
        var options = reviewAuthOptions.Value;
        if (!options.IsEnabled)
        {
            return Results.NotFound();
        }

        if (!options.IsValidToken(token))
        {
            return Results.Unauthorized();
        }

        var access = await rpcClient.BindUserAccessAsync(
            options.Email,
            options.Subject,
            true,
            context.RequestAborted);
        if (!access.Succeeded || access.Rows.Count == 0)
        {
            return Results.Unauthorized();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, options.Email),
            new(ClaimTypes.Email, options.Email),
            new(ClaimTypes.Name, options.DisplayName),
            new(ProsperAccessClaims.GoogleSubject, options.Subject),
            new(ReviewAuthOptions.ClaimType, ReviewAuthOptions.ClaimValue)
        };

        foreach (var row in access.Rows)
        {
            if (!row.TryGetProperty("department_id", out var departmentProperty))
            {
                continue;
            }

            var departmentId = departmentProperty.ValueKind == System.Text.Json.JsonValueKind.String
                ? departmentProperty.GetString()
                : departmentProperty.GetRawText();
            if (string.IsNullOrWhiteSpace(departmentId))
            {
                continue;
            }

            claims.Add(new Claim(ProsperAccessClaims.Department, departmentId));
            if (row.TryGetProperty("is_default", out var defaultProperty) && defaultProperty.GetBoolean())
            {
                claims.Add(new Claim(ProsperAccessClaims.DefaultDepartment, departmentId));
            }

            if (row.TryGetProperty("access_role", out var roleProperty) &&
                !string.IsNullOrWhiteSpace(roleProperty.GetString()))
            {
                claims.Add(new Claim(
                    ProsperAccessClaims.DepartmentRole,
                    $"{departmentId}:{roleProperty.GetString()}"));
            }
        }

        if (!claims.Any(claim => claim.Type == ProsperAccessClaims.Department))
        {
            return Results.Unauthorized();
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = options.GetExpiresUtc()
            });

        return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/");
    });

app.MapGet("/logout", async (HttpContext context, string? returnUrl) =>
    {
        context.Session.Clear();
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/");
    })
    .RequireAuthorization();

app.Run();

static bool IsLocalReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return false;
    }

    return returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\')) ||
           returnUrl.Length > 1 && returnUrl[0] == '~' && returnUrl[1] == '/';
}
