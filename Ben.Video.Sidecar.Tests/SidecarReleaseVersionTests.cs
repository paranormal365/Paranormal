using System.Text.RegularExpressions;
using Ben.Video.Core.SidecarContracts;
using Xunit;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// The version the sidecar reports and the version the site advertises are the same number.
/// </summary>
/// <remarks>
/// The editor's "a newer sidecar is available" notice compares what /v1/health reports — the
/// sidecar's ASSEMBLY version, set by &lt;Version&gt; in its csproj — against what the host
/// publishes, which comes from SidecarRelease.Version. Those live in two different kinds of file,
/// so bumping one and forgetting the other is easy and silent: advertise too high and every
/// install is nagged for ever, too low and none of them ever are. Neither failure shows up
/// anywhere except in a user's face (2026-09-19).
/// </remarks>
public sealed class SidecarReleaseVersionTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public void The_csproj_and_the_constant_agree()
    {
        var csproj = File.ReadAllText(RepoFile("Ben.Video.Sidecar", "Ben.Video.Sidecar.csproj"));
        var match  = Regex.Match(csproj, @"<Version>\s*([^<\s]+)\s*</Version>");

        Assert.True(match.Success, "Ben.Video.Sidecar.csproj has no <Version>, so it reports 1.0.0.0 "
                                 + "whatever SidecarRelease says and the update notice cannot work.");
        Assert.Equal(SidecarRelease.Version, match.Groups[1].Value);
    }

    [Fact]
    public void The_macos_installer_stamps_that_version_rather_than_a_literal()
    {
        // The third place the number has to be right, and the one that had silently kept saying
        // 1.0.0 through the bump to 1.1.1 (2026-09-20). build.sh writes CFBundleVersion,
        // CFBundleShortVersionString and pkgbuild --version; install.sh then reports
        // CFBundleShortVersionString to /api/sidecar-telemetry/installs, so a literal here does not
        // just mislabel a package — it misreports every install on the admin dashboard, as a
        // version that is real and older, which is the hardest kind of wrong number to notice.
        var build = File.ReadAllText(RepoFile("Ben.Video.Sidecar", "installer", "macos", "build.sh"));

        foreach (var key in new[] { "CFBundleVersion", "CFBundleShortVersionString" })
        {
            var line = build.Split('\n').FirstOrDefault(l => l.Contains($"<key>{key}</key>"));
            Assert.True(line is not null, $"build.sh no longer writes {key}.");
            Assert.True(line!.Contains("$VERSION"),
                $"build.sh stamps a literal into {key}; it must use the $VERSION read from the csproj.");
        }

        var pkgVersion = build.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("--version"));
        Assert.True(pkgVersion is not null, "build.sh no longer passes --version to pkgbuild.");
        Assert.True(pkgVersion!.Contains("$VERSION"),
            "pkgbuild is given a literal --version; it must use the $VERSION read from the csproj.");

        Assert.Contains("Ben.Video.Sidecar.csproj", build);
    }

    [Fact]
    public void The_running_assembly_reports_that_same_version()
    {
        // The end of the chain: what a user's sidecar actually puts in its health response.
        var assembly = typeof(Ben.Video.Sidecar.Api.HealthEndpoints).Assembly.GetName().Version;

        Assert.NotNull(assembly);
        Assert.True(Version.TryParse(SidecarRelease.Version, out var declared));
        Assert.Equal(declared!.Major, assembly!.Major);
        Assert.Equal(declared.Minor, assembly.Minor);
        Assert.Equal(declared.Build, assembly.Build);
    }
}
