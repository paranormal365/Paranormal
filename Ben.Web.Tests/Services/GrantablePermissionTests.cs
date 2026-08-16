using Ben.Data.Common.Enums;
using System.Text.RegularExpressions;
using Xunit;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every permission the server checks must be one an administrator can actually grant.
/// </summary>
/// <remarks>
/// <para>A table the WebApi tests with <c>HasAccessAsync</c> but that the role editor never lists
/// is a permission nobody can hand out: the check runs, always fails for anyone relying on a
/// grant, and the only route to the feature is being an owner or administrator of everything.
/// Nothing reports it. The role editor simply has one fewer row than it should.</para>
///
/// <para>That is exactly what happened to <c>Investigation</c>. It was added to both enums, wired
/// into <c>InvestigationAccess.CanManageAsync</c>, tested, and documented for administrators as
/// the lever to delegate scheduling — while the screen they were told to use had no such row.
/// The help was wrong for as long as the gap existed, which is worse than silence.</para>
///
/// <para>Source-scanned rather than reflected, because the role editor's list is Razor markup and
/// the grant checks are call sites. Renaming either is the ordinary way this breaks.</para>
/// </remarks>
public sealed class GrantablePermissionTests
{
    private static readonly Regex TableReference = new(
        @"OrganizationSecurityTable\.(?<table>\w+)",
        RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void Every_permission_the_server_checks_can_be_granted_in_the_role_editor()
    {
        var root = RepoRoot();

        var editorPath = Path.Combine(
            root.FullName, "Ben.Web.Library", "Organization", "Roles", "OrgRoleEditor.razor");
        Assert.True(File.Exists(editorPath), $"The role editor was not where this test expects: {editorPath}");

        var offered = TableReference.Matches(File.ReadAllText(editorPath))
            .Select(m => m.Groups["table"].Value)
            .ToHashSet();

        var apiRoot = Path.Combine(root.FullName, "Ben.Data.WebApi");
        var checkedTables = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => TableReference.Matches(File.ReadAllText(f)))
            .Select(m => m.Groups["table"].Value)
            .Where(name => Enum.TryParse<OrganizationSecurityTable>(name, out _))
            .ToHashSet();

        // One-directional on purpose: the editor may offer a permission nothing enforces yet
        // (harmless — an unused grant), but it must never enforce one it cannot offer.
        var ungrantable = checkedTables.Except(offered).OrderBy(t => t).ToList();

        Assert.True(
            ungrantable.Count == 0,
            "The WebApi checks these permissions, but OrgRoleEditor.razor offers no way to grant "
            + $"them, so no role can ever hold one: {string.Join(", ", ungrantable)}. Add a "
            + "PermissionSection row for each.");
    }
}
