using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Keeps Telerik's segmented date, date-and-time and time pickers out of the site (item 224).
/// </summary>
/// <remarks>
/// <para>They turn an impossible part into a different real one, silently: typing 0-9-3-1 gives 09/01, and 1-3 in a
/// 12-hour hour gives 03 PM. Measured 2026-09-15 on Telerik 14.1 and 15.0.1; <c>AutoCorrectParts="false"</c> leaves a
/// red field that never saves and moves the next digits into the year. <c>BenDateField</c> reads the typed text and
/// refuses an impossible date with a sentence.</para>
/// <para><c>TelerikCalendar</c>, the month grid inside <c>BenDateField</c>, is not a typed input and stays allowed.</para>
/// </remarks>
public sealed class BenDateFieldGuardTests
{
    private static readonly string[] Banned = ["<TelerikDatePicker", "<TelerikDateTimePicker", "<TelerikTimePicker", "<TelerikDateInput"];

    [Fact]
    public void No_razor_file_uses_a_Telerik_date_or_time_picker()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var sep = Path.DirectorySeparatorChar;
        var files = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(root!.FullName, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .ToList();

        var offenders = files
            .Where(f => Banned.Any(tag => File.ReadAllText(f).Contains(tag, StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(root!.FullName, f))
            .ToList();

        // A scan that reads nothing must not pass: the layout moving would otherwise turn this guard green forever.
        Assert.True(files.Count > 100, $"Only {files.Count} .razor files were scanned — has the layout moved?");
        Assert.True(files.Any(f => File.ReadAllText(f).Contains("<BenDateField", StringComparison.Ordinal)),
            "No BenDateField found anywhere — the scan is not reading the pages it should.");
        Assert.True(offenders.Count == 0,
            "Telerik's date and time pickers turn an impossible day or hour into a different one without saying so. "
            + "Use <BenDateField Mode=\"DateEntryMode.Date|DateTime|Time\">. Found in:\n  " + string.Join("\n  ", offenders));
    }
}
