using Ben.Data.Common;
using Microsoft.AspNetCore.HttpOverrides;
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
// Its feed sibling (item 186 F7): the editor's "Post to the feed" destination.
builder.Services.AddScoped<FeedExportPublisher>();
// Records a sidecar pairing against the signed-in account, using the circuit's token. The WASM
// host registers its own implementation; the editor calls whichever it finds, or none.
builder.Services.AddScoped<Ben.Video.Editor.Services.ISidecarPairingReporter,
    Ben.Web.Services.SidecarPairingReporter>();
builder.Services.Configure<WebApiOptions>(builder.Configuration.GetSection("WebApi"));
// The site's own name and origin, in one place — see SiteIdentity. Used by the footer, page titles
// and the link previews that carry a shared URL into a chat window.
builder.Services.Configure<Ben.Data.Common.SiteIdentity>(builder.Configuration.GetSection("SiteIdentity"));
builder.Services.AddScoped<IWebApiTokenStore, WebApiTokenStore>();
// Backs both ticket services. Singleton and in-memory: a restart empties it, which costs nothing
// this app had — a Blazor Server restart has already destroyed every circuit, so the pages holding
// those URLs are gone anyway (item 201).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Ben.Web.Website.Services.BrowserTicketStore>();
builder.Services.AddSingleton<Ben.Web.Website.Services.MediaTicketService>();
builder.Services.AddSingleton<Ben.Web.Website.Services.UploadTicketService>();
builder.Services.AddScoped<Ben.Web.Services.IMediaUrlBuilder, Ben.Web.Website.Services.MediaUrlBuilder>();
// ApiBasePathHandler is what keeps "/webapi" attached. Every call site writes its path with a
// leading slash, which BaseAddress treats as root-relative and so discards the base path - see the
// handler for the full story. Harmless when the API is at an origin root, as it is in development.
builder.Services.AddHttpClient<IWebApiIdentityClient, WebApiIdentityClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
}).AddHttpMessageHandler(sp =>
    new ApiBasePathHandler(sp.GetRequiredService<IOptions<WebApiOptions>>().Value.BaseUrl));

builder.Services.AddHttpClient<IWebApiClient, WebApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
}).AddHttpMessageHandler(sp =>
    new ApiBasePathHandler(sp.GetRequiredService<IOptions<WebApiOptions>>().Value.BaseUrl));
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

// A year, not the 30-day default, and every subdomain: www serves the same site over TLS and
// nothing else answers under ishaunted.com. Not preloaded - that is a one-way door into the
// browser lists and a decision to take on its own, once the www redirect is settled.
builder.Services.AddHsts(hsts =>
{
    hsts.MaxAge            = TimeSpan.FromDays(365);
    hsts.IncludeSubDomains = true;
});

var azureAd = builder.Configuration.GetSection("AzureAd");
// One rule, shared with Ben.Data.WebApi, so the two hosts cannot disagree about whether Entra is
// configured - see EntraConfig for what went wrong when they each had their own.
bool entraEnabled = EntraConfig.IsConfigured(azureAd["ClientId"]);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, cookie =>
    {
        cookie.Cookie.SameSite = SameSiteMode.None;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

var tenantId = EntraConfig.TenantOrCommon(azureAd["TenantId"]);
bool multiTenant = EntraConfig.IsMultiTenant(tenantId);

if (entraEnabled)
{
    builder.Services.AddAuthentication()
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, oidc =>
        {
            oidc.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
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
                // ValidateIssuer has to be false on the multi-tenant authorities: every user's
                // token carries their own tenant's issuer URL, not the /common URL used during
                // discovery, so there is no single value to check against.
                //
                // Pointed at one tenant there is, and leaving this off would be a real hole -
                // a token minted in ANY Microsoft tenant would satisfy the rest of the checks.
                // ValidIssuer is deliberately not set: the handler then takes the issuer from
                // the authority's own discovery document, which is correct whether TenantId is
                // written as a GUID or as a domain name (the token always says GUID).
                ValidateIssuer = !multiTenant,
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

// ── Behind a reverse proxy ──────────────────────────────────────────────────
//
// Must run BEFORE anything that inspects the scheme — UseHttpsRedirection below is the one that
// matters. A proxy (Cloudflare Tunnel today, Azure App Service later) terminates TLS and forwards
// to this app over plain HTTP, so without this the app sees IsHttps == false, issues its
// 307 to https://, the proxy fetches that, and the request loops. The site looks completely
// broken with nothing wrong in IIS.
//
// Only X-Forwarded-Proto and X-Forwarded-For are honoured. Both are trivially spoofable by a
// client, so ASP.NET Core trusts them **only from loopback** by default and that default is left
// alone deliberately: cloudflared runs on this machine and connects to localhost, so the immediate
// peer genuinely is loopback. Widening KnownProxies/KnownNetworks would let any caller claim to
// have arrived over HTTPS from any address.
//
// X-Forwarded-For also restores the real client IP, which the audit log would otherwise record as
// the proxy for every single request.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
});

// ── Security response headers ───────────────────────────────────────────────
// Two headers, both cheap, both closing a real gap the deploy audit named:
//
//   X-Content-Type-Options: nosniff — stops a browser second-guessing a declared content type.
//     This site serves user-uploaded files, and a browser that decides an uploaded "image" is
//     really HTML will run it as HTML on this origin. The upload paths already refuse SVG for
//     exactly that reason; this is the same defence applied to everything at once.
//
//   Referrer-Policy: strict-origin-when-cross-origin — the full URL stops travelling to other
//     sites in the Referer header. Paths here carry case, investigation, place and organization
//     ids, so a link out to a map or an evidence source was quietly handing that address to a
//     third party. Same-origin navigation keeps the full path, so nothing internal changes.
//
// Deliberately NOT a Content-Security-Policy: this site loads Telerik, wavesurfer, mapping and
// inline Blazor bootstrap script, and a CSP written blind would either be so loose it means
// nothing or would break the editor in ways only found in production. It is worth doing
// properly, on its own, with the browser console open.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Nothing on this site is meant to live inside another site's frame, and nothing here frames
    // itself (no <iframe> anywhere in the Razor). Both headers, because older browsers read only
    // the first and everything current reads the second. This is the whole of the site's CSP on
    // purpose - see the note above about why a full policy is a separate piece of work.
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    await next();
});

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

// ── /build-info.json - which build is actually on disk ───────────────────────
//
// Read from disk PER REQUEST, deliberately: this is what the deploy script's smoke check asks
// for to prove the copy phase really happened, so a value cached at startup would answer for the
// process rather than for the files and defeat the point.
//
// It needs its own endpoint because MapStaticAssets serves only what the build-time manifest
// (Ben.Web.Website.staticwebassets.endpoints.json) lists, and the deploy script stamps this file
// into wwwroot AFTER publish - so the framework 404s it while it sits there on disk. That 404 is
// exactly what the first identity-checked deploy hit on 2026-08-27.
app.MapGet("/build-info.json", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "wwwroot", "build-info.json");
    return File.Exists(path)
        ? Results.Text(File.ReadAllText(path), "application/json")
        : Results.NotFound();
}).AllowAnonymous();


// ── Universal links and the web manifest (item 209) ──────────────────────────
//
// Both are endpoints rather than files in wwwroot, for the same two reasons: the association file
// has NO EXTENSION so static middleware has no content type for it, and both must be served with
// an exact type — iOS refuses an association file that is not application/json, over HTTPS, with
// no redirect in front of it. Building them here also means the app identifier and the site name
// come from configuration instead of from a JSON blob nothing validates.
//
// The path is the modern one. iOS 9 looked in the site root; every version since checks
// /.well-known/ first, and serving only the well-known copy is what Apple documents today.
app.MapGet("/.well-known/apple-app-site-association", (IConfiguration config) =>
{
    // No default. An association file naming the wrong team would be worse than none: it claims
    // links for an app that cannot open them, and the failure appears only on somebody's phone.
    var appId = config["Apple:AppLinks:AppId"];
    if (string.IsNullOrWhiteSpace(appId)) return Results.NotFound();

    return Results.Json(
        Ben.Web.Website.Services.AppleAppSiteAssociation.For(appId),
        contentType: "application/json");
}).AllowAnonymous();

app.MapGet("/manifest.webmanifest", (
    Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site) =>
    Results.Json(
        Ben.Web.Website.Services.WebAppManifest.For(site.Value),
        contentType: "application/manifest+json"))
    .AllowAnonymous();


// ── /go/{adId} — the counted door on a promoted card (item 186 F8) ───────────
// A minimal endpoint, not a Blazor page: a redirect must not stand up a circuit. The API counts
// the click and answers where the card leads; the redirect renders from NOTHING but the two
// fields of that closed-set answer. Any failure lands on /find — a stale ad in an old tab is a
// person to deliver somewhere honest, never a dead end (item 149's rule).
app.MapGet("/go/{adId:guid}", async (
    Guid adId, IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct) =>
{
    try
    {
        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        using var response = await http.PostAsync(
            $"{config["WebApi:BaseUrl"]}/api/public/promoted-groups/{adId}/click", null, ct);
        if (response.IsSuccessStatusCode)
        {
            var target = await response.Content
                .ReadFromJsonAsync<Ben.Service.Models.Entities.PromotedClickTarget>(cancellationToken: ct);
            if (target is not null && target.TargetKind == "org"
                && !string.IsNullOrWhiteSpace(target.OrganizationUrlName))
                return Results.Redirect($"/o/{Uri.EscapeDataString(target.OrganizationUrlName)}");
        }
    }
    catch (Exception)
    {
        // The counter is garnish on the navigation, not the other way round.
    }
    return Results.Redirect("/find");
});

// ── Media, streamed ───────────────────────────────────────────────────────────
//
// The browser fetches a file's picture or bytes THROUGH here, and this process never holds the
// file: the API's response is copied straight to the client as it arrives. What this replaced
// fetched whole files into memory and base64'd them into the page, which took the site to
// sixteen gigabytes on a media library and got it killed.
//
// The ticket carries WHO is asking (see MediaTicketService). The API remains the authority on
// what they may see — this endpoint asserts nothing, it only forwards a bearer token — so the
// audience rules cannot drift apart from the ones the API already enforces.
// A session's recordings are gated on the investigation, not on the file's own audience, so they
// come from the field-session endpoint. Same ticket, same streaming, different upstream path.
app.MapGet("/media/field-sessions/{sessionId:guid}/files/{fileId:guid}", async (
    Guid sessionId, Guid fileId, string? t,
    Ben.Web.Website.Services.MediaTicketService tickets,
    IHttpClientFactory httpFactory, IConfiguration config,
    HttpContext ctx, CancellationToken ct) =>
{
    var accessToken = string.IsNullOrWhiteSpace(t) ? null : tickets.Unprotect(fileId, t);
    return await Ben.Web.Website.Services.MediaProxy.StreamAsync(
        $"{config["WebApi:BaseUrl"]}/api/field-sessions/{sessionId}/files/{fileId}",
        accessToken, httpFactory, ctx, ct);
});

// A recording reached through a share link (item 207). No ticket and no bearer token: the share
// token IS the authority, and the API re-checks its expiry, its revocation and which file it
// covers on every request. This endpoint asserts nothing — it forwards a path and streams the
// answer — which is what keeps the rule in one place instead of two that can drift apart.
app.MapGet("/media/shared/{token}/files/{fileId:guid}", async (
    string token, Guid fileId,
    IHttpClientFactory httpFactory, IConfiguration config,
    HttpContext ctx, CancellationToken ct) =>
{
    return await Ben.Web.Website.Services.MediaProxy.StreamAsync(
        $"{config["WebApi:BaseUrl"]}/api/shared-sessions/{Uri.EscapeDataString(token)}/files/{fileId}",
        accessToken: null, httpFactory, ctx, ct);
}).AllowAnonymous();

app.MapGet("/media/{fileId:guid}/{kind}", async (
    Guid fileId, string kind, string? t,
    Ben.Web.Website.Services.MediaTicketService tickets,
    IHttpClientFactory httpFactory, IConfiguration config,
    HttpContext ctx, CancellationToken ct) =>
{
    if (kind is not ("thumbnail" or "download")) return Results.NotFound();

    var accessToken = string.IsNullOrWhiteSpace(t) ? null : tickets.Unprotect(fileId, t);
    return await Ben.Web.Website.Services.MediaProxy.StreamAsync(
        $"{config["WebApi:BaseUrl"]}/api/upload-files/{fileId}/{kind}",
        accessToken, httpFactory, ctx, ct);
});

// ── Chunked upload relays ────────────────────────────────────────────────────
//
// The browser PUTs chunks HERE, not to the API: page JavaScript holds no bearer token (and must
// not), so the circuit mints an UploadTicket bound to the session and the relay speaks to the API
// with the token inside it — the same trust shape as the media endpoints above, in the opposite
// direction. Each relay streams the body straight through; the file never lands in this process.
// The framework's request-size ceiling is off on the chunk PUT because the real ceiling is the
// API's configurable chunk limit — these chunks are also what keeps every request under
// Cloudflare's 100 MB, which is the reason chunked uploads exist at all.

app.MapPut("/uploads/chunked/{sessionId:guid}/chunks/{index:int}", async (
    Guid sessionId, int index, string? t,
    Ben.Web.Website.Services.UploadTicketService tickets,
    IHttpClientFactory httpFactory, IConfiguration config,
    HttpContext ctx, CancellationToken ct) =>
{
    var accessToken = string.IsNullOrWhiteSpace(t) ? null : tickets.Unprotect(sessionId, t);
    if (accessToken is null) return Results.Unauthorized();

    ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()!
        .MaxRequestBodySize = null;

    using var http = httpFactory.CreateClient();
    http.Timeout = TimeSpan.FromMinutes(30);   // one chunk on a slow home upstream

    using var request = new HttpRequestMessage(
        HttpMethod.Put,
        $"{config["WebApi:BaseUrl"]}/api/chunked-uploads/{sessionId}/chunks/{index}");
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    request.Content = new StreamContent(ctx.Request.Body);
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

    return await Ben.Web.Website.Services.UploadRelay.ForwardAsync(http, request, ct);
});

app.MapGet("/uploads/chunked/{sessionId:guid}", async (
    Guid sessionId, string? t,
    Ben.Web.Website.Services.UploadTicketService tickets,
    IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct) =>
{
    var accessToken = string.IsNullOrWhiteSpace(t) ? null : tickets.Unprotect(sessionId, t);
    if (accessToken is null) return Results.Unauthorized();

    using var http = httpFactory.CreateClient();
    using var request = new HttpRequestMessage(
        HttpMethod.Get, $"{config["WebApi:BaseUrl"]}/api/chunked-uploads/{sessionId}");
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    return await Ben.Web.Website.Services.UploadRelay.ForwardAsync(http, request, ct);
});

app.MapPost("/uploads/chunked/{sessionId:guid}/complete", async (
    Guid sessionId, string? t,
    Ben.Web.Website.Services.UploadTicketService tickets,
    IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct) =>
{
    var accessToken = string.IsNullOrWhiteSpace(t) ? null : tickets.Unprotect(sessionId, t);
    if (accessToken is null) return Results.Unauthorized();

    using var http = httpFactory.CreateClient();
    http.Timeout = TimeSpan.FromMinutes(30);   // assembly of a large file is a slow disk copy

    using var request = new HttpRequestMessage(
        HttpMethod.Post, $"{config["WebApi:BaseUrl"]}/api/chunked-uploads/{sessionId}/complete");
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    return await Ben.Web.Website.Services.UploadRelay.ForwardAsync(http, request, ct);
});

app.MapDelete("/uploads/chunked/{sessionId:guid}", async (
    Guid sessionId, string? t,
    Ben.Web.Website.Services.UploadTicketService tickets,
    IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct) =>
{
    var accessToken = string.IsNullOrWhiteSpace(t) ? null : tickets.Unprotect(sessionId, t);
    if (accessToken is null) return Results.Unauthorized();

    using var http = httpFactory.CreateClient();
    using var request = new HttpRequestMessage(
        HttpMethod.Delete, $"{config["WebApi:BaseUrl"]}/api/chunked-uploads/{sessionId}");
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    return await Ben.Web.Website.Services.UploadRelay.ForwardAsync(http, request, ct);
});

// The classic-upload relay: the same browser-side JS path, for the files chunking refuses (an
// SVG is sanitised as a whole document) or doesn't help (anything small). The nonce is a random
// id the circuit minted purely to bind the ticket to — one ticket, one upload gesture. The
// multipart body streams through untouched, boundary and all.
app.MapPost("/uploads/classic/{nonce:guid}", async (
    Guid nonce, string? t,
    Ben.Web.Website.Services.UploadTicketService tickets,
    IHttpClientFactory httpFactory, IConfiguration config,
    HttpContext ctx, CancellationToken ct) =>
{
    var accessToken = string.IsNullOrWhiteSpace(t) ? null : tickets.Unprotect(nonce, t);
    if (accessToken is null) return Results.Unauthorized();

    using var http = httpFactory.CreateClient();
    http.Timeout = TimeSpan.FromMinutes(30);

    using var request = new HttpRequestMessage(
        HttpMethod.Post, $"{config["WebApi:BaseUrl"]}/api/upload-files");
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    request.Content = new StreamContent(ctx.Request.Body);
    if (ctx.Request.ContentType is { } contentType)
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);

    return await Ben.Web.Website.Services.UploadRelay.ForwardAsync(http, request, ct);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Ben.Web.Website.Library.LibraryAssemblyMarker).Assembly);

app.Run();
