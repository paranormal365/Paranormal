using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A page that fetches a <c>LoadResult</c> must do something with the failure it can now see.
/// </summary>
/// <remarks>
/// <para><b>The half-conversion this exists to stop.</b> Changing an adapter method to return
/// <c>LoadResult&lt;T&gt;</c> makes a refusal visible; it does not make it <i>shown</i>. The
/// cheapest way to fix the compile error is <c>.Items</c> at the call site, which leaves the page
/// exactly as wrong as it was before — "No records available" over a 403 — while the ratchet in
/// <see cref="SwallowedFailureRatchetTests"/> happily records progress. Item 120's whole point is
/// the sentence on the screen, not the type in the adapter.</para>
///
/// <para><b>What it requires.</b> A <c>.razor</c> file that calls one of the converted case-area
/// methods must mention <c>BenListState</c> or read <c>.Failed</c> somewhere. That is a low bar on
/// purpose: it cannot check that the right thing is rendered, only that the failure was not
/// dropped on the floor without a decision.</para>
///
/// <para><b>Deliberate exceptions are listed, with the reason.</b> Some fetches really are
/// decorations, and a warning panel over a badge would be worse than the badge not appearing. Those
/// belong in <see cref="Decorations"/> where the choice is written down, rather than passing
/// silently because nobody looked.</para>
/// </remarks>
public sealed class LoadResultRenderedGuardTests
{
    /// <summary>The case-area methods converted to <c>LoadResult</c>.</summary>
    private static readonly string[] ConvertedMethods =
    [
        "GetCaseTransfersAsync", "GetPublicCasesAsync", "GetOrgCasesAsync",
        "GetOrgPendingRequestsAsync", "GetCaseTimelineAsync", "GetMyCaseReportsAsync",
        "GetCaseReportsAsync", "GetCaseResearchAsync", "GetCaseFilesAsync", "GetCaseNotesAsync",
        "GetMyClientRequestsAsync", "GetClientRequestOrgsAsync", "GetMyCasesAsync",
        "GetMyCaseMessagesAsync", "GetCoClientsAsync", "GetCaseInvitesAsync",
        "GetRelatedPeopleAsync", "GetVideoAssetsAsync", "GetCaseMessagesAsync",
        "GetCaseVoteSummariesAsync",
    ];

    /// <summary>
    /// Files that read one of these lists purely to decorate something, where the reader loses
    /// nothing they can act on if the fetch fails.
    /// </summary>
    private static readonly Dictionary<string, string> Decorations = new()
    {
        ["PublicCaseDiscovery.razor"] =
            "Vote summaries only mark which cards the viewer has already voted on. A failed lookup "
          + "leaves the marks off — the cases themselves still render, and a warning panel over a "
          + "badge would be a worse page than a missing badge.",
    };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Strips Razor and C# comments before scanning.
    /// </summary>
    /// <remarks>
    /// <para>Two traps, both hit while writing this. The first is the familiar one: a scanner that
    /// reads comments measures the documentation, so <c>@* … *@</c> and <c>//</c> go first.</para>
    ///
    /// <para>The second is new and cost a false accusation. A naive <c>/* … */</c> strip eats a
    /// file-input's <c>accept="image/*,audio/*,video/*"</c> and everything after it up to the next
    /// real <c>*/</c> — which in <c>MyCaseDetail.razor</c> deleted 700 lines including every
    /// failure branch, and the guard then reported the one page most carefully converted as the
    /// only offender. The lookbehind requires <c>/*</c> to start a token, so a MIME wildcard is
    /// left alone.</para>
    /// </remarks>
    private static string StripComments(string source)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            source, @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"(?<![\w""'])/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    private static IEnumerable<string> RazorFiles() =>
        new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(RepoRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void A_page_that_can_see_a_refusal_does_not_drop_it()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var name = Path.GetFileName(file);
            if (Decorations.ContainsKey(name)) continue;

            var source = StripComments(File.ReadAllText(file));

            var called = ConvertedMethods.Where(m => source.Contains(m, StringComparison.Ordinal)).ToList();
            if (called.Count == 0) continue;

            var handles = source.Contains("BenListState", StringComparison.Ordinal)
                       || source.Contains(".Failed", StringComparison.Ordinal);

            if (!handles) offenders.Add($"{name} — calls {string.Join(", ", called)}");
        }

        Assert.True(
            offenders.Count == 0,
            $"""
             These pages fetch a list that can report a refusal, and then ignore it:

               {string.Join("\n  ", offenders)}

             Render the failure — wrap the list in BenListState, or branch on .Failed where the
             list is mutated in place. If the fetch is genuinely a decoration, add the file to
             LoadResultRenderedGuardTests.Decorations with the reason, so the choice is on record.
             """);
    }

    /// <summary>
    /// An allowlist that outlives its entries stops guarding. Every exception must still be a file
    /// that exists and still calls one of these methods.
    /// </summary>
    [Fact]
    public void Every_declared_decoration_is_still_real()
    {
        var files = RazorFiles().ToList();

        foreach (var (name, reason) in Decorations)
        {
            var match = files.FirstOrDefault(f => Path.GetFileName(f) == name);
            Assert.True(match is not null, $"Decoration '{name}' no longer exists — remove it.");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"Decoration '{name}' has no reason.");

            var source = StripComments(File.ReadAllText(match!));
            Assert.True(
                ConvertedMethods.Any(m => source.Contains(m, StringComparison.Ordinal)),
                $"Decoration '{name}' no longer calls a converted method — remove it from the list.");
        }
    }
}
