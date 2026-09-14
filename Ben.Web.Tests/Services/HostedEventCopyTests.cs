using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Starting the next event from the last one, and the bookings spreadsheet (item 235 phase 12).
/// </summary>
/// <remarks>
/// The source runs over the night the clocks go back in Nashville, and the copy is a fortnight later
/// on standard time — so a séance at 7 PM that came across as 6 PM or 8 PM would be the UTC arithmetic
/// this is meant to avoid.
/// </remarks>
public sealed class HostedEventCopyTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid HelperId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid FridayId = Guid.NewGuid();
    private static readonly Guid SaturdayId = Guid.NewGuid();
    private static readonly Guid BlueRoomId = Guid.NewGuid();

    private const string Nashville = "America/Chicago";

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (HostId, "Host"), (HelperId, "Helper"), (GuestId, "Guest") })
            db.Users.Add(new AppUser { Id = id, Email = $"{name}@example.test", UserName = $"{name}@example.test", DisplayName = name, DateCreated = now });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });

        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Séance Weekend", UrlName = "seance-weekend",
            TimeZoneId = Nashville, StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
            LifecycleState = HostedEventLifecycleState.Ended, LayoutKind = HostedEventLayoutKind.Rooms,
            BookingMode = HostedEventBookingMode.Pick, HoldMinutes = 600,
            VenueArrangement = HostedEventVenueArrangement.External, VenueContactName = "Mrs Thomas",
            VenueAgreedOnUtc = new DateTime(2026, 6, 1), VenueReference = "Letter of 06/01",
            GoNoGoDecision = HostedEventGoNoGo.Go, MinimumGuests = 10,
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = FridayId, HostedEventId = EventId, Date = new DateTime(2026, 10, 30), SortOrder = 0, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventNights.Add(new HostedEventNight { Id = SaturdayId, HostedEventId = EventId, Date = new DateTime(2026, 10, 31), SortOrder = 1, DateCreated = now, CreatedByAppUserId = HostId });

        db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit { Id = BlueRoomId, HostedEventId = EventId, Label = "Blue Room", Capacity = 2, Price = 180, SortOrder = 0, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock { Id = Guid.NewGuid(), HostedEventLayoutUnitId = BlueRoomId, HostedEventNightId = SaturdayId, Kind = HostedEventBlockKind.Blocked, Note = "Painting", DateCreated = now, CreatedByAppUserId = HostId });

        var menu = new HostedEventMenu { Id = Guid.NewGuid(), HostedEventNightId = SaturdayId, Title = "Dinner", DateCreated = now, CreatedByAppUserId = HostId };
        menu.Items.Add(new HostedEventMenuItem { Id = Guid.NewGuid(), HostedEventMenuId = menu.Id, Name = "Soup", DateCreated = now });
        menu.Items.Add(new HostedEventMenuItem { Id = Guid.NewGuid(), HostedEventMenuId = menu.Id, Name = "Pie", DateCreated = now });
        db.HostedEventMenus.Add(menu);

        // 7 PM on Saturday 10/31/2026, daylight time: midnight UTC.
        db.HostedEventSessions.Add(new HostedEventSession
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Title = "The séance", StartsAtUtc = new DateTime(2026, 11, 1, 0, 0, 0),
            EndsAtUtc = new DateTime(2026, 11, 1, 2, 0, 0), PlacesTaken = 12, DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventSessions.Add(new HostedEventSession
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Title = "Called off", StartsAtUtc = new DateTime(2026, 10, 31, 15, 0, 0),
            EndsAtUtc = new DateTime(2026, 10, 31, 16, 0, 0), CalledOffUtc = now, DateCreated = now, CreatedByAppUserId = HostId,
        });

        db.HostedEventBands.Add(new HostedEventBand { Id = Guid.NewGuid(), HostedEventId = EventId, Colour = "Red", Meaning = "Every night", DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventStaff.Add(new HostedEventStaff { Id = Guid.NewGuid(), HostedEventId = EventId, AppUserId = HelperId, RunsTheDoor = true, DateConfirmed = now, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventStaff.Add(new HostedEventStaff { Id = Guid.NewGuid(), HostedEventId = EventId, Email = "never@example.test", Token = "t", DateCreated = now, CreatedByAppUserId = HostId });
        db.OrganizationAds.Add(new OrganizationAd { Id = Guid.NewGuid(), OrganizationId = OrgId, Headline = "Come", Body = "Do", TargetKind = "event", HostedEventId = EventId, Status = OrganizationAdStatus.Approved, Impressions = 900, DateCreated = now, CreatedByAppUserId = HostId });

        db.HostedEventBookings.Add(new HostedEventBooking { Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId, Status = HostedEventBookingStatus.Confirmed, DateCreated = now, CreatedByAppUserId = GuestId });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task<HostedEvent> CopyAsync(SqliteTestDb sqlite, CopyHostedEventRequest request)
    {
        await using var db = await sqlite.NewContextAsync();
        var source = await db.HostedEvents.AsNoTracking().Include(e => e.Nights).SingleAsync(e => e.Id == EventId);
        var (copy, _) = await HostedEventCopy.CopyAsync(db, source, request, HostId, DateTime.UtcNow, default);
        await db.SaveChangesAsync();
        return copy;
    }

    [Fact]
    public async Task The_copy_is_a_draft_on_the_new_dates_with_the_same_gaps()
    {
        await using var sqlite = await SeedAsync();
        var copy = await CopyAsync(sqlite, new CopyHostedEventRequest("Séance Weekend 2027", new DateTime(2026, 11, 13)));

        await using var db = await sqlite.NewContextAsync();
        var made = await db.HostedEvents.Include(e => e.Nights).SingleAsync(e => e.Id == copy.Id);

        Assert.Equal(HostedEventLifecycleState.Draft, made.LifecycleState);
        Assert.NotEqual("seance-weekend", made.UrlName);
        Assert.Equal(new DateTime(2026, 11, 13), made.StartsOn);
        Assert.Equal(new DateTime(2026, 11, 14), made.EndsOn);
        Assert.Equal([new DateTime(2026, 11, 13), new DateTime(2026, 11, 14)], made.Nights.OrderBy(n => n.Date).Select(n => n.Date));
        Assert.Equal(HostedEventGoNoGo.Undecided, made.GoNoGoDecision);
        Assert.Equal(10, made.MinimumGuests);
    }

    [Fact]
    public async Task A_session_at_seven_stays_at_seven_across_the_clocks_going_back()
    {
        await using var sqlite = await SeedAsync();
        var copy = await CopyAsync(sqlite, new CopyHostedEventRequest("Next", new DateTime(2026, 11, 13)));

        await using var db = await sqlite.NewContextAsync();
        var session = await db.HostedEventSessions.SingleAsync(s => s.HostedEventId == copy.Id);

        // 7 PM on Saturday 11/14/2026 is standard time: 1 AM UTC, not midnight.
        Assert.Equal(new DateTime(2026, 11, 15, 1, 0, 0), session.StartsAtUtc);
        Assert.Equal(0, session.PlacesTaken);
        Assert.Equal("The séance", session.Title);
    }

    [Fact]
    public async Task What_the_organizer_made_comes_across_on_the_same_nights()
    {
        await using var sqlite = await SeedAsync();
        var copy = await CopyAsync(sqlite, new CopyHostedEventRequest("Next", new DateTime(2026, 11, 13)));

        await using var db = await sqlite.NewContextAsync();
        var saturday = await db.HostedEventNights.SingleAsync(n => n.HostedEventId == copy.Id && n.Date == new DateTime(2026, 11, 14));

        var unit = await db.HostedEventLayoutUnits.SingleAsync(u => u.HostedEventId == copy.Id);
        Assert.Equal("Blue Room", unit.Label);
        Assert.Equal(180, unit.Price);

        var block = await db.HostedEventUnitBlocks.SingleAsync(b => b.HostedEventLayoutUnitId == unit.Id);
        Assert.Equal(saturday.Id, block.HostedEventNightId);

        var menu = await db.HostedEventMenus.Include(m => m.Items).SingleAsync(m => m.HostedEventNightId == saturday.Id);
        Assert.Equal(2, menu.Items.Count);

        Assert.Equal(1, await db.HostedEventBands.CountAsync(b => b.HostedEventId == copy.Id));

        // The helper who said yes comes; the invitation nobody answered does not.
        var helper = Assert.Single(await db.HostedEventStaff.Where(s => s.HostedEventId == copy.Id).ToListAsync());
        Assert.Equal(HelperId, helper.AppUserId);
        Assert.True(helper.RunsTheDoor);

        var ad = await db.OrganizationAds.SingleAsync(a => a.HostedEventId == copy.Id);
        Assert.Equal(OrganizationAdStatus.Draft, ad.Status);
        Assert.Equal(0, ad.Impressions);
    }

    [Fact]
    public async Task What_happened_stays_behind_and_so_does_the_venues_yes()
    {
        await using var sqlite = await SeedAsync();
        var copy = await CopyAsync(sqlite, new CopyHostedEventRequest("Next", new DateTime(2026, 11, 13)));

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(0, await db.HostedEventBookings.CountAsync(b => b.HostedEventId == copy.Id));
        Assert.Equal(1, await db.HostedEventSessions.CountAsync(s => s.HostedEventId == copy.Id));

        var made = await db.HostedEvents.SingleAsync(e => e.Id == copy.Id);
        Assert.Equal("Mrs Thomas", made.VenueContactName);
        Assert.Null(made.VenueAgreedOnUtc);
        Assert.Null(made.VenueReference);
    }

    [Fact]
    public async Task Unticked_parts_are_left_behind()
    {
        await using var sqlite = await SeedAsync();
        var copy = await CopyAsync(sqlite, new CopyHostedEventRequest("Next", new DateTime(2026, 11, 13),
            Plan: false, Menus: false, Programme: false, Bands: false, Helpers: false, Adverts: false));

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(0, await db.HostedEventLayoutUnits.CountAsync(u => u.HostedEventId == copy.Id));
        Assert.Equal(0, await db.HostedEventMenus.CountAsync(m => m.HostedEventNight.HostedEventId == copy.Id));
        Assert.Equal(0, await db.HostedEventSessions.CountAsync(s => s.HostedEventId == copy.Id));
        Assert.Equal(0, await db.HostedEventStaff.CountAsync(s => s.HostedEventId == copy.Id));
        Assert.Equal(0, await db.OrganizationAds.CountAsync(a => a.HostedEventId == copy.Id));
        Assert.Equal(2, await db.HostedEventNights.CountAsync(n => n.HostedEventId == copy.Id));
    }

    // ── the spreadsheet ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1 615 555 0100", "'+1 615 555 0100")]
    [InlineData("@home", "'@home")]
    [InlineData("Late, after nine", "\"Late, after nine\"")]
    [InlineData("Plain", "Plain")]
    public void A_cell_is_never_read_as_a_formula(string value, string written)
        => Assert.Equal(written, BookingsCsv.Cell(value));

    [Fact]
    public void The_spreadsheet_has_a_row_per_booking_with_the_phone_and_the_nights_they_came()
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), Status = HostedEventBookingStatus.Confirmed, PartySize = 2, ContactPhone = "615-555-0100",
            LeadAppUser = new AppUser { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.test" },
            DateCreated = new DateTime(2026, 9, 1),
        };
        booking.Nights.Add(new HostedEventBookingNight
        {
            HostedEventNight = new HostedEventNight { Date = new DateTime(2026, 10, 30) },
            HostedEventLayoutUnit = new HostedEventLayoutUnit { Label = "Blue Room" },
        });
        booking.Guests.Add(new HostedEventBookingGuest { DisplayName = "Charles", DietaryNotes = "No nuts" });

        var csv = BookingsCsv.Write([booking],
            new Dictionary<Guid, IReadOnlyList<DateTime>> { [booking.Id] = [new DateTime(2026, 10, 30)] },
            includeDietary: true);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Lovelace", lines[1]);
        Assert.Contains("615-555-0100", lines[1]);
        Assert.Contains("10/30/2026 Blue Room", lines[1]);
        Assert.Contains("Charles: No nuts", lines[1]);
        Assert.EndsWith("10/30/2026", lines[1].TrimEnd('\r'));
    }
}
