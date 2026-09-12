using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The rooms page keeps the three things a booking plan reads, and keeps them on a phone.
/// </summary>
/// <remarks>
/// <para><b>Item 235 phase 1.</b> Sleeps, Bookable and Beds were on the API from phase 2.1 and
/// write-only until <c>PlaceRoomsManager.razor</c> learned them. Three rules pin what that screen
/// does, each one the shape of a mistake that would not look like one:</para>
///
/// <para><b>Every cell names its column.</b> Below 576px the table reflows to stacked cards and the
/// heading row is hidden; a <c>&lt;td&gt;</c> without <c>data-label</c> shows a bare "2" with nothing
/// to say it is a capacity. This is the org side's first deliberate reflow and the idiom every later
/// one copies, so the rule is checked at the source rather than left to somebody noticing on a phone.</para>
///
/// <para><b>The row's Save sends the booking fields by name, and a blank Sleeps as ClearCapacity.</b>
/// The server treats a null on any post-first-release field as "leave it alone" — the right rule for
/// a screen that edits one thing, and the wrong one for this row, where an emptied Sleeps box means
/// the venue has gone back to "we have not said". A later edit that drops back to the four-argument
/// form would compile, save, show no error, and change nothing a guest reads.</para>
///
/// <para><b>The public toggle sends only the public flag.</b> The mirror image: a one-button change
/// that re-sent the row's copies of Sleeps, Bookable and Beds would write whatever the list happened
/// to be told, and a list that ever omitted a field would then wipe it on every Make public.</para>
///
/// <para>Label association is covered site-wide by <see cref="LabelAssociationTests"/>, which skips
/// any <c>for=</c> containing a Razor expression. The per-row inputs here are exactly that shape
/// (<c>room-sleeps-@room.Id</c>), so this file checks them literally: the string after <c>for=</c>
/// must appear verbatim as an <c>id=</c> in the same file.</para>
/// </remarks>
public sealed class PlaceRoomsManagerSourceTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Source() => File.ReadAllText(Path.Combine(
        RepoRoot().FullName, "Ben.Web.Website.Library", "Organization", "PlaceRoomsManager.razor"));

    /// <summary>The rooms table alone, so a cell in some other table on the page is not blamed.</summary>
    private static string RoomsTable(string source)
    {
        var open = source.IndexOf("data-testid=\"rooms-table\"", StringComparison.Ordinal);
        Assert.True(open >= 0, "The rooms table lost its data-testid; the Playwright test finds it by that.");
        var close = source.IndexOf("</table>", open, StringComparison.Ordinal);
        Assert.True(close > open, "The rooms table never closes.");
        return source[open..close];
    }

    /// <summary>The body of one private method in the @code block, by name.</summary>
    private static string MethodBody(string source, string name)
    {
        var m = Regex.Match(source, @"private\s+(?:async\s+)?[\w<>?\[\]\.]+\s+" + Regex.Escape(name) + @"\s*\(");
        Assert.True(m.Success, $"{name} is no longer on the page. If it was renamed, rename it here too.");

        var start = source.IndexOf('{', m.Index + m.Length);
        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..i];
        }
        throw new InvalidOperationException($"{name} never closes.");
    }

    [Fact]
    public void Every_cell_in_the_rooms_table_names_its_column()
    {
        var table = RoomsTable(Source());
        var cells = Regex.Matches(table, @"<td\b[^>]*>").Select(m => m.Value).ToList();

        Assert.True(cells.Count >= 8, $"only {cells.Count} cells found — has the table been restructured?");

        var unlabelled = cells.Where(c => !Regex.IsMatch(c, @"\bdata-label=""[^""]+""")).ToList();
        Assert.True(unlabelled.Count == 0,
            "Every <td> in the rooms table needs data-label=\"<column>\": below 576px the heading row "
            + "is hidden and td::before shows the label instead. Missing on:\n  "
            + string.Join("\n  ", unlabelled));
    }

    [Fact]
    public void Every_label_on_the_page_names_a_control_in_the_same_file()
    {
        var source = Source();
        var ids = new HashSet<string>(
            Regex.Matches(source, @"\bid=""([^""]+)""").Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        var orphans = Regex.Matches(source, @"\bfor=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(target => !ids.Contains(target))
            .ToList();

        Assert.True(orphans.Count == 0,
            "A for= that names no id= in PlaceRoomsManager.razor (per-row ids must match character "
            + "for character, Razor expression included):\n  " + string.Join("\n  ", orphans));
    }

    [Fact]
    public void The_row_save_sends_every_booking_field_by_name()
    {
        var body = MethodBody(Source(), "SaveRowAsync");
        var call = Regex.Match(body, @"UpdatePlaceRoomAsync\((?:[^;])*\);", RegexOptions.Singleline);
        Assert.True(call.Success, "SaveRowAsync no longer calls UpdatePlaceRoomAsync.");

        foreach (var named in new[] { "Capacity:", "IsBookable:", "BedNote:", "ClearCapacity:" })
            Assert.True(call.Value.Contains(named, StringComparison.Ordinal),
                $"SaveRowAsync's UpdatePlaceRoomAsync call does not pass {named} by name. The server "
                + "leaves a null field alone, so the four-argument form saves without error and "
                + "silently keeps the old Sleeps, Bookable and Beds. Call:\n" + call.Value);
    }

    [Fact]
    public void The_public_toggle_sends_only_the_public_flag()
    {
        var body = MethodBody(Source(), "TogglePublicAsync");
        var call = Regex.Match(body, @"UpdatePlaceRoomAsync\((?:[^;])*\);", RegexOptions.Singleline);
        Assert.True(call.Success, "TogglePublicAsync no longer calls UpdatePlaceRoomAsync.");

        foreach (var named in new[] { "Capacity:", "IsBookable:", "BedNote:", "ClearCapacity:" })
            Assert.False(call.Value.Contains(named, StringComparison.Ordinal),
                $"TogglePublicAsync passes {named}. Make public changes one flag and must send one "
                + "flag; re-sending the row's copy of a booking field writes whatever the list was "
                + "told, and would wipe it the day the list omits one. Call:\n" + call.Value);
    }
}
