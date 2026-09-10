using System.Text;
using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The calendar file a tour guest is sent (item 233).
/// </summary>
/// <remarks>
/// A calendar file fails in one of two ways: a client refuses it outright, or — far worse — it
/// accepts it and puts the walk in the wrong place or at the wrong time. These pin the second
/// kind: the escaping that keeps an address in one piece, the UTC stamps, and the uid that makes
/// a second mail update the entry instead of adding a duplicate.
/// </remarks>
public sealed class IcsBuilderTests
{
    private static IcsBuilder.IcsEvent Sample(string? description = null, string? location = null) =>
        new(
            Uid: "6c1b69fd-0957-4f2a-8330-f47beb746b4b@ishaunted.com",
            StartUtc: new DateTime(2026, 9, 13, 0, 8, 0, DateTimeKind.Utc),
            EndUtc: new DateTime(2026, 9, 13, 1, 38, 0, DateTimeKind.Utc),
            Summary: "Printers Alley Ghost Walk",
            Description: description,
            Location: location ?? "1 Printers Alley, Nashville, TN, 37201",
            Url: "https://ishaunted.com/o/paw/tours/printers-alley-ghost-walk",
            OrganizerName: "Printers Alley Walks",
            OrganizerEmail: "walks@example.com",
            Latitude: 36.1628568m,
            Longitude: -86.7773829m,
            StampUtc: new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc));

    private static IReadOnlyList<string> Lines(string ics) => ics.Split("\r\n");

    [Fact]
    public void It_writes_one_event_a_calendar_will_open()
    {
        var ics = IcsBuilder.Build(Sample());
        var lines = Lines(ics);

        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Contains("VERSION:2.0", lines);
        Assert.Contains("BEGIN:VEVENT", lines);
        Assert.Contains("END:VEVENT", lines);
        Assert.Equal("END:VCALENDAR", lines[^2]);   // the file ends with a break
        // Every line ends CRLF, which the format requires and a bare \n quietly breaks.
        Assert.DoesNotContain(ics.Replace("\r\n", ""), c => c == '\n');
    }

    [Fact]
    public void Times_are_written_in_utc_so_no_timezone_definition_is_needed()
    {
        var lines = Lines(IcsBuilder.Build(Sample()));

        Assert.Contains("DTSTART:20260913T000800Z", lines);
        Assert.Contains("DTEND:20260913T013800Z", lines);
        Assert.Contains("DTSTAMP:20260910T200000Z", lines);
    }

    [Fact]
    public void A_local_start_time_is_converted_rather_than_written_as_it_stands()
    {
        // Everything in this product stores UTC, but a DateTime that arrives Unspecified or Local
        // must not be stamped with a Z it has not earned.
        var local = new DateTime(2026, 9, 13, 0, 8, 0, DateTimeKind.Utc).ToLocalTime();
        var ics = IcsBuilder.Build(Sample() with { StartUtc = local });

        Assert.Contains("DTSTART:20260913T000800Z", Lines(ics));
    }

    [Fact]
    public void The_uid_is_the_events_own_so_a_second_mail_updates_rather_than_duplicates()
    {
        var first = IcsBuilder.Build(Sample());
        var second = IcsBuilder.Build(Sample() with { Sequence = 1, StampUtc = DateTime.UtcNow });

        Assert.Contains("UID:6c1b69fd-0957-4f2a-8330-f47beb746b4b@ishaunted.com", Lines(first));
        Assert.Contains("UID:6c1b69fd-0957-4f2a-8330-f47beb746b4b@ishaunted.com", Lines(second));
        Assert.Contains("SEQUENCE:0", Lines(first));
        Assert.Contains("SEQUENCE:1", Lines(second));
    }

    [Fact]
    public void An_address_with_a_comma_stays_one_property()
    {
        // The failure this prevents: "1 Printers Alley, Nashville" read as two values, and a
        // calendar entry whose location is "1 Printers Alley".
        var lines = Lines(IcsBuilder.Build(Sample()));
        var location = Assert.Single(lines, l => l.StartsWith("LOCATION:", StringComparison.Ordinal));

        Assert.Equal(@"LOCATION:1 Printers Alley\, Nashville\, TN\, 37201", location);
    }

    [Fact]
    public void Semicolons_newlines_and_backslashes_are_escaped_and_in_the_right_order()
    {
        var ics = IcsBuilder.Build(Sample(
            description: "Meet by the arch; wear boots.\nA back\\slash too.",
            location: "Somewhere"));

        var description = Lines(ics).First(l => l.StartsWith("DESCRIPTION:", StringComparison.Ordinal));

        Assert.Contains(@"arch\; wear", description);
        Assert.Contains(@"boots.\nA back", description);
        // The backslash was escaped FIRST, so it is one escaped backslash rather than the start
        // of some other escape.
        Assert.Contains(@"back\\slash", description);
    }

    [Fact]
    public void A_long_description_is_folded_and_never_splits_a_character()
    {
        var ics = IcsBuilder.Build(Sample(
            description: string.Concat(Enumerable.Repeat("An evening walk — with an em dash. ", 12))));

        foreach (var line in Lines(ics).Where(l => l.Length > 0))
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75,
                $"line is {Encoding.UTF8.GetByteCount(line)} octets: {line}");

        // Unfolding is: remove every CRLF-space. Do it, and the text comes back whole — which is
        // the property that fails when a fold lands inside a multi-byte character.
        var unfolded = ics.Replace("\r\n ", "");
        Assert.Contains("An evening walk — with an em dash.", unfolded);
    }

    [Fact]
    public void The_meeting_point_travels_as_coordinates_too()
    {
        // So a phone offers to navigate there, rather than searching for the words.
        Assert.Contains("GEO:36.162857;-86.777383", Lines(IcsBuilder.Build(Sample())));
    }

    [Fact]
    public void An_event_that_ends_before_it_starts_is_given_an_hour_rather_than_refused()
    {
        var ics = IcsBuilder.Build(Sample() with
        {
            EndUtc = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
        });

        Assert.Contains("DTEND:20260913T010800Z", Lines(ics));
    }

    [Fact]
    public void The_bytes_are_utf8_and_the_content_type_says_what_they_are()
    {
        var bytes = IcsBuilder.BuildBytes(Sample(description: "An em dash — here"));

        Assert.Equal(IcsBuilder.Build(Sample(description: "An em dash — here")),
                     Encoding.UTF8.GetString(bytes));
        Assert.Contains("text/calendar", IcsBuilder.ContentType);
        Assert.Contains("method=PUBLISH", IcsBuilder.ContentType);
    }
}
