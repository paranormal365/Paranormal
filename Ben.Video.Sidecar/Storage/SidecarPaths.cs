using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace Ben.Video.Sidecar.Storage;

/// <summary>
/// OS-appropriate storage locations for the sidecar's own state — never anywhere inside the
/// user's project files or arbitrary user-chosen directories, and never anything derived from
/// request input (see <see cref="Ben.Video.Sidecar.Validation.SpecValidator"/> for why that
/// matters).
///
/// An instance (not static) so tests can construct one rooted at a throwaway temp directory. The
/// override is read from <see cref="IConfiguration"/> (config key <c>Sidecar:HomeOverride</c>)
/// rather than an environment variable — a process-wide env var would race across xunit's
/// parallel test execution, since every WebApplicationFactory-created host in the same test run
/// shares one process; per-host IConfiguration does not have that problem. A
/// <c>BENVIDEO_SIDECAR_HOME</c> environment variable is still honored as a fallback, for a real
/// published binary run manually with a redirected home directory.
/// </summary>
public sealed class SidecarPaths
{
    public const string EnvironmentVariableName = "BENVIDEO_SIDECAR_HOME";

    public SidecarPaths(IConfiguration configuration)
    {
        var overrideRoot = configuration["Sidecar:HomeOverride"]
            ?? Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrEmpty(overrideRoot))
        {
            ConfigDir = EnsureDir(Path.Combine(overrideRoot, "config"));
            CacheDir = EnsureDir(Path.Combine(overrideRoot, "cache"));
        }
        else
        {
            ConfigDir = ResolveConfigDir();
            CacheDir = ResolveCacheDir();
        }
    }

    /// <summary>Small, durable config — the pairing token file.</summary>
    public string ConfigDir { get; }

    /// <summary>Larger, evictable data — the source cache and per-job workspaces.</summary>
    public string CacheDir { get; }

    public string SourcesDir => EnsureDir(Path.Combine(CacheDir, "sources"));
    public string JobsDir => EnsureDir(Path.Combine(CacheDir, "jobs"));

    /// <summary>Item #70 phase 160 — retained rendered segments (dual residency). Separate from
    /// <see cref="JobsDir"/> because job workspaces are swept on a retention timer while these
    /// outlive their originating job by design, and separate from <see cref="SourcesDir"/> because
    /// they have their own quota and eviction policy.</summary>
    public string SegmentsDir => EnsureDir(Path.Combine(CacheDir, "segments"));

    private static string ResolveConfigDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BenVideo", "sidecar"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "BenVideo", "sidecar"));

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = !string.IsNullOrEmpty(xdgConfig)
            ? xdgConfig
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return EnsureDir(Path.Combine(baseDir, "benvideo", "sidecar"));
    }

    private static string ResolveCacheDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BenVideo", "cache"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "BenVideo"));

        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var baseDir = !string.IsNullOrEmpty(xdgCache)
            ? xdgCache
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return EnsureDir(Path.Combine(baseDir, "benvideo"));
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
