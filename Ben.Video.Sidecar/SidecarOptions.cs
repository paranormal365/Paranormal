using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar;

/// <summary>
/// Runtime configuration for the sidecar process. Everything here is either a fixed default
/// (documented, not user-editable in v1) or overridable via <c>appsettings.json</c>/environment
/// for development — the shipped binary runs with these defaults untouched.
/// </summary>
public sealed class SidecarOptions
{
    /// <summary>First port tried on startup; if occupied, <see cref="PortScanRange"/> more ports
    /// are tried in sequence. The browser's health probe tries the same range in the same
    /// order — see <see cref="SidecarProtocol"/> for the shared defaults both sides agree on.</summary>
    public int Port { get; set; } = SidecarProtocol.DefaultPort;

    /// <summary>How many additional ports to try after <see cref="Port"/> if it's occupied.</summary>
    public int PortScanRange { get; set; } = SidecarProtocol.DefaultPortScanRange;

    /// <summary>
    /// Browser origins allowed to talk to this sidecar. Every mutating request (and every request
    /// that carries an Origin header at all) is checked against this list server-side — the
    /// actual enforcement, not just the CORS preflight. Configurable so a self-hosted deployment
    /// of the editor can add its own origin; the local dev origins are always included.
    /// </summary>
    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost:5000", "https://localhost:5001",
        "http://localhost:5078",  "https://localhost:7050",  // Ben.Web.WebApp dev defaults
        "http://localhost:5180",                             // Ben.Wasm.Video dev default

        // Production. Without these the sidecar refuses every request from the deployed editor,
        // and it refuses them the same way it refuses a wrong pairing code — a 403 that reads to
        // the user as "the code did not work", with a healthy sidecar sitting right there.
        "https://ishaunted.com", "https://www.ishaunted.com",
    ];

    /// <summary>Default cap on the source cache (OPFS clip uploads), in bytes. LRU-evicted.</summary>
    public long SourceCacheQuotaBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>Default cap on a single job's scratch workspace, in bytes.</summary>
    public long JobWorkspaceQuotaBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>Item #70 phase 160 — disk ceiling for retained rendered segments (dual residency).
    /// Smaller than the source/job quotas on purpose: retained segments are an optimization whose
    /// worst case is a re-render, whereas evicting a source the browser still needs would cost a
    /// full re-upload. LRU-evicted, never evicting a segment pinned by an in-flight job.</summary>
    public long RetainedSegmentQuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>How long a finished/failed job's workspace and result stay available before
    /// automatic cleanup, if the browser never calls <c>DELETE /v1/jobs/{id}</c>.</summary>
    public TimeSpan JobRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Wall-clock limit for a single ffmpeg invocation before it's killed.</summary>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum number of ffmpeg processes running at once. Kept small — this is a
    /// companion to the browser, not a render farm, and unbounded concurrency defeats the
    /// resource-exhaustion defense (item #38 phase E threat T6).</summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>Default request body cap. Raised per-request only on the endpoints that
    /// legitimately need it (source/asset uploads).</summary>
    public long DefaultMaxRequestBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>Cap on a single source/asset upload body.</summary>
    public long MaxUploadBodyBytes { get; set; } = 16L * 1024 * 1024 * 1024;

    /// <summary>File extensions accepted by <c>PUT /v1/sources/{clipId}</c> — a closed allowlist,
    /// not a blocklist. Anything else is rejected before any bytes are written.</summary>
    public HashSet<string> AllowedSourceExtensions { get; } =
    [
        ".mp4", ".mov", ".webm", ".mkv", ".m4v",
        ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
    ];
}
