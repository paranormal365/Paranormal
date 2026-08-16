using Ben.Video.Core.SidecarContracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// The base fixture for every sidecar integration test: a real in-process Kestrel-free TestServer
/// running the actual application pipeline (real DI, real middleware order, real routing) — the
/// only things swapped out are the ffmpeg binary (the fake, per-test-isolated) and storage
/// locations (a throwaway temp directory, unique per instance). One instance per test class
/// (xunit's default IClassFixture lifetime) gives each test class its own isolated sidecar.
/// </summary>
public class SidecarWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string DefaultOrigin = "http://localhost:64368";
    public const string ValidClipExt = ".mp4";

    private readonly string _homeDir = Directory.CreateTempSubdirectory("benvideo-sidecar-test-").FullName;

    /// <summary>Deliberately empty — see the AppBaseDirectoryOverride note in ConfigureWebHost.</summary>
    private readonly string _bundleDir = Directory.CreateTempSubdirectory("benvideo-sidecar-bundle-").FullName;

    /// <summary>
    /// Item #70 phase 158 — when true, this instance also points <c>Sidecar:FfprobeDevPathOverride</c>
    /// at the fake binary, which makes <c>GET /v1/capabilities</c> advertise probe/thumbnails.
    /// Default false so the base fixture keeps modelling a sidecar with no ffprobe (the pre-158
    /// shape), and capability-gating tests can assert both sides of that branch.
    /// </summary>
    protected bool WithFfprobe { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Sidecar:HomeOverride"] = _homeDir,
                ["Sidecar:FfmpegDevPathOverride"] = FakeFfmpegPath.Resolve(),
                ["Sidecar:AllowedOrigins:0"] = DefaultOrigin,
                // Item #70 phase 174 — pin the bundled-binary lookup at an empty directory so
                // "this build ships no ffmpeg/ffprobe" is a property of the fixture rather than of
                // the developer's working tree. Once real binaries are fetched they land in the
                // test project's output too (Content flows through the project reference), which
                // otherwise makes the base fixture start advertising probe/thumbnails and turns
                // Capabilities_WithoutFfprobe_OmitsOnlyTheFfprobeDependentOnes into a test that
                // passes or fails depending on whether fetch-ffmpeg.sh has ever been run here.
                ["Sidecar:AppBaseDirectoryOverride"] = _bundleDir,
            };
            if (WithFfprobe) settings["Sidecar:FfprobeDevPathOverride"] = FakeFfmpegPath.Resolve();
            config.AddInMemoryCollection(settings);
        });
    }

    /// <summary>An HttpClient pre-configured with the Origin and pairing-token headers a real
    /// paired browser would send. Individual tests remove/override headers to exercise the
    /// negative cases.</summary>
    public HttpClient CreateAuthenticatedClient(string? origin = DefaultOrigin, string? token = null)
    {
        var client = CreateClient();
        if (origin is not null) client.DefaultRequestHeaders.Add("Origin", origin);
        if (token is not null) client.DefaultRequestHeaders.Add(SidecarProtocol.TokenHeaderName, token);
        return client;
    }

    /// <summary>Reads the pairing token this factory's sidecar instance generated on first
    /// startup, straight off disk — mirrors what a human would copy from the console banner.</summary>
    public string ReadGeneratedPairingToken()
    {
        var tokenFile = Path.Combine(_homeDir, "config", "pairing-token");
        // The token file is written lazily on first DI resolution of PairingTokenStore — force
        // that by touching the health endpoint once, synchronously, if it hasn't happened yet.
        if (!File.Exists(tokenFile))
        {
            using var client = CreateClient();
            client.GetAsync("/v1/health").GetAwaiter().GetResult();
        }
        return File.ReadAllText(tokenFile).Trim();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_homeDir, recursive: true); } catch { /* best-effort cleanup */ }
        try { Directory.Delete(_bundleDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}

internal static class FakeFfmpegPath
{
    public static string Resolve()
    {
        var exeName = OperatingSystem.IsWindows() ? "fakeffmpeg.exe" : "fakeffmpeg";
        var path = Path.Combine(AppContext.BaseDirectory, exeName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Fake ffmpeg binary not found at {path} — Ben.Video.Sidecar.FakeFfmpeg must build " +
                "as a project reference of Ben.Video.Sidecar.Tests for its apphost to be copied here.",
                path);
        return path;
    }
}
