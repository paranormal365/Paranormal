using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>Which bundled binary a caller wants — item #70 phase 158. ffprobe is optional
/// (probe/thumbnail capabilities simply aren't advertised without it); ffmpeg is required for the
/// sidecar to serve job endpoints at all.</summary>
public enum FfmpegTool { Ffmpeg, Ffprobe }

/// <summary>
/// Resolves the ffmpeg/ffprobe binaries this process will run and verifies their integrity against
/// the committed hash manifest — item #38 phase E threat T7 (supply chain). A binary path is
/// always either the bundled per-RID path under the app's own directory, or (development only)
/// an explicit override — <b>never</b> a PATH lookup, so nothing on the user's PATH can be
/// silently substituted for the real binary this app shipped with.
///
/// <para>Item #70 phase 158 generalized this from ffmpeg-only to a two-tool resolver.
/// <see cref="FfmpegTool.Ffprobe"/> is deliberately optional and <b>fails soft</b>: a missing or
/// unverified ffprobe only withholds the probe/thumbnail capabilities from
/// <c>GET /v1/capabilities</c>, it never stops the sidecar serving segment jobs. ffmpeg keeps its
/// original fail-closed behavior via <see cref="VerifyIntegrity"/>.</para>
/// </summary>
public sealed class FfmpegLocator
{
    private sealed record ManifestEntry(string Url, string Sha256, string Version, string? FfprobeSha256 = null);

    /// <summary>
    /// The manifest's keys are camelCase (<c>sha256</c>, <c>ffprobeSha256</c>) but these record
    /// properties are PascalCase, and <see cref="JsonSerializer"/>'s DEFAULT options are
    /// case-sensitive — without this, every property silently deserialized to null and
    /// <see cref="VerifyIntegrity"/> compared against an empty hash, so a bundled (non-override)
    /// binary could never verify. Found while adding ffprobe support in item #70 phase 158; it
    /// had been latent because every code path exercised so far used the development override,
    /// which returns before ever reading the manifest.
    /// </summary>
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<FfmpegTool, string> _paths;
    private readonly Dictionary<FfmpegTool, bool> _isOverride;

    public FfmpegLocator(
        string appBaseDirectory,
        string? developmentPathOverride = null,
        string? ffprobeDevelopmentPathOverride = null)
    {
        var rid = CurrentRid();
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var ffmpegExe  = isWindows ? "ffmpeg.exe"  : "ffmpeg";
        var ffprobeExe = isWindows ? "ffprobe.exe" : "ffprobe";

        _paths = new Dictionary<FfmpegTool, string>
        {
            [FfmpegTool.Ffmpeg] = !string.IsNullOrEmpty(developmentPathOverride)
                ? developmentPathOverride
                : Path.Combine(appBaseDirectory, "ffmpeg", rid, ffmpegExe),
            [FfmpegTool.Ffprobe] = !string.IsNullOrEmpty(ffprobeDevelopmentPathOverride)
                ? ffprobeDevelopmentPathOverride
                : Path.Combine(appBaseDirectory, "ffmpeg", rid, ffprobeExe),
        };
        _isOverride = new Dictionary<FfmpegTool, bool>
        {
            [FfmpegTool.Ffmpeg]  = !string.IsNullOrEmpty(developmentPathOverride),
            [FfmpegTool.Ffprobe] = !string.IsNullOrEmpty(ffprobeDevelopmentPathOverride),
        };

        ManifestPath = Path.Combine(appBaseDirectory, "ffmpeg-manifest.json");
        Rid = rid;
    }

    /// <summary>The ffmpeg binary — unchanged meaning from before phase 158, kept as a property so
    /// every existing call site (<see cref="FfmpegRunner"/>, health checks) is untouched.</summary>
    public string ExecutablePath => _paths[FfmpegTool.Ffmpeg];

    public string Rid { get; }
    public bool IsDevelopmentOverride => _isOverride[FfmpegTool.Ffmpeg];
    private string ManifestPath { get; }

    public string PathFor(FfmpegTool tool) => _paths[tool];

    /// <summary>
    /// True when the ffmpeg executable exists and (for the bundled, non-override case) its SHA-256
    /// matches the committed manifest entry for this RID. A mismatch — or a missing manifest entry
    /// — fails closed: <see cref="Program"/> refuses to serve job endpoints rather than run an
    /// unverified binary.
    /// </summary>
    public bool VerifyIntegrity() => VerifyIntegrity(FfmpegTool.Ffmpeg);

    /// <summary>
    /// Per-tool integrity check. For <see cref="FfmpegTool.Ffprobe"/> a <c>false</c> result is not
    /// fatal — <c>GET /v1/capabilities</c> simply omits the probe/thumbnail capabilities and the
    /// browser keeps doing that work in wasm, which is exactly today's behavior.
    /// </summary>
    public bool VerifyIntegrity(FfmpegTool tool)
    {
        var path = _paths[tool];
        if (!File.Exists(path)) return false;

        // Development override deliberately skips hash verification — it's pointing at a
        // developer's own local build for testing before real per-RID binaries are published,
        // not at anything this app is claiming to trust as "the shipped binary".
        if (_isOverride[tool]) return true;

        if (!File.Exists(ManifestPath)) return false;

        try
        {
            // Item #70 phase 174 — read ONLY this RID's entry out of the document instead of
            // deserializing the whole file into Dictionary<string, ManifestEntry>. The manifest has
            // always carried a top-level "_comment" STRING alongside the per-RID objects, and
            // binding a string to ManifestEntry throws — so the dictionary form threw on the real
            // manifest, was swallowed by the catch below, and reported "unverified" for a binary
            // whose hash matched perfectly. Latent for the same reason as the case-sensitivity bug
            // above: every path exercised until now used a dev override, which returns before ever
            // reading this file. Found the first time a real pin was bundled — the sidecar started
            // with correct, hash-matching binaries and still served FfmpegIntegrityOk: false.
            using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty(Rid, out var entryElement)) return false;
            if (entryElement.ValueKind != JsonValueKind.Object) return false;

            var entry = entryElement.Deserialize<ManifestEntry>(ManifestJsonOptions);
            if (entry is null) return false;

            var expected = tool == FfmpegTool.Ffmpeg ? entry.Sha256 : entry.FfprobeSha256;
            // A manifest predating phase 158 has no ffprobe hash at all — treat that as "this
            // build doesn't ship a verified ffprobe" rather than trusting an unpinned binary.
            if (string.IsNullOrEmpty(expected)) return false;

            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            return string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string CurrentRid()
    {
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
        return $"linux-{arch}";
    }
}
