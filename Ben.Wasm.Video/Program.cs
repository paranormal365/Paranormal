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

// TODO(auth): the media library and project save/load endpoints require a bearer token, and this
// host does not yet have one to send — under Blazor Server the circuit's token store fills that
// role (BenMediaLibraryProvider), which has no WASM counterpart yet. Until that lands, only the
// anonymous surfaces (asset catalog, local editing) work against a real WebApi. This is the
// known first work item for this project, not an oversight.

await builder.Build().RunAsync();
