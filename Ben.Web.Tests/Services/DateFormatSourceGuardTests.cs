using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// No source file may hand-type a day-first date format.
/// </summary>
/// <remarks>
/// <para><b>Why a source scan and not a unit test.</b> <c>DisplayDateFormatTests</c> already pins
/// the shared constants, and it passed the entire time the site was showing British dates. It
/// could only ever assert what referred to those constants, and the places that got it wrong did
/// not: a Telerik <c>DatePicker</c> takes <c>Format="dd/MM/yyyy"</c> as a string attribute, and a
/// grid column takes <c>DisplayFormat="{0:dd/MM/yyyy}"</c>. Seventy-four of those existed across
/// twenty-eight files while the constants sat in one file saying month-first.</para>
///
/// <para><b>The cost of not having this.</b> Ben reported day-first dates four separate times. On
/// the fourth he was looking at "Date Created" columns, and he was right on all four — each
/// earlier fix corrected the constants, or one screen, and left the rest. Nothing in the build
/// disagreed, because nothing was looking.</para>
///
/// <para><b>ISO is not day-first and is deliberately allowed.</b> <c>yyyy-MM-dd</c> is
/// unambiguous, sorts correctly, and is what <c>&lt;input type="date"&gt;</c>, log lines and
/// generated filenames require. This guard is about the order of day and month for a human
/// reader, not about banning format strings.</para>
/// </remarks>
public sealed class DateFormatSourceGuardTests
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
    /// Strips comments first. Four guards in this codebase have now fired on their own prose —
    /// twice on the sentence describing the very thing being banned — and this file necessarily
    /// spells out "dd/MM/yyyy" to explain itself.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return string.Join('\n', withoutBlocks.Split('\n').Select(line =>
        {
            var razorComment = line.IndexOf("@*", StringComparison.Ordinal);
            if (razorComment >= 0) line = line[..razorComment];

            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    private static IEnumerable<FileInfo> Sources()
    {
        var root = RepoRoot();
        string[] projects =
        [
            "Ben.Web.Website", "Ben.Web.Website.Library", "Ben.Web.Services",
            "Ben.Data.WebApi", "Ben.Video.Editor", "Ben.Wasm.Video",
        ];

        return projects
            .Select(p => new DirectoryInfo(Path.Combine(root.FullName, p)))
            .Where(d => d.Exists)
            .SelectMany(d => d.EnumerateFiles("*.*", SearchOption.AllDirectories))
            .Where(f => f.Extension is ".cs" or ".razor")
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    /// <summary>Day before month, in every spelling that has actually turned up here.</summary>
    private static readonly (string Name, Regex Pattern)[] DayFirst =
    [
        ("dd/MM",        new Regex(@"\bdd/MM\b")),
        ("d/M/yyyy",     new Regex(@"\bd/M/yyyy\b")),
        ("dd-MM",        new Regex(@"\bdd-MM\b")),
        ("dd.MM",        new Regex(@"\bdd\.MM\b")),
        ("d MMM",        new Regex(@"(?<![A-Za-z])d+ MMM")),
        ("dddd d MMMM",  new Regex(@"dddd,? d MMMM")),
    ];

    [Fact]
    public void No_source_file_hand_types_a_day_first_date_format()
    {
        var offences = new List<string>();

        foreach (var file in Sources())
        {
            var text = StripComments(File.ReadAllText(file.FullName));

            foreach (var (name, pattern) in DayFirst)
            {
                foreach (Match m in pattern.Matches(text))
                {
                    var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                    offences.Add($"{file.Name}:{line} — {name}");
                }
            }
        }

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} day-first date format(s) found in source.

             This site is American: 08/04/2026 means August 4th. Use the constants on
             DateTimeViewerExtensions rather than typing a pattern — DatePattern,
             DateTimeNoSecondsPattern, MediumDatePattern, LongDatePattern, ChartDayPattern, or
             GridDateFormat / GridDateTimeFormat for a Telerik DisplayFormat.

             ISO (yyyy-MM-dd) is fine and deliberately not flagged.

               {string.Join("\n  ", offences.Take(40))}
             """);
    }

    /// <summary>
    /// The constants themselves, so the two halves of the rule cannot disagree.
    /// </summary>
    [Fact]
    public void The_shared_constants_are_month_first()
    {
        Assert.StartsWith("MM/dd/", Ben.Web.Services.DateTimeViewerExtensions.DatePattern);
        Assert.StartsWith("MM/dd/", Ben.Web.Services.DateTimeViewerExtensions.DateTimePattern);
        Assert.StartsWith("MM/dd/", Ben.Web.Services.DateTimeViewerExtensions.DateTimeNoSecondsPattern);
        Assert.StartsWith("MMM", Ben.Web.Services.DateTimeViewerExtensions.MediumDatePattern);
        Assert.StartsWith("MMMM", Ben.Web.Services.DateTimeViewerExtensions.LongDatePattern);
        Assert.StartsWith("MMM", Ben.Web.Services.DateTimeViewerExtensions.ChartDayPattern);
        Assert.Equal("{0:MM/dd/yyyy}", Ben.Web.Services.DateTimeViewerExtensions.GridDateFormat);
        Assert.Equal("{0:MM/dd/yyyy hh:mm tt}", Ben.Web.Services.DateTimeViewerExtensions.GridDateTimeFormat);
    }
}
