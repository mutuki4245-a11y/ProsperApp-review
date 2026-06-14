using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.Options;
using ProsperApp.Endpoints;
using ProsperApp.Options;
using ProsperApp.Services;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection("Supabase"));
builder.Services.Configure<GoogleDriveOptions>(builder.Configuration.GetSection("GoogleDrive"));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("GoogleAuth"));
builder.Services.Configure<ReviewAuthOptions>(builder.Configuration.GetSection("ReviewAuth"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSession();
builder.Services.AddSingleton<IFeatureGate, FeatureGate>();
builder.Services.AddSingleton<IStoreClock, StoreClock>();
builder.Services.AddSingleton<IOrderQueueService, OrderQueueService>();
builder.Services.AddScoped<ILocalSettingsProvider, LocalSettingsProvider>();
builder.Services.AddHttpClient<ISupabaseRpcClient, SupabaseRpcClient>();
builder.Services.AddScoped<IGoogleDriveAuthService, GoogleDriveAuthService>();
builder.Services.AddScoped<IStoreSettingsRepository, SupabaseStoreSettingsRepository>();
builder.Services.AddScoped<IReceiptRepository, SupabaseReceiptRepository>();
builder.Services.AddScoped<IBusinessDayRepository, SupabaseBusinessDayRepository>();
builder.Services.AddScoped<IStoreSlipRepository, SupabaseStoreSlipRepository>();
builder.Services.AddScoped<IStoreOrderRepository, SupabaseStoreOrderRepository>();
builder.Services.AddScoped<ICheckoutRepository, SupabaseCheckoutRepository>();
builder.Services.AddScoped<IStoreItemAdminRepository, SupabaseStoreItemAdminRepository>();
builder.Services.AddScoped<IStoreCastAdminRepository, SupabaseStoreCastAdminRepository>();
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
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(3650);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = context =>
        {
            var authOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<GoogleAuthOptions>>()
                .Value;
            var driveOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<GoogleDriveOptions>>()
                .Value;
            var reviewAuthOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<ReviewAuthOptions>>()
                .Value;
            var isReviewAuth = reviewAuthOptions.IsEnabled &&
                               context.Principal?.HasClaim(ReviewAuthOptions.ClaimType, ReviewAuthOptions.ClaimValue) == true;
            if (isReviewAuth)
            {
                return Task.CompletedTask;
            }

            var configured = !string.IsNullOrWhiteSpace(driveOptions.ClientId) &&
                             !string.IsNullOrWhiteSpace(driveOptions.ClientSecret);
            var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;

            if (!configured || !authOptions.IsAllowed(email))
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

            foreach (var scope in googleDriveOptions.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
            {
                if (!options.Scope.Contains(scope))
                {
                    options.Scope.Add(scope);
                }
            }

            if (!options.Scope.Contains("email"))
            {
                options.Scope.Add("email");
            }

            options.Events.OnCreatingTicket = context =>
            {
                var authOptions = context.HttpContext.RequestServices
                    .GetRequiredService<IOptions<GoogleAuthOptions>>()
                    .Value;
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

                if (!authOptions.IsAllowed(email))
                {
                    context.HttpContext.Items["GoogleAuthErrorMessage"] =
                        "このGoogleアカウントはこのアプリの利用を許可されていません。";
                    return Task.CompletedTask;
                }

                var accessToken = context.AccessToken;
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    context.HttpContext.Session.SetString("GoogleDriveAccessToken", accessToken);
                }

                return Task.CompletedTask;
            };

            options.Events.OnTicketReceived = context =>
            {
                if (context.HttpContext.Items.TryGetValue("GoogleAuthErrorMessage", out var errorMessage) &&
                    errorMessage is string message)
                {
                    context.Response.Redirect($"/Login?error={Uri.EscapeDataString(message)}");
                    context.HandleResponse();
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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, options.Email),
            new(ClaimTypes.Email, options.Email),
            new(ClaimTypes.Name, options.DisplayName),
            new(ReviewAuthOptions.ClaimType, ReviewAuthOptions.ClaimValue)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
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
