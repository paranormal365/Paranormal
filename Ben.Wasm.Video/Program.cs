using Ben.Video.Editor.Extensions;
using Ben.Wasm.Video;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The WebAssembly host for the Ben video editor.
//
// This exists because of where the code runs. The editor's media path — pull the user's
// already-uploaded files down, cache them in OPFS, edit, render the final video, upload the
// result — is written against HttpClient and OPFS, which under this host all execute in the
// browser: media streams straight from the WebApi to the user's machine and the render never
// leaves it. The same editor hosted under Blazor Server runs those very HttpClient calls on the
// server instead, so every clip is downloaded into server memory and then re-sent to the browser
// over the SignalR circuit — a full server round-trip per clip that this host exists to remove.
//
// Because the browser is the caller here, requests to the WebApi are cross-origin whenever the
// two are not served from one origin — which makes this project the reason the WebApi's CORS
// configuration is real rather than theoretical. Same-origin hosting (reverse proxy) remains the
// preferred deployment; CORS is the fallback for a split.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddTelerikBlazor();

// WebApi endpoints come from wwwroot/appsettings*.json — fetched by the browser at startup, so
// per-environment values are a static file swap, not a rebuild.
var cfg = builder.Configuration.GetSection("BenVideo");
var apiBaseUrl = cfg["WebApiBaseUrl"]?.TrimEnd('/');

builder.Services.AddBenVideoEditor(options =>
{
    // With no API configured the editor still runs fully local — file-picker imports, OPFS
    // persistence, in-browser render — which is also the safe state for a fresh checkout.
    if (string.IsNullOrEmpty(apiBaseUrl)) return;

    options.MediaLibraryBaseUrl = apiBaseUrl;
    options.AssetCatalogUrl     = apiBaseUrl;
    options.DocumentPostUrl     = $"{apiBaseUrl}/api/video-projects";

    // The native sidecar pairs against the user's own loopback, orthogonal to hosting model.
    options.NativeSidecar = true;
});

// ── Auth ─────────────────────────────────────────────────────────────────────
// The WASM counterpart of the server host's circuit-held tokens: the browser signs in against the
// WebApi's own /login (the same endpoint the site uses — no new server surface), keeps the tokens
// in TokenStore, and BearerTokenHandler attaches them to the editor's authenticated clients.
builder.Services.AddScoped<Ben.Wasm.Video.Services.TokenStore>();
builder.Services.AddScoped<Ben.Wasm.Video.Services.BearerTokenHandler>();
builder.Services.AddScoped(sp => new Ben.Wasm.Video.Services.AuthService(
    // Dedicated client pinned to the API origin, with NO bearer handler: /refresh must be
    // callable with an expired access token.
    new HttpClient { BaseAddress = new Uri(string.IsNullOrEmpty(apiBaseUrl)
        ? builder.HostEnvironment.BaseAddress : apiBaseUrl) },
    sp.GetRequiredService<Ben.Wasm.Video.Services.TokenStore>()));

// IHttpClientFactory merges repeated AddHttpClient(name) registrations, so the handler attaches
// to the editor's named clients without touching editor code. AssetCatalog is left alone on
// purpose — its read endpoints are anonymous by design.
builder.Services.AddHttpClient(Ben.Video.Editor.Extensions.ServiceCollectionExtensions.MediaLibraryHttpClientName)
    .AddHttpMessageHandler<Ben.Wasm.Video.Services.BearerTokenHandler>();
builder.Services.AddHttpClient(Ben.Video.Editor.Extensions.ServiceCollectionExtensions.ProjectPersistenceHttpClientName)
    .AddHttpMessageHandler<Ben.Wasm.Video.Services.BearerTokenHandler>();

// Records a successful sidecar pairing against the signed-in account, so the site can tell who is
// running a native sidecar and which build. Optional by design — the editor calls it only if a
// host registers one.
builder.Services.AddScoped<Ben.Video.Editor.Services.ISidecarPairingReporter>(sp =>
    new Ben.Wasm.Video.Services.SidecarPairingReporter(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<Ben.Wasm.Video.Services.TokenStore>(),
        apiBaseUrl));

await builder.Build().RunAsync();
