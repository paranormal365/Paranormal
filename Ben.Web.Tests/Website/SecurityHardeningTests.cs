using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The response-header hardening the 2026-09-03 sweep asked for, held in place by reading the
/// source — the same shape as the other guards, because a header that quietly disappears from a
/// middleware is exactly the failure nothing else would notice.
/// </summary>
public sealed class SecurityHardeningTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Theory]
    [InlineData("Ben.Web.Website/Program.cs")]
    [InlineData("Ben.Data.WebApi/Program.cs")]
    public void Both_hosts_refuse_to_be_framed(string programFile)
    {
        // Nothing on the site frames itself — there is no <iframe> in any Razor file — so nothing
        // may frame it either. Both headers: older browsers read only the first, everything
        // current reads the second.
        var source = RepoFile(programFile);
        Assert.Contains("Headers[\"X-Frame-Options\"] = \"DENY\"", source);
        Assert.Contains("Headers[\"Content-Security-Policy\"] = \"frame-ancestors 'none'\"", source);
    }

    [Fact]
    public void Nothing_in_the_site_frames_itself()
    {
        // The precondition for DENY. The day something needs a frame, this test is the place that
        // says so, and the header moves to SAMEORIGIN / 'self' deliberately rather than by surprise.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        var offenders = new[] { "Ben.Web.Website", "Ben.Web.Website.Library" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(dir!.FullName, p), "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("<iframe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            "These pages use an <iframe>; X-Frame-Options: DENY will break them:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void HSTS_is_a_year_and_covers_subdomains_but_is_not_preloaded()
    {
        var source = RepoFile("Ben.Web.Website/Program.cs");
        Assert.Contains("hsts.MaxAge            = TimeSpan.FromDays(365)", source);
        Assert.Contains("hsts.IncludeSubDomains = true", source);
        // Preload is a one-way door into the browser lists; it must be a decision, not a side effect.
        Assert.DoesNotContain("hsts.Preload", source);
    }

    [Fact]
    public void IIS_does_not_announce_its_version()
    {
        // The website ships its own web.config; the API's is generated at publish, so the deploy
        // script sets the attribute there. Both paths have to hold.
        Assert.Contains("<requestFiltering removeServerHeader=\"true\">", RepoFile("Ben.Web.Website/web.config"));
        Assert.Contains("SetAttribute('removeServerHeader', 'true')", RepoFile("scripts/deploy-ishaunted.ps1"));
        // The video editor is a child application with its own web.config, and it was still
        // announcing IIS after the other two stopped (2026-09-03).
        Assert.Contains("<requestFiltering removeServerHeader=\"true\" />", RepoFile("Ben.Wasm.Video/wwwroot/web.config"));
    }

    [Fact]
    public void Log_retention_checks_the_table_exists_before_sweeping()
    {
        // A dev instance whose EF context points at a database with no Logs table used to log an
        // ERROR every hour — into the production Logs table its Serilog sink pointed at.
        var source = RepoFile("Ben.Data.WebApi/Services/Scheduling/LogRetentionJob.cs");
        var check = source.IndexOf("OBJECT_ID(N'[{table}]', N'U')", StringComparison.Ordinal);
        var delete = source.IndexOf("DELETE TOP", StringComparison.Ordinal);
        Assert.True(check >= 0, "The retention job no longer checks that the table exists.");
        Assert.True(check < delete, "The existence check must come before the DELETE.");
    }
}
