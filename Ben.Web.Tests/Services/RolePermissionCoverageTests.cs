using Ben.Data.Common.Enums;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every organization-scoped permission is assignable from the role editor (backlog item #83).
/// </summary>
/// <remarks>
/// <para>The role editor's <c>Sections</c> list is hand-written, and a permission missing from it is
/// invisible rather than broken: the enum value exists, the server enforces it, and no role can ever
/// be given it. That failure is silent in exactly the way the reserved-slug one was — everything
/// compiles, every test passes, and the capability simply does not exist for anybody.</para>
///
/// <para><b>User-scoped tables are deliberately excluded.</b> An organization role has no business
/// granting rights over somebody's own profile, addresses or messages, so those fourteen values are
/// not oversights. Naming the exclusion by prefix rather than listing ids means a new
/// <c>User…</c> table is excluded automatically and a new org-scoped one is not.</para>
/// </remarks>
public sealed class RolePermissionCoverageTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string RoleEditorSource()
        => File.ReadAllText(Path.Combine(
            RepoRoot().FullName, "Ben.Web.Library", "Organization", "Roles", "OrgRoleEditor.razor"));

    /// <summary>
    /// Values an organization role is not meant to grant.
    /// </summary>
    /// <remarks>
    /// The <c>User…</c> prefix covers a person's own profile, contact details and messages.
    /// <c>AppUser</c> is the same idea without the prefix — and it is, as of 2026-08-17, referenced
    /// by nothing at all in the codebase. It is excluded rather than given a row on purpose: a
    /// toggle that grants nothing is worse than no toggle, because it tells a role-builder something
    /// untrue. Recorded in the backlog under item #83.
    /// </remarks>
    private static bool IsUserScoped(string name)
        => name.StartsWith("User", StringComparison.Ordinal)
        || name == "AppUser";

    private static HashSet<string> TablesInEditor()
    {
        var block = Regex.Match(RoleEditorSource(), @"Sections =\s*\[(.*?)\n    \];", RegexOptions.Singleline);
        Assert.True(block.Success, "Could not find the Sections list — this test has stopped proving anything.");

        return [.. Regex.Matches(block.Groups[1].Value, @"OrganizationSecurityTable\.(\w+)")
            .Select(m => m.Groups[1].Value)];
    }

    /// <summary>
    /// Adding an org-scoped permission without adding a row here makes it unassignable for ever.
    /// </summary>
    [Fact]
    public void Every_organization_scoped_permission_can_be_assigned_to_a_role()
    {
        var inEditor = TablesInEditor();

        var missing = Enum.GetNames<OrganizationSecurityTable>()
            .Where(name => !IsUserScoped(name))
            .Where(name => !inEditor.Contains(name))
            .ToList();

        Assert.True(missing.Count == 0,
            "These organization-scoped permissions have no row in OrgRoleEditor, so no role can ever "
            + "be granted them: " + string.Join(", ", missing)
            + ". Add a PermissionSection — with a description — for each.");
    }

    /// <summary>
    /// The editor offers nothing that is not a real permission, so a rename cannot leave a dead row.
    /// </summary>
    [Fact]
    public void The_editor_offers_no_permission_that_does_not_exist()
    {
        var known = Enum.GetNames<OrganizationSecurityTable>().ToHashSet(StringComparer.Ordinal);

        var unknown = TablesInEditor().Where(t => !known.Contains(t)).ToList();

        Assert.True(unknown.Count == 0,
            "These rows name something that is not an OrganizationSecurityTable value: "
            + string.Join(", ", unknown));
    }

    /// <summary>
    /// Every row explains itself. The whole point of item #83 was that "Files" and "Investigations"
    /// told a role-builder nothing about what the toggle actually grants.
    /// </summary>
    [Fact]
    public void Every_row_carries_a_description()
    {
        var block = Regex.Match(RoleEditorSource(), @"Sections =\s*\[(.*?)\n    \];", RegexOptions.Singleline)
            .Groups[1].Value;

        // Each entry is new("Name", Table, Parent, "Description") — so a row with a description has
        // two quoted strings. One means somebody added a row and left the explanation off.
        var thin = Regex.Matches(block, @"new\((.*?)\),\s*(?=new\(|$)", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .Where(entry => Regex.Matches(entry, "\"").Count < 4)
            .ToList();

        Assert.True(thin.Count == 0,
            "These permission rows have no description: " + string.Join(" | ", thin));
    }
}
