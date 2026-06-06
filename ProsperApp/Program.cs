using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using ProsperApp.Options;
using ProsperApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection("Supabase"));
builder.Services.Configure<GoogleDriveOptions>(builder.Configuration.GetSection("GoogleDrive"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSession();
builder.Services.AddSingleton<IFeatureGate, FeatureGate>();
builder.Services.AddScoped<ILocalSettingsProvider, LocalSettingsProvider>();
builder.Services.AddHttpClient<IStoreSettingsRepository, SupabaseStoreSettingsRepository>();
builder.Services.AddHttpClient<IReceiptRepository, SupabaseReceiptRepository>();
builder.Services.AddHttpClient<IBusinessDayRepository, SupabaseBusinessDayRepository>();
builder.Services.AddHttpClient<IStoreSlipRepository, SupabaseStoreSlipRepository>();
builder.Services.AddHttpClient<IDriveFileService, GoogleDriveFileService>();

var googleDriveOptions = builder.Configuration.GetSection("GoogleDrive").Get<GoogleDriveOptions>() ?? new();
var googleAuthConfigured = !string.IsNullOrWhiteSpace(googleDriveOptions.ClientId) &&
                           !string.IsNullOrWhiteSpace(googleDriveOptions.ClientSecret);

if (googleAuthConfigured)
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddGoogle(options =>
        {
            options.ClientId = googleDriveOptions.ClientId;
            options.ClientSecret = googleDriveOptions.ClientSecret;
            options.SaveTokens = true;

            foreach (var scope in googleDriveOptions.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
            {
                options.Scope.Add(scope);
            }

            options.Events.OnCreatingTicket = context =>
            {
                var accessToken = context.AccessToken;
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    context.HttpContext.Session.SetString("GoogleDriveAccessToken", accessToken);
                }

                return Task.CompletedTask;
            };
        });
}
else
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie();
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
var razorPages = app.MapRazorPages()
   .WithStaticAssets();

if (googleAuthConfigured)
{
    razorPages.RequireAuthorization();
}

app.MapGet("/logout", async (HttpContext context, string? returnUrl) =>
    {
        context.Session.Clear();
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    })
    .RequireAuthorization();

app.Run();
