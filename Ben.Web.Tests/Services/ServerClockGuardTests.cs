using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// No website code may read a time on the server's clock.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Under Blazor Server, <c>DateTime.ToLocalTime()</c> and <c>TimeZoneInfo.Local</c>
/// mean the machine the site runs on — nobody's clock in particular. The 08/11 sweep replaced 51 of
/// them with the viewer helpers; by 09/28 there were 64 again, in 37 files, and a person who had
/// chosen Pacific time on their profile would have read those pages in Central (Ben, 2026-09-28:
/// "if someone has configured their timezone, show date and time for their timezone").</para>
///
/// <para><b>What to use instead.</b> <c>ToViewerLocalTime(UserState)</c> for the reader's own
/// clock, or the item's zone (<c>Zones</c>, <c>EventClock</c>) for a case, investigation, event or
/// tour read in its local time.</para>
/// </remarks>
public sealed class ServerClockGuardTests
{
    private static readonly Regex ServerClock = new(@"\.ToLocalTime\s*\(|\bTimeZoneInfo\.Local\b");

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Comments out, anchored: "/*" only where it starts a token, so <c>accept="image/*"</c> is not
    /// read as a comment that swallows the rest of the file — the trap a sibling guard fell into.
    /// </summary>
    private static string StripComments(string source)
    {
        var noRazor = Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        var noBlocks = Regex.Replace(noRazor, @"(?<![\w""'/])/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', noBlocks.Split('\n').Select(line =>
        {
            var at = Regex.Match(line, @"(?<![:""'\w])//");
            return at.Success ? line[..at.Index] : line;
        }));
    }

    public static TheoryData<string> Projects() => ["Ben.Web.Website", "Ben.Web.Website.Library", "Ben.Web.Services"];

    [Theory]
    [MemberData(nameof(Projects))]
    public void Nothing_reads_the_servers_clock(string project)
    {
        var root = Path.Combine(RepoRoot().FullName, project);
        Assert.True(Directory.Exists(root), $"{project} is missing — the guard would pass by reading nothing");

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(f => StripComments(File.ReadAllText(f)).Split('\n')
                .Select((line, i) => (File: Path.GetRelativePath(root, f), Line: i + 1, Text: line.Trim()))
                .Where(x => ServerClock.IsMatch(x.Text)))
            .Select(x => $"{x.File}:{x.Line}: {x.Text}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These read the server's clock. Use ToViewerLocalTime(UserState), or the item's zone:\n"
            + string.Join('\n', offenders));
    }
}
