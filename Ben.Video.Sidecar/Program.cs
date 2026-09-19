using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Ben.Video.Editor.Services;
using Ben.Video.Sidecar;
using Ben.Video.Sidecar.Api;
using Ben.Video.Sidecar.Jobs;
using Ben.Video.Sidecar.Lifetime;
using Ben.Video.Sidecar.Security;
using Ben.Video.Sidecar.Storage;
using Ben.Video.Sidecar.Validation;
using Microsoft.AspNetCore.RateLimiting;

// The content root is the app's own folder, stated rather than inherited.
//
// CreateBuilder takes the content root from the CURRENT DIRECTORY when it is not told otherwise,
// and then the default configuration watches appsettings.json in it with reloadOnChange — which
// puts a recursive PhysicalFileProvider watch on that directory. Installed, this runs from a
// LaunchAgent, and launchd starts a process with its working directory set to "/". So the watch
// was over the whole filesystem: every change anywhere on the machine arrived as an FSEvent and
// was stat()ed, one thread pinned a core for as long as the app was up, and the heap climbed to
// 2.9 GB. Measured 2026-09-19 after it had been running just under four hours, by which point a
// /v1/health request answering a small object took 340ms — the very call the editor polls to
// decide whether native acceleration is there.
//
// AppContext.BaseDirectory is where this app's own files actually are, however it was launched,
// so it is the right answer for a bundle, for `dotnet run`, and for the tests alike.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args            = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<SidecarOptions>(builder.Configuration.GetSection("Sidecar"));
builder.Services.AddSingleton<AuthFailureThrottle>();
builder.Services.AddSingleton<JobRegistry>();
builder.Services.AddSingleton<IdleWatch>();
builder.Services.AddHostedService<IdleShutdownService>();
builder.Services.AddSingleton<SidecarPaths>();
builder.Services.AddSingleton(sp => new InstallIdentity(sp.GetRequiredService<SidecarPaths>().ConfigDir));
builder.Services.AddSingleton<SourceCache>();
builder.Services.AddSingleton<SpecValidator>();
builder.Services.AddSingleton(sp =>
{
    var devOverride = builder.Configuration["Sidecar:FfmpegDevPathOverride"];
    // Item #70 phase 158 — optional; absent just means no probe/thumbnail capability.
    var ffprobeDevOverride = builder.Configuration["Sidecar:FfprobeDevPathOverride"];
    // Item #70 phase 174 — where the BUNDLED ffmpeg/<rid>/ tree and the manifest are looked up.
    // Development/test only: integration tests need "this build has no bundled binaries" to be a
    // property of the test, not of whether the developer happens to have run fetch-ffmpeg.sh.
    // Before real binaries existed the test output directory was empty by definition, so the
    // fixture got that for free and CapabilitiesEndpointTests silently depended on it.
    var baseDirOverride = builder.Configuration["Sidecar:AppBaseDirectoryOverride"];
    var baseDir = string.IsNullOrEmpty(baseDirOverride) ? AppContext.BaseDirectory : baseDirOverride;
    return new FfmpegLocator(baseDir, devOverride, ffprobeDevOverride);
});
builder.Services.AddSingleton<FfmpegRunner>();
builder.Services.AddSingleton(sp =>
{
    var store = new PairingTokenStore(sp.GetRequiredService<SidecarPaths>().ConfigDir);
    store.LoadOrCreate();
    return store;
});

// Item #38 phase 123 (F) — the effect registry MUST be the same DefaultEffectRegistry.CreateDefault()
// list the browser registers (Ben.Video.Editor.Extensions.ServiceCollectionExtensions), so an
// AppliedEffectDto.EffectId means the same thing on both sides of the wire.
builder.Services.AddSingleton(DefaultEffectRegistry.CreateDefault());
builder.Services.AddSingleton<SegmentJobStore>();
// Item #70 phase 159 — one shared encode budget across every job kind, so MaxConcurrentJobs
// means what it says now that there's more than one runner.
builder.Services.AddSingleton<JobConcurrencyLimiter>();
builder.Services.AddSingleton<SegmentJobRunner>();
builder.Services.AddSingleton<ThumbnailJobRunner>();
builder.Services.AddSingleton<ConcatJobRunner>();
builder.Services.AddSingleton<ExportAssembleJobRunner>();
builder.Services.AddSingleton<RenderedSegmentStore>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 600,
            Window = TimeSpan.FromMinutes(1),
        }));
});

var sidecarOptions = builder.Configuration.GetSection("Sidecar").Get<SidecarOptions>() ?? new SidecarOptions();
// Installed, launchd owns the listening socket: it binds the port itself, holds it whether or not
// this process exists, and starts this process on the first connection — which is how a sidecar
// that is not always running is nevertheless always there when the editor looks (2026-09-19).
// Everywhere else — dotnet run, the tests, Windows — this finds nothing and the port is bound the
// way it always was.
var launchdSocket = LaunchdSockets.TryTakeListener();
var resolvedPort  = launchdSocket is null
    ? ResolveFreePort(sidecarOptions.Port, sidecarOptions.PortScanRange)
    : sidecarOptions.Port;

// Idle shutdown only where something brings the process back - see
// IdleShutdownPolicy.EffectiveTimeout. Without this, Windows stopped the sidecar fifteen quiet
// minutes after login and nothing started it again until the next login (1.1.0).
builder.Services.PostConfigure<SidecarOptions>(o =>
    o.IdleTimeout = IdleShutdownPolicy.EffectiveTimeout(o.IdleTimeout, restartedOnDemand: launchdSocket is not null));

builder.WebHost.ConfigureKestrel(kestrel =>
{
    if (launchdSocket is { } handle)
        kestrel.ListenHandle(handle);
    else
        kestrel.Listen(IPAddress.Loopback, resolvedPort);

    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = sidecarOptions.DefaultMaxRequestBodyBytes;
});

var app = builder.Build();

// Reset/print the pairing token before anything else runs, so a fresh `--reset-token` invocation
// never accidentally serves a request under the old token.
var tokenStore = app.Services.GetRequiredService<PairingTokenStore>();
if (args.Contains("--reset-token"))
{
    tokenStore.Generate();
}

// Item #70 phase 175 — `--pair` generates a code, prints it, and exits WITHOUT serving.
//
// An installed sidecar runs as a background service (a macOS LaunchAgent) with no console
// attached, so the first-run banner below is written to a log file nobody is watching and the
// user has no way to reach their pairing code. This is the installer's answer to that, and it
// rotates rather than re-displays: PairingTokenStore deliberately keeps only a hash in memory
// after a normal load, on the principle that the code is for the user to paste once. Rotating
// honours that (any previously paired browser must re-pair, which is the correct security
// behaviour for "I need a code again") instead of weakening the store to allow read-back.
if (args.Contains("--pair"))
{
    if (!tokenStore.WasJustCreated) tokenStore.Generate();
    Console.WriteLine();
    Console.WriteLine("  Pairing code:  " + tokenStore.PlaintextOnFirstRun);
    Console.WriteLine();
    Console.WriteLine("  Paste this into the editor's Settings -> Native acceleration panel.");
    Console.WriteLine("  Any browser paired with a previous code will need to pair again.");
    Console.WriteLine();
    return;
}

PrintStartupBanner(app, tokenStore, resolvedPort);

// Ahead of everything, and deliberately: a request that is rate-limited or rejected for a bad
// token is still evidence that somebody is using this, and stopping underneath them would be the
// wrong answer to it.
app.Use(async (context, next) =>
{
    context.RequestServices.GetRequiredService<IdleWatch>().Touch();
    await next(context);
});

app.UseRateLimiter();
app.UseMiddleware<SecurityMiddleware>();

app.MapHealthEndpoints();
app.MapPairingEndpoints();
app.MapSourceEndpoints();
app.MapJobEndpoints();
app.MapProbeEndpoints();
app.MapSegmentEndpoints();

app.Run();

static int ResolveFreePort(int startPort, int scanRange)
{
    for (var port = startPort; port <= startPort + scanRange; port++)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return port;
        }
        catch (SocketException)
        {
            // Port in use — try the next one.
        }
    }

    throw new InvalidOperationException(
        $"No free port found in range {startPort}-{startPort + scanRange} on 127.0.0.1.");
}

static void PrintStartupBanner(WebApplication app, PairingTokenStore tokenStore, int port)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("BenVideo sidecar listening on http://127.0.0.1:{Port}", port);

    if (tokenStore.WasJustCreated && tokenStore.PlaintextOnFirstRun is not null)
    {
        Console.WriteLine();
        Console.WriteLine("========================================================");
        Console.WriteLine(" BenVideo sidecar — first run");
        Console.WriteLine();
        Console.WriteLine($"   Pairing code:  {tokenStore.PlaintextOnFirstRun}");
        Console.WriteLine();
        Console.WriteLine(" Paste this into the editor's Settings -> Native");
        Console.WriteLine(" acceleration panel. You only need to do this once.");
        Console.WriteLine("========================================================");
        Console.WriteLine();
    }
    else
    {
        logger.LogInformation("Pairing token loaded. Use --reset-token to generate a new one.");
    }
}

/// <summary>Exposes the implicit Program class to WebApplicationFactory-based integration tests.</summary>
public partial class Program;
