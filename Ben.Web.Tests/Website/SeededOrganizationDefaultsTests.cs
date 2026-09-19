using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Anything that creates an organization must give it the roles, ladder and duties every real
/// creation gives one.
/// </summary>
/// <remarks>
/// <para><b>The bug this exists to stop coming back.</b> Three seeders added
/// <c>Organization</c> rows and stopped there. Every genuine creation path —
/// <c>OrganizationController</c>, <c>AdminOrganizationController</c>,
/// <c>OrganizationSecurityService</c> — calls all three defaults, so the groups those seeders made
/// were unlike any group a person can actually create: no named roles, no title ladder, no duty
/// board.</para>
///
/// <para><b>Why nobody noticed for so long.</b> Standalone backfill seeders run early in startup
/// and cover organizations that exist at that moment; the ones created later are picked up by the
/// NEXT startup. So the defect is invisible on any database that has been started twice, which is
/// every database anybody uses, and it appears only on a genuinely first run. It cost most of a
/// session to find, through six e2e failures whose real message was
/// <c>Role 'Case Manager Role' not found — the default-role seed is missing</c>.</para>
///
/// <para><b>Source-scanned, because the alternative does not fit.</b> The honest test would run
/// the seeders against a database and inspect the result, but they take an
/// <c>IServiceProvider</c> and expect a configured host — a fixture bigger than the rule it would
/// check. Scanning is weaker and cheap, and it catches the exact mistake that was made: adding an
/// organization and forgetting what comes with one.</para>
/// </remarks>
public sealed class SeededOrganizationDefaultsTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>The three calls a new organization needs, by the names they are actually called by.</summary>
    private static readonly string[] RequiredCalls =
        ["AddDefaultLevels", "AddDefaultDuties", "AddDefaultRoles"];

    /// <summary>
    /// Comments are stripped before matching.
    /// </summary>
    /// <remarks>
    /// Guards in this repository have been defeated more than once by the prose written to explain
    /// them: a comment naming <c>AddDefaultRoles</c> would satisfy a naive scan and let a file
    /// that never calls it pass. The comment above this very class names all three.
    /// </remarks>
    private static string WithoutComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\n]*", " ");
    }

    private static IEnumerable<string> SeederFiles()
        => Directory.EnumerateFiles(
            Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi", "SeedData"), "*.cs");

    /// <summary>
    /// Every seeder that adds an organization also adds the three defaults.
    /// </summary>
    [Fact]
    public void A_seeder_that_creates_a_group_gives_it_what_a_real_group_gets()
    {
        var offenders = new List<string>();

        foreach (var path in SeederFiles())
        {
            var code = WithoutComments(File.ReadAllText(path));

            // "Adds an organization" means exactly that — an Organization entity handed to the
            // context. Mentioning the type is not enough: most seeders read organizations.
            if (!Regex.IsMatch(code, @"\bOrganizations\s*\.\s*Add\s*\(")) continue;

            var missing = RequiredCalls.Where(call => !code.Contains(call, StringComparison.Ordinal)).ToList();
            if (missing.Count > 0)
                offenders.Add($"{Path.GetFileName(path)} creates an organization but never calls "
                            + string.Join(", ", missing));
        }

        Assert.True(offenders.Count == 0,
            "A seeded group must be born like a real one — roles, title ladder and duty board:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The scan can actually see the seeders, so a green result means something.
    /// </summary>
    /// <remarks>
    /// Without this, a wrong path or a renamed folder would make the rule above pass by finding
    /// nothing at all — the failure mode of every source-scanning guard.
    /// </remarks>
    [Fact]
    public void The_scan_finds_the_seeders_that_create_groups()
    {
        var creators = SeederFiles()
            .Where(p => Regex.IsMatch(WithoutComments(File.ReadAllText(p)), @"\bOrganizations\s*\.\s*Add\s*\("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.NotEmpty(creators);
        Assert.Contains("DevelopmentDataSeeder.cs", creators);
        Assert.Contains("DevelopmentRosterSeeder.cs", creators);
    }

    /// <summary>
    /// Comment-stripping works, so prose naming the calls cannot satisfy the rule.
    /// </summary>
    [Fact]
    public void A_comment_naming_the_calls_does_not_count()
    {
        const string code = """
            // This one really ought to call AddDefaultRoles one day.
            /* and AddDefaultLevels and AddDefaultDuties too */
            db.Organizations.Add(org);
            """;

        var stripped = WithoutComments(code);

        Assert.DoesNotContain("AddDefaultRoles", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDefaultLevels", stripped, StringComparison.Ordinal);
        Assert.Matches(@"\bOrganizations\s*\.\s*Add\s*\(", stripped);
    }
}
