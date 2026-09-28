using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The super-admin seed password reaches the API as an application-pool environment variable and
/// is never written into the deployed <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Until 2026-09-27 <c>scripts/deploy-ishaunted.ps1</c> merged
/// <c>SeedData:SuperAdmin:Password</c> from the secrets file into the API's <c>appsettings.json</c>
/// on every deploy, so the password sat in plain text beside the binaries. It now rides the same
/// rail as <c>Smtp__Password</c>: the pool's environment, in applicationHost.config, readable only by
/// administrators.</para>
///
/// <para><b>Why not simply stop sending it</b> once the account exists: <c>SuperAdminSeeder</c>
/// returns before doing anything when the password is blank, and "anything" includes creating the
/// site roles. A release that adds a role would never get it, and nothing would say so.</para>
///
/// <para>A source scan because the script runs elevated, on one machine, and a regression here fails
/// silently in production - the file just quietly carries the password again.</para>
/// </remarks>
public sealed class SeedPasswordStaysOffDiskTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string RepoFile(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string DeployScript() => RepoFile("scripts/deploy-ishaunted.ps1");

    [Fact]
    public void The_deploy_never_merges_the_seed_password_into_appsettings()
    {
        Assert.False(Regex.IsMatch(DeployScript(), @"'SeedData:SuperAdmin:Password'\s*="),
            "deploy-ishaunted.ps1 carries SeedData:SuperAdmin:Password into appsettings.json again; it belongs on the pool");
    }

    /// <summary>
    /// <c>-SkipBuild</c> re-reads the staged file, so a stage made before the change would still ship
    /// the password unless the script strips it.
    /// </summary>
    [Fact]
    public void The_deploy_strips_a_seed_password_left_in_a_staged_appsettings()
    {
        var script = DeployScript();
        Assert.Contains("Get-JsonValue $cfg 'SeedData:SuperAdmin'", script, StringComparison.Ordinal);
        Assert.Contains("PSObject.Properties.Remove('Password')", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_deploy_puts_the_seed_password_on_the_api_pool()
    {
        var script = DeployScript();
        Assert.Contains("$seedPassword  = Get-JsonValue $secrets 'SeedSuperAdmin:Password'", script, StringComparison.Ordinal);
        Assert.Contains("Set-PoolEnv $WebApiPool 'SeedData__SuperAdmin__Password' $seedPassword", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The staged API is started before anything is copied (standing rule 6). It must seed the way the
    /// pool will, or the seeder's startup path is the one part of startup that staging never runs.
    /// </summary>
    [Fact]
    public void The_staged_api_is_given_the_seed_password_the_same_way()
    {
        var script = DeployScript();
        Assert.Contains("Test-StagedApi $webapiOut $seedPassword", script, StringComparison.Ordinal);
        Assert.Contains("$env:SeedData__SuperAdmin__Password = $seedPassword", script, StringComparison.Ordinal);
        Assert.Contains("$env:SeedData__SuperAdmin__Password = $previousSeed", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pool variable only works because <c>SeedData__SuperAdmin__Password</c> maps onto this exact
    /// key. Renaming the key in the seeder would silently orphan the variable.
    /// </summary>
    [Fact]
    public void The_seeder_reads_the_key_the_pool_variable_maps_onto()
    {
        Assert.Contains("config[\"SeedData:SuperAdmin:Password\"]",
            RepoFile("Ben.Data.WebApi/SeedData/SuperAdminSeeder.cs"), StringComparison.Ordinal);
    }
}
