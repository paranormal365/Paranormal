using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A refused request must never be turned into an empty list. This is now a ban, not a count.
/// </summary>
/// <remarks>
/// <para><b>What it forbids.</b> <c>GetAsync</c> answers any non-2xx with <c>default</c>, so
/// <c>?? []</c> after it hands a component the same value for a 403, a 500 and a genuinely empty
/// list. Every list surface then says "No records available", and the page tells somebody their
/// group is empty when the server actually refused them. Item 120, and the shared cause of three
/// bugs on 2026-08-20 plus the admin pages that rendered blank on the deployment for a day.</para>
///
/// <para><b>It used to be a ratchet.</b> There were <b>120</b> of these across every feature in the
/// product, and converting one means changing its consumers too, so a hard ban would have been a
/// single unmergeable change touching hundreds of call sites. A count that could only fall let the
/// work land in seven slices — organization, case, platform, equipment, user, investigation, and
/// the rest together — while making the one thing that mattered impossible: adding a new one.
/// The count reached zero on 2026-08-22, so the scaffolding comes down and the rule stands on its
/// own.</para>
///
/// <para><b>The replacement.</b> <c>GetListAsync</c> returns
/// <see cref="Ben.Web.Services.WebApi.LoadResult{T}"/>, and <c>BenListState</c> renders its four
/// states — loading, signed out, couldn't load, empty. <c>LoadResultRenderedGuardTests</c> is the
/// other half: this test stops a refusal being swallowed in the client, that one stops a page
/// ignoring a refusal it can now see.</para>
///
/// <para><b>If this fails, do not add an exception.</b> A list endpoint uses <c>GetListAsync</c>.
/// A mutation that returns a list — there are three — returns its own outcome alongside the list,
/// because "did this happen?" is a different question from "is this list real?".</para>
/// </remarks>
public sealed class SwallowedFailureRatchetTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Strips comments before scanning. Six guards in this codebase have now fired on their own
    /// explanatory prose — including, twice, on the sentence describing the very thing being
    /// banned. The lookbehind on <c>/*</c> keeps a MIME wildcard from swallowing the rest of a file.
    /// </summary>
    private static string StripComments(string source)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            source, @"(?<![\w""'])/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    /// <summary>
    /// The whole client, not just the adapter. The user slice found two methods whose swallows sat
    /// in <c>WebApiClient</c> itself and so never appeared in the old count — the ratchet measured
    /// one file pattern and was quietly reporting a floor rather than a total.
    /// </summary>
    /// <remarks>
    /// <c>LoadResult.cs</c> is excluded, and it is the only exclusion. Its
    /// <c>Items => _items ?? []</c> is the mechanism that makes this rule enforceable — the promise
    /// that <c>Items</c> is never null is what lets a call site adopt the type without becoming
    /// more dangerous. Banning it there would ban the cure along with the disease.
    /// </remarks>
    private static IEnumerable<string> ClientFiles() =>
        Directory.EnumerateFiles(
            Path.Combine(RepoRoot().FullName, "Ben.Web.Services", "WebApi"),
            "*.cs", SearchOption.AllDirectories)
        .Where(f => Path.GetFileName(f) != "LoadResult.cs");

    [Fact]
    public void No_client_code_turns_a_refusal_into_an_empty_list()
    {
        var offenders = new List<string>();

        foreach (var file in ClientFiles().OrderBy(f => f))
        {
            var lines = StripComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"\?\?\s*\[\]"))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
        }

        Assert.True(
            offenders.Count == 0,
            $"""
             A refusal is being turned into an empty list again:

               {string.Join("\n  ", offenders)}

             `?? []` after a fetch makes a 403, a 500 and an empty list indistinguishable, and the
             page then states something untrue in the voice it uses for genuinely empty things.
             Use GetListAsync, which returns LoadResult<T>, and render it with BenListState. All
             120 of these were removed on 2026-08-22; this is a ban, not a budget.
             """);
    }
}
