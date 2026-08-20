using Ben.Web.Services.WebApi;
using Ben.Web.Services;
using Ben.Web.Website.Components;
using Ben.Web.Services.Help;
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
    // Item #70 phase 173: show the "Native acceleration" panel so a user who has installed the
    // sidecar can pair with it. Opt-in twice over — this only makes the editor probe the user's
    // own loopback ports and render the pairing panel; nothing is routed anywhere until they
    // paste that sidecar's one-time code. With no sidecar installed the probe finds nothing and
    // every path stays on ffmpeg.wasm exactly as before.
    options.NativeSidecar   = true;
    options.MediaLibraryBaseUrl = builder.Configuration["WebApi:BaseUrl"];
    // Turns on SharedCatalogAssetProvider, which has existed in the editor since phase 49 but
    // stayed dark because nothing served GET /api/video-assets. That endpoint is now real, so
    // the shared clipart library shows up in the editor's asset browser. The catalog's read
    // endpoints are anonymous by design — this named HttpClient carries no auth handler.
    options.AssetCatalogUrl = builder.Configuration["WebApi:BaseUrl"];
    options.DocumentPostUrl = $"{builder.Configuration["WebApi:BaseUrl"]}/api/video-projects";
});
// Override the default HttpMediaLibraryProvider with one that injects the bearer token.
builder.Services.AddScoped<Ben.Video.Editor.Services.IMediaLibraryProvider, BenMediaLibraryProvider>();
// BenMediaLibraryProvider answers the scope question too (item 91). The editor's own registration
// resolves IMediaLibraryScopeSource by casting whatever IMediaLibraryProvider is registered, so
// this line is what makes that cast land on the site's provider rather than the editor's default.
builder.Services.AddScoped<Ben.Video.Editor.Services.IMediaLibraryScopeSource>(sp =>
    (Ben.Video.Editor.Services.IMediaLibraryScopeSource)
        sp.GetRequiredService<Ben.Video.Editor.Services.IMediaLibraryProvider>());
// Handles VideoEditor.OnPublishExport — sends a finished render to the server, saving the project
// first when it has never been saved (the publish endpoint attaches to an existing project row).
builder.Services.AddScoped<VideoExportPublisher>();
// Records a sidecar pairing against the signed-in account, using the circuit's token. The WASM
// host registers its own implementation; the editor calls whichever it finds, or none.
builder.Services.AddScoped<Ben.Video.Editor.Services.ISidecarPairingReporter,
    Ben.Web.Services.SidecarPairingReporter>();
builder.Services.Configure<WebApiOptions>(builder.Configuration.GetSection("WebApi"));
// The site's own name and origin, in one place — see SiteIdentity. Used by the footer, page titles
// and the link previews that carry a shared URL into a chat window.
builder.Services.Configure<Ben.Data.Common.SiteIdentity>(builder.Configuration.GetSection("SiteIdentity"));
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
// One set of unread counts per circuit, shared by every badge on the page. Scoped, so it is torn
// down with the circuit — the poll it owns must not outlive the session it polls for.
builder.Services.AddScoped<NotificationState>();
// Scoped = per circuit. Avatar resolution depends on who is asking, so this must not be shared
// across sessions — see AvatarCache.
builder.Services.AddScoped<AvatarCache>();

// Toast queue rendered by BenToastHost in the layout. Scoped for the same reason as the two
// above: a toast raised for one session must never surface in another.
builder.Services.AddScoped<Ben.Web.Website.Library.Kit.BenToastService>();

// Help documents are embedded, immutable between deployments and identical for every reader, so
// one parse for the whole process is right. Who may *see* which document is per-circuit, and lives
// in the resolver instead.
// Singleton: which sections are on is a property of the site, not of the visitor, so one
// snapshot serves every circuit. It refreshes itself behind readers and falls back to the
// declared defaults, so the navigation and the route guards can answer synchronously during the
// first render — which is what lets a switched-off section refuse its URL instead of drawing and
// then hiding itself.
builder.Services.AddSingleton<SiteFeaturesProvider>();

builder.Services.AddSingleton<HelpContentService>();
builder.Services.AddScoped<HelpViewerResolver>();

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

// A route that does not exist renders nothing at all otherwise — the Router's <NotFound> block
// only covers client-side routing in a Blazor Web App, so a server-rendered miss returned an
// empty 404 body. Re-executing into a real page is what makes a missing route look missing.
app.UseStatusCodePagesWithReExecute("/not-found");

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
    .AddAdditionalAssemblies(typeof(Ben.Web.Website.Library.LibraryAssemblyMarker).Assembly);

app.Run();
