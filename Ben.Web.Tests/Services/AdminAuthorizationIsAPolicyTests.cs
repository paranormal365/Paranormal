using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// An admin endpoint must gate on the SuperAdmin <b>policy</b>, never on
/// <c>[Authorize(Roles = ...)]</c>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A bare <c>Roles</c> attribute names no authentication scheme, so ASP.NET Core
/// re-authenticates the request with the <em>default</em> scheme alone — the local Identity bearer
/// handler. A caller holding a perfectly valid Entra JWT is not merely refused: they come back
/// unauthenticated, and the endpoint answers <b>401</b> where a 403 was meant. The role check
/// never runs at all.</para>
///
/// <para><b>It also cannot see the role.</b> An Entra principal carries no Identity role claims,
/// which is the entire reason <c>SuperAdminHandler</c> exists — it resolves the role from the
/// database by OID. <c>Roles =</c> consults claims only, so even reached, it would say no.</para>
///
/// <para><b>What it cost.</b> Eight endpoints across seven controllers were written this way and
/// every one of them was closed to Entra sign-ins. It surfaced as the dashboard reporting
/// "couldn't load the dashboard figures" against <c>/api/admin/stats</c>, while
/// <c>/api/app-users</c> — same account, same token, same request batch — returned 200 because it
/// used the policy. That contrast is what made it findable; on its own the 401 reads as a
/// permissions problem with the account, and the account was fine.</para>
///
/// <para>The registration in <c>Ben.Data.WebApi/Program.cs</c> pins the schemes explicitly, which
/// is what makes the policy work for both sign-in routes.</para>
/// </remarks>
public sealed class AdminAuthorizationIsAPolicyTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<FileInfo> ControllerSources()
    {
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));
        Assert.True(api.Exists, $"Ben.Data.WebApi not found at {api.FullName}");

        return api.GetFiles("*.cs", SearchOption.AllDirectories)
                  .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    [Fact]
    public void No_endpoint_authorizes_by_role_attribute()
    {
        // Matches the attribute only, so the explanatory comment in Program.cs does not trip it.
        var rolesAttribute = new Regex(@"^\s*\[Authorize\s*\(\s*Roles\s*=", RegexOptions.Multiline);

        var offenders = new List<string>();
        foreach (var file in ControllerSources())
        {
            foreach (Match m in rolesAttribute.Matches(File.ReadAllText(file.FullName)))
            {
                var line = File.ReadAllText(file.FullName).Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{file.Name}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These use [Authorize(Roles = ...)], which answers 401 to a valid Entra caller because "
          + "it re-authenticates with the default scheme only. Use [Authorize(Policy = "
          + "RoleNames.SuperAdmin)], or AuthPolicyNames.AppAdministrator where both roles are "
          + "accepted:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_admin_stats_endpoints_use_the_policy()
    {
        // The specific endpoint the dashboard calls, named so a regression is obvious rather than
        // arriving as one entry in a list.
        var path = Path.Combine(RepoRoot().FullName,
            "Ben.Data.WebApi", "Controllers", "Admin", "AdminStatsController.cs");

        Assert.True(File.Exists(path), $"AdminStatsController not found at {path}");
        var source = File.ReadAllText(path);

        Assert.Contains("[Authorize(Policy = RoleNames.SuperAdmin)]", source);
        Assert.DoesNotContain("[Authorize(Roles", source);
    }

    [Fact]
    public void Both_admin_policies_are_registered()
    {
        // An attribute naming a policy that was never registered throws at request time, not at
        // startup - so the attribute and the registration have to be checked together.
        var program = File.ReadAllText(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi", "Program.cs"));

        Assert.Contains("AddPolicy(RoleNames.SuperAdmin", program);
        Assert.Contains("AddPolicy(AuthPolicyNames.AppAdministrator", program);
        Assert.Contains("AppAdministratorHandler", program);
    }
}
