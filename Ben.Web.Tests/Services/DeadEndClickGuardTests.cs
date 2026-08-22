using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A click must always show something. The specific idiom that broke this — a click handler that
/// bails with a bare <c>return;</c> when an investigation has no case — is banned at the source.
/// </summary>
/// <remarks>
/// <para><b>The bug (2026-08-22).</b> Case-less investigations render as cards with
/// <c>cursor:pointer</c>, but their handlers read <c>if (x.CaseId is not { } caseId) return;</c>
/// — so the two seeded internal Bell Witch visits simply ignored every click, on three separate
/// surfaces (MyInvestigations twice, MyProfile's map, OrgInvestigations' own pins). Ben's rule:
/// "A link should always show something... even if it is a message explaining why it shows
/// nothing... or where to find it."</para>
///
/// <para><b>What to do instead.</b> A case-bound row opens its case page; a case-less row opens
/// the group's Investigations tab focused on itself:
/// <c>/organizations/{orgId}?tab=investigations&amp;inv={investigationId}</c> — or, when already
/// on that tab, focuses the row in place (<c>OrgInvestigations.FocusRow</c>). Branch on CaseId
/// to pick a destination; never to pick between navigating and nothing.</para>
///
/// <para>This is the narrow, mechanical half of the rule — it catches the idiom that actually
/// shipped, in .razor files where click handlers live. The broad half stays a habit; see also
/// <c>LoadResultRenderedGuardTests</c> for the sibling rule that a refusal must render.</para>
/// </remarks>
public sealed class DeadEndClickGuardTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>Strips razor and C# comments — seven guards here have fired on their own prose.</summary>
    private static string StripComments(string source)
    {
        var s = Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        s = Regex.Replace(s, @"(?<![\w""'])/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    /// <summary>
    /// The idiom: a null-pattern test on a CaseId whose failure arm is a bare <c>return;</c> on
    /// the same statement. Anchored to CaseId deliberately — that is the field whose nullability
    /// keeps producing this bug, and a wider net would drown in legitimate guards.
    /// </summary>
    private static readonly Regex DeadEnd = new(
        @"CaseId\s+is\s+not\s+\{\s*\}\s*\w*\s*\)\s*return\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void No_click_handler_swallows_a_caseless_investigation()
    {
        var root = RepoRoot().FullName;
        var offenders = new List<string>();

        foreach (var dir in new[] { "Ben.Web.Website.Library", "Ben.Web.Website" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.razor", SearchOption.AllDirectories)
                                      .OrderBy(f => f))
        {
            var lines = StripComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (DeadEnd.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
        }

        Assert.True(
            offenders.Count == 0,
            $$"""
             A case-less investigation click is being silently swallowed again:

               {{string.Join("\n  ", offenders)}}

             `CaseId is not { } … ) return;` makes the element a dead end: it renders as
             clickable and then ignores the click. A click must always show something. Branch on
             CaseId to choose a destination — the case page when there is one, otherwise
             /organizations/{orgId}?tab=investigations&inv={investigationId} (or FocusRow when
             already on that tab). All three of these were removed on 2026-08-22.
             """);
    }
}
