using Ben.Canvas.Editor.Extensions;
using Ben.Canvas.Editor.Services;
using Ben.Wasm.Canvas;
using Ben.Wasm.Canvas.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

// The standalone WebAssembly host for the canvas editor.
//
// It exists because of where the work happens: every pan, drag, paste and autosave runs in the
// person's own browser, and the server is reached only to load, save or publish a board (and to
// resolve a pasted link). Hosted as an interactive-server component instead, every gesture would be a
// round trip over a SignalR circuit.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddTelerikBlazor();

// The browser fetches these from wwwroot/appsettings*.json at startup, so changing them is a static
// file swap, not a rebuild. An empty WebApiBaseUrl is a working configuration: a local-only editor.
var cfg = builder.Configuration.GetSection("Canvas");
var apiBaseUrl = cfg["WebApiBaseUrl"]?.Trim().TrimEnd('/');
var mapTokenUrl = cfg["MapTokenUrl"];
var siteBaseUrl = cfg["SiteBaseUrl"];

// The trailing slash, together with relative request paths ("login", not "/login"), is what keeps the
// /webapi mount. A leading-slash path replaces the base path and posts to the website instead of the
// API - proven live on 2026-09-14, where POST https://ishaunted.com/login answers the website's
// text/html 400 (Ben.Data.WebApi.Client/ApiBasePathHandler.cs:3-24 documents the same failure).
var apiRoot = new Uri(string.IsNullOrEmpty(apiBaseUrl)
    ? builder.HostEnvironment.BaseAddress
    : apiBaseUrl + "/");

builder.Services.AddBenCanvasEditor(o =>
{
    CanvasEditorHostDefaults.ApplyEditingDefaults(o);
    CanvasEditorHostDefaults.ApplyServerIntegration(o, apiBaseUrl, mapTokenUrl, siteBaseUrl);
});

// ── Auth ─────────────────────────────────────────────────────────────────────
// The browser signs in against the API's own login endpoint, keeps the tokens in TokenStore, and
// BearerTokenHandler attaches them to the editor's persistence client.
//
// Singletons, not scoped (review correction R15): IHttpClientFactory builds message handlers in a
// scope of its own, so a scoped TokenStore resolved for the handler would be a different instance from
// the app's - a refresh done in one would never reach the other. A WebAssembly app has one user per
// tab, so one instance is exactly right.
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton(sp => new AuthService(
    // No bearer handler on this client: refresh must be callable with an expired access token.
    new HttpClient { BaseAddress = apiRoot },
    sp.GetRequiredService<TokenStore>()));
builder.Services.AddTransient(sp => new BearerTokenHandler(
    sp.GetRequiredService<TokenStore>(),
    sp.GetRequiredService<AuthService>(),
    apiBaseUrl));

// Exactly one bearer handler on the persistence client; the public client stays anonymous.
builder.Services.AddHttpClient(CanvasEditorServiceCollectionExtensions.PersistenceHttpClientName)
    .AddHttpMessageHandler<BearerTokenHandler>();

// The site's link can carry a one-minute code that signs this host in as the same person. Same client
// reasoning as AuthService: the caller has no token yet.
builder.Services.AddScoped(sp => new CanvasHandoffService(
    new HttpClient { BaseAddress = apiRoot },
    sp.GetRequiredService<TokenStore>(),
    sp.GetRequiredService<NavigationManager>(),
    sp.GetRequiredService<IJSRuntime>()));

builder.Services.AddSingleton(sp => new AccountInfoService(
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<TokenStore>(),
    apiBaseUrl));

builder.Services.AddSingleton<ICanvasSignInState, WasmSignInState>();
builder.Services.AddSingleton<ICanvasAccessTokenSource, WasmAccessTokenSource>();

await builder.Build().RunAsync();
