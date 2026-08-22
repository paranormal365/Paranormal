using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The number of places that turn a refused request into an empty list must only ever go down.
/// </summary>
/// <remarks>
/// <para><b>What this is guarding.</b> <c>GetAsync</c> returns <c>default</c> for any non-2xx, so
/// an adapter method ending in <c>?? []</c> hands a component the same value for a 403, a 500 and a
/// genuinely empty list. Every list surface then says "No records available", and the page tells
/// somebody their group is empty when the server actually refused them. Item 120, and the shared
/// cause of three bugs on 2026-08-20 plus item 126 — where two admin pages rendered blank on the
/// deployment for a day, saying nothing at all about the 404s behind them.</para>
///
/// <para><b>Why a count and not a ban.</b> There were 120 of these when the conversion started
/// (101 after organization, 81 after case, 67 after platform, 45 after equipment,
/// 34 after user, 26 after investigation), and
/// converting one means changing its consumers too, so a hard ban would mean a single unmergeable
/// change touching hundreds of call sites. A ratchet lets the work land in pieces while making the
/// one thing that matters impossible: adding a new one. If this test fails because the number went
/// UP, do not raise the number — convert the method instead. If it fails because the number went
/// DOWN, lower it here; that is the conversion working.</para>
///
/// <para><b>The replacement.</b> <c>WebApiClient.GetListAsync</c> returns
/// <see cref="Ben.Web.Services.WebApi.LoadResult{T}"/>, and <c>BenListState</c> renders its three
/// states. Converting a method means changing its return type, not adding a second method beside
/// it: a parallel <c>LoadXAsync</c> for each <c>GetXAsync</c> would double the interface and leave
/// every old method in place as the trap it already is.</para>
/// </remarks>
public sealed class SwallowedFailureRatchetTests
{
    /// <summary>
    /// The count as it stands. Lower it as methods are converted; never raise it.
    /// </summary>
    private const int Ceiling = 26;

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Strips comments before counting. This codebase has now had four source-scanning guards fire
    /// on their own explanatory prose — including, twice, on the sentence describing the very thing
    /// being banned. A scanner that reads comments measures the documentation, not the code.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join('\n', withoutBlocks
            .Split('\n')
            .Select(line =>
            {
                var slashes = line.IndexOf("//", StringComparison.Ordinal);
                return slashes >= 0 ? line[..slashes] : line;
            }));
    }

    private static (int Total, List<string> PerFile) CountSwallows()
    {
        var adapters = Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot().FullName, "Ben.Web.Services", "WebApi"),
                "BenAdminClientAdapter.*.cs",
                SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        Assert.NotEmpty(adapters);

        var total = 0;
        var perFile = new List<string>();

        foreach (var file in adapters)
        {
            var count = System.Text.RegularExpressions.Regex
                .Matches(StripComments(File.ReadAllText(file)), @"\?\?\s*\[\]")
                .Count;

            if (count > 0) perFile.Add($"{Path.GetFileName(file)}: {count}");
            total += count;
        }

        return (total, perFile);
    }

    [Fact]
    public void The_number_of_swallowed_failures_never_grows()
    {
        var (total, perFile) = CountSwallows();

        Assert.True(
            total <= Ceiling,
            $"""
             Swallowed failures went UP: {total}, ceiling {Ceiling}.

             A new `?? []` turns a refused request into "No records available" — the page states
             something untrue in the voice it uses for genuinely empty things. Convert the method
             to return LoadResult<T> via GetListAsync and render it with BenListState, rather than
             raising the ceiling.

             Per file:
               {string.Join("\n  ", perFile)}
             """);
    }

    [Fact]
    public void The_ceiling_is_kept_tight_as_methods_are_converted()
    {
        var (total, _) = CountSwallows();

        // A ceiling left above the real number stops ratcheting: someone adds one back and the
        // test still passes. The two assertions together mean the number can only ever fall.
        Assert.True(
            total == Ceiling,
            $"Swallowed failures are down to {total} but the ceiling is still {Ceiling}. "
          + $"Lower Ceiling to {total} to lock the progress in.");
    }
}
