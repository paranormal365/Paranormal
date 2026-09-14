using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A weekend is several entries in one calendar file, not one long block (item 235 phase 2.3).
/// </summary>
/// <remarks>
/// A guest who booked Friday and Saturday wants two nights in their diary, each naming the room
/// they are in, so that looking at Saturday tells them where they are sleeping. One entry spanning
/// both would sit across the whole weekend and say nothing about either night.
/// </remarks>
public sealed class IcsBuilderMultiEventTests
{
    private static IcsBuilder.IcsEvent Night(string uid, int day, string room) => new(
        Uid: uid,
        StartUtc: new DateTime(2026, 10, day, 18, 0, 0, DateTimeKind.Utc),
        // Offset from the start rather than built from day + 1: the last night of October is the
        // 31st, and a weekend running into November is exactly the case a hotel books.
        EndUtc: new DateTime(2026, 10, day, 18, 0, 0, DateTimeKind.Utc).AddHours(16),
        Summary: $"Halloween Lock-In — {room}",
        Location: "The Thomas House Hotel");

    [Fact]
    public void Two_nights_are_two_entries_in_one_file()
    {
        var text = IcsBuilder.Build([
            Night("a@ishaunted.com", 30, "Blue Room"),
            Night("b@ishaunted.com", 31, "The Suite"),
        ]);

        Assert.Equal(2, Occurrences(text, "BEGIN:VEVENT"));
        Assert.Equal(2, Occurrences(text, "END:VEVENT"));
        // One calendar wrapping them, not two files glued together.
        Assert.Equal(1, Occurrences(text, "BEGIN:VCALENDAR"));
        Assert.Equal(1, Occurrences(text, "END:VCALENDAR"));
    }

    [Fact]
    public void Each_night_keeps_its_own_identity_and_its_own_room()
    {
        // Sharing one uid across the nights would make every night overwrite the last, and a guest
        // with a three-night stay would end up holding one entry.
        var text = IcsBuilder.Build([
            Night("a@ishaunted.com", 30, "Blue Room"),
            Night("b@ishaunted.com", 31, "The Suite"),
        ]);

        Assert.Contains("UID:a@ishaunted.com", text);
        Assert.Contains("UID:b@ishaunted.com", text);
        Assert.Contains("Blue Room", text);
        Assert.Contains("The Suite", text);
    }

    [Fact]
    public void One_entry_still_reads_exactly_as_it_always_did()
    {
        // The tour mail sends a single walk through the same builder, and nothing about that file
        // may change: a calendar that saw a different shape would add a duplicate walk.
        var single = Night("a@ishaunted.com", 30, "Blue Room");

        Assert.Equal(IcsBuilder.Build(single), IcsBuilder.Build([single]));
        Assert.Equal(1, Occurrences(IcsBuilder.Build(single), "BEGIN:VEVENT"));
    }

    [Fact]
    public void A_file_with_no_entries_is_still_a_calendar_rather_than_nonsense()
    {
        var text = IcsBuilder.Build([]);

        Assert.Contains("BEGIN:VCALENDAR", text);
        Assert.Contains("END:VCALENDAR", text);
        Assert.Equal(0, Occurrences(text, "BEGIN:VEVENT"));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }
}
