using Ben.Video.Sidecar.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Redirecting where the sidecar keeps its own state.
/// </summary>
/// <remarks>
/// <para>The config key is what every test here uses and is well covered by the fixtures that
/// build a host. The <b>environment variable</b> is the other half, and it is the half a person
/// actually reaches for: it is the only way to run a second sidecar — a fresh build, say — beside
/// an installed one without the two sharing a pairing token. Following the e2e script's
/// instructions without it resets the installed sidecar's token and quietly breaks whatever
/// browser session was paired with it (2026-09-05).</para>
///
/// <para>Not parallelised: the variable is process-wide, and another test constructing
/// <see cref="SidecarPaths"/> at the same moment would see it. One collection, one at a time,
/// and the variable is cleared however the test ends.</para>
/// </remarks>
[Collection(nameof(SidecarPathsOverrideTests))]
[CollectionDefinition(nameof(SidecarPathsOverrideTests), DisableParallelization = true)]
public sealed class SidecarPathsOverrideTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(), "sidecar-paths-" + Guid.NewGuid().ToString("N")[..8]);

    private static IConfiguration NoConfig()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SidecarPaths.EnvironmentVariableName, null);
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    [Fact]
    public void The_environment_variable_moves_both_directories()
    {
        Environment.SetEnvironmentVariable(SidecarPaths.EnvironmentVariableName, _home);

        var paths = new SidecarPaths(NoConfig());

        Assert.Equal(Path.Combine(_home, "config"), paths.ConfigDir);
        Assert.Equal(Path.Combine(_home, "cache"), paths.CacheDir);

        // Created, not merely computed: the token is written on first run and a path that does not
        // exist yet would fail at exactly the wrong moment.
        Assert.True(Directory.Exists(paths.ConfigDir));
        Assert.True(Directory.Exists(paths.CacheDir));
    }

    /// <summary>
    /// Configuration wins. A host that sets the key means it, and a stray variable in the
    /// environment must not quietly move a running sidecar's state somewhere else.
    /// </summary>
    [Fact]
    public void Configuration_beats_the_environment_variable()
    {
        var fromConfig = Path.Combine(_home, "from-config");
        Environment.SetEnvironmentVariable(SidecarPaths.EnvironmentVariableName, Path.Combine(_home, "from-env"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sidecar:HomeOverride"] = fromConfig })
            .Build();

        var paths = new SidecarPaths(config);

        Assert.Equal(Path.Combine(fromConfig, "config"), paths.ConfigDir);
    }

    /// <summary>
    /// With neither set, the real per-user location is used — the case that must keep working,
    /// because it is where an installed sidecar's token actually lives.
    /// </summary>
    [Fact]
    public void With_no_override_it_falls_back_to_the_users_own_location()
    {
        Environment.SetEnvironmentVariable(SidecarPaths.EnvironmentVariableName, null);

        var paths = new SidecarPaths(NoConfig());

        Assert.False(paths.ConfigDir.StartsWith(_home, StringComparison.Ordinal));
        Assert.True(Path.IsPathRooted(paths.ConfigDir));
    }
}
