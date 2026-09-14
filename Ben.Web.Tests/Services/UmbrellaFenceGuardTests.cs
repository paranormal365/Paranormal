using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The calendar row behind a hosted event is a reflection, and only bookings write it
/// (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> A hosted event carries an ordinary calendar row so that every
/// existing list, reminder, share card and phone screen keeps working without knowing what a hosted
/// event is. The attendees on that row are derived from the bookings. Two other screens could write
/// them directly — the generic public RSVP path, which the shipped phone calls, and the calendar's
/// own attendee management — and both did.</para>
///
/// <para><b>What that produced.</b> Somebody Accepted on the door's list with no booking behind
/// them: no room, no pass, nothing the venue ever agreed to, and counted in every total. And in the
/// other direction, declining through the calendar left a CONFIRMED booking — a room catered and
/// staffed against — pointing at an attendee who says they are not coming.</para>
///
/// <para><b>A source scan, because a fence is a shape.</b> Each endpoint is correct for an ordinary
/// calendar event and wrong only for a hosted one, so no test of any single endpoint sees it. What
/// is being held is "every mutating path asks first", and only something that reads all of them can
/// hold that.</para>
/// </remarks>
public sealed class UmbrellaFenceGuardTests
{
    /// <summary>Every endpoint that changes who is attending, and must ask first.</summary>
    /// <remarks>
    /// <para>Reads are deliberately absent. Refusing to READ a hosted event's attendee list would
    /// take it off the ordinary calendar screen for no benefit at all — the harm is in changing
    /// it.</para>
    ///
    /// <para><b>The verb is part of the key.</b> Matching the route alone found
    /// <c>[HttpGet("{eventId:guid}/attendees")]</c> first and reported the unfenced read as an
    /// offence, which is a guard failing on the one endpoint that is right.</para>
    /// </remarks>
    private static readonly (string File, string[] Endpoints)[] Fenced =
    [
        ("PublicEventController.cs",
            ["[HttpPost(\"{eventId:guid}/rsvp\")]"]),
        ("OrgCalendarController.cs",
            [
                "[HttpPost(\"{eventId:guid}/attendees/by-email\")]",
                "[HttpPost(\"{eventId:guid}/attendees\")]",
                "[HttpPut(\"{eventId:guid}/attendees/{attendeeId:guid}/rsvp\")]",
                "[HttpDelete(\"{eventId:guid}/attendees/{attendeeId:guid}\")]",
                "[HttpPost(\"{eventId:guid}/attendees/{attendeeId:guid}/approve\")]",
                "[HttpPost(\"{eventId:guid}/attendees/{attendeeId:guid}/turn-down\")]",
            ]),
    ];

    /// <summary>The words that mean "this one asked".</summary>
    private static readonly string[] Asks =
    [
        "WhyThisIsBookedElsewhereAsync",
        "WhyThisBelongsToTheEvent",
        "WhyThisRowBelongsToAnEventAsync",
    ];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>One endpoint's body: from its route attribute to the next one.</summary>
    private static string? BodyOf(string source, string route)
    {
        var at = source.IndexOf(route, StringComparison.Ordinal);
        if (at < 0) return null;

        var next = source.IndexOf("\n    [Http", at + route.Length, StringComparison.Ordinal);
        return next < 0 ? source[at..] : source[at..next];
    }

    [Fact]
    public void Every_path_that_changes_who_is_attending_asks_whether_it_may()
    {
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));
        var offences = new List<string>();

        foreach (var (fileName, endpoints) in Fenced)
        {
            var file = api.EnumerateFiles(fileName, SearchOption.AllDirectories).FirstOrDefault();
            Assert.True(file is not null,
                $"{fileName} is guarded here but no longer exists. Update this list.");

            var source = File.ReadAllText(file!.FullName);

            foreach (var route in endpoints)
            {
                var body = BodyOf(source, route);
                if (body is null)
                {
                    offences.Add($"{fileName}: no endpoint at {route} any more");
                    continue;
                }

                if (!Asks.Any(ask => body.Contains(ask, StringComparison.Ordinal)))
                    offences.Add($"{fileName}: {route} changes attendees without asking whether "
                               + "the row belongs to a hosted event");
            }
        }

        Assert.True(offences.Count == 0,
            "A hosted event's attendees are written by BookingTransitions and by nothing else. "
            + "These would write them directly, which leaves the calendar row and the booking "
            + "saying different things.\n  " + string.Join("\n  ", offences));
    }

    [Fact]
    public void The_refusals_send_somebody_somewhere_rather_than_only_saying_no()
    {
        // A host who found the calendar screen is trying to do something reasonable. "No" on its
        // own leaves them pressing the same button harder.
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));

        var calendar = api.EnumerateFiles("OrgCalendarController.cs", SearchOption.AllDirectories)
            .First();
        Assert.Contains("Bookings page", File.ReadAllText(calendar.FullName));

        var publicEvents = api.EnumerateFiles("PublicEventController.cs", SearchOption.AllDirectories)
            .First();
        Assert.Contains("the event's own page", File.ReadAllText(publicEvents.FullName));
    }
}
