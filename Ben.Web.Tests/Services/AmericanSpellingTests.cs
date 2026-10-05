using Xunit;
using System.Text.RegularExpressions;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The site is written in American English (Ben, 2026-10-02: "All text in the site should be
/// written as American English").
/// </summary>
/// <remarks>
/// Scans the help guides and the changelog — the prose a visitor reads at length, where a
/// "catalogue" or a "colour" stands out. Words only, outside links and code spans, so an anchor
/// or a URL that still carries an old spelling (a route kept so old links work) is not reported.
/// </remarks>
public sealed class AmericanSpellingTests
{
    private static readonly string[] British =
    [
        "catalogue", "catalogues", "colour", "colours", "coloured", "programme", "programmes",
        "cancelled", "cancelling", "favourite", "favourites", "favour", "centre", "centres",
        "recognise", "recognised", "recognising", "fulfilment", "grey", "labelled", "organise",
        "organised", "organiser", "organisers", "organisation", "organisations", "behaviour",
        "honour", "metre", "metres", "licence", "neighbour", "neighbours", "neighbourhood",
        "enrolment", "authorise", "authorised", "summarise", "summarised", "optimise", "travelled",
        "travelling", "realise", "realised", "judgement", "acknowledgement", "analyse", "defence",
        "apologise", "whilst", "amongst", "learnt", "sanitise", "sanitised", "modelled",
        "car park", "car parks",
    ];

    private static readonly Regex Word = new(
        @"(?<![A-Za-z0-9_/#-])(" + string.Join("|", British) + @")(?![A-Za-z0-9_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void Help_guides_and_the_changelog_are_in_American_English()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var found = new List<string>();
        foreach (var folder in new[] { "Ben.Web.Services/Help/Content", "Ben.Web.Services/Changelog/Content" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root!.FullName, folder), "*.md"))
            {
                var inFence = false;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("```")) { inFence = !inFence; continue; }
                    if (inFence) continue;
                    // links' targets and code spans are addresses and names, not prose
                    var prose = Regex.Replace(lines[i], @"`[^`]*`|\]\([^)]*\)|<[^>]+>", " ");
                    foreach (Match m in Word.Matches(prose))
                        found.Add($"{Path.GetFileName(file)}:{i + 1} \"{m.Value}\"");
                }
            }
        }

        Assert.True(found.Count == 0, "British spellings in the site's prose:\n" + string.Join("\n", found.Take(40)));
    }
}
