using Ben.Web.WebApp.Services.WebApi;
using Ben.Web.WebApp.Services;
using Ben.Web.WebApp.Components;
using Ben.Web.Library.Services;
using Ben.Video.Editor.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using System.Text.Json;
using System.Text.Json.Serialization;


var builder = WebApplication.CreateBuilder(args);

/* LOGGING */

// NOTE:
// Ensure repository project receives ILoggerFactory via DI; Serilog integrates automatically. Use Errors minimum level to limit rows.

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

/* END LOGGING */

builder.Services.AddTelerikBlazor();
builder.Services.AddBenVideoEditor(options =>
{
    options.MultiTrack      = true;
    options.AudioTracks     = true;
    options.Transitions     = true;
    options.TextOverlays    = true;
    options.VideoEffects    = true;
    options.MediaLibrary    = true;
    options.ProjectPersistence = true;
    options.ErrorLog        = true;
    options.RippleEdit      = true;
    // Item #36 phase E rollout: background render worker + rough/fine two-pass preview.
    options.BackgroundRendering = true;
    options.MediaLibraryBaseUrl = builder.Configuration["WebApi:BaseUrl"];
    options.DocumentPostUrl = $"{builder.Configuration["WebApi:BaseUrl"]}/api/video-projects";
});
// Override the default HttpMediaLibraryProvider with one that injects the bearer token.
builder.Services.AddScoped<Ben.Video.Editor.Services.IMediaLibraryProvider, BenMediaLibraryProvider>();
builder.Services.Configure<WebApiOptions>(builder.Configuration.GetSection("WebApi"));
builder.Services.AddScoped<IWebApiTokenStore, WebApiTokenStore>();
builder.Services.AddHttpClient<IWebApiIdentityClient, WebApiIdentityClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddHttpClient<IWebApiClient, WebApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddScoped<IWebApiAuthService, WebApiAuthService>();
builder.Services.AddScoped<IBenAdminClient, BenAdminClientAdapter>();
builder.Services.AddScoped<IBenUserState>(sp => (IBenUserState)sp.GetRequiredService<IWebApiTokenStore>());

// ── Microsoft Entra OIDC ─────────────────────────────────────────────────────
// EntraTokenHolder is populated by middleware before the Blazor circuit starts
// so that the access token is available to components after the circuit is up.
builder.Services.AddScoped<EntraTokenHolder>();

var azureAd = builder.Configuration.GetSection("AzureAd");
bool entraEnabled = !string.IsNullOrWhiteSpace(azureAd["ClientId"])
                    && azureAd["ClientId"] != "YOUR_WEBAPP_CLIENT_ID";

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, cookie =>
    {
        cookie.Cookie.SameSite = SameSiteMode.None;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

if (entraEnabled)
{
    builder.Services.AddAuthentication()
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, oidc =>
        {
            oidc.Authority = $"https://login.microsoftonline.com/{azureAd["TenantId"]}/v2.0";
            oidc.ClientId = azureAd["ClientId"];
            oidc.ClientSecret = azureAd["ClientSecret"];
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            oidc.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            oidc.SaveTokens = true;         // tokens stored in the auth cookie
            oidc.Scope.Add("offline_access");
            oidc.Scope.Add("openid");
            oidc.Scope.Add("profile");
            oidc.Scope.Add("email");

            // Access token for the WebApi
            var apiScope = builder.Configuration["DownstreamApis:BenWebApi:Scope"];
            if (!string.IsNullOrEmpty(apiScope))
                oidc.Scope.Add(apiScope);

            oidc.TokenValidationParameters = new TokenValidationParameters
            {
                // ValidateIssuer must be false when TenantId = "common".
                // Each user's token carries their own tenant-specific issuer URL,
                // not the /common endpoint URL used during discovery.
                ValidateIssuer = false,
                NameClaimType = "preferred_username",
            };

            // On HTTP (localhost dev), the browser won't send Secure cookies back
            // on the callback, causing "Correlation failed". Set SameSite=Unspecified
            // so cookies work over plain HTTP.
            oidc.CorrelationCookie.SameSite = SameSiteMode.Unspecified;
            oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            oidc.NonceCookie.SameSite = SameSiteMode.Unspecified;
            oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
}

// Add services to the container.
builder.Services.AddAuthorization();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// ── Entra token capture middleware ──────────────────────────────────────────
// Runs on the initial HTTP request (before the Blazor SignalR circuit starts).
// Captures the Entra access token into EntraTokenHolder so Blazor components
// can read it throughout the circuit lifetime without touching HttpContext.
// NOTE: In the WebApp, the only way user.Identity.IsAuthenticated is true at the
// HTTP level is via the OIDC cookie — local Identity auth uses bearer tokens
// stored in the Blazor circuit, not HTTP-level cookies.
app.Use(async (context, next) =>
{
    var user = context.User;
    if (user.Identity?.IsAuthenticated == true)
    {
        var holder = context.RequestServices.GetRequiredService<EntraTokenHolder>();
        holder.AccessToken = await context.GetTokenAsync("access_token");
        holder.Email = user.FindFirst("preferred_username")?.Value
                       ?? user.FindFirst("email")?.Value;
        holder.EntraOid = user.FindFirst("oid")?.Value
                          ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        holder.IsEntraAuthenticated = holder.AccessToken is not null;
    }
    await next();
});

// ── Entra sign-in endpoint (triggers OIDC challenge from Blazor forceLoad) ──
app.MapGet("/auth/entra-signin", async (HttpContext ctx) =>
{
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = "/"
        });
}).AllowAnonymous();

// ── Entra sign-out endpoint ──────────────────────────────────────────────────
app.MapGet("/auth/entra-signout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = "/login"
        });
}).AllowAnonymous();

app.MapStaticAssets();
app.UseAntiforgery();


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Ben.Web.Library.SuperAdmin.LibraryAssemblyMarker).Assembly);

app.Run();
