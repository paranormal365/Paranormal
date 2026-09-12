using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Two hosted events with real shapes, so the plan screens open on something (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Eight sub-phases of item 235 shipped with roughly two hundred
/// green tests and not one row a person could look at. Every design mistake in that stretch was
/// caught by a question or a test and never by using the thing, which is the specific failure this
/// seeder is against: a screen has to open on data before anybody can tell whether it is any
/// good.</para>
///
/// <para><b>The two shapes are deliberately the extremes.</b> Six rooms over a weekend is what a
/// hotel does and fits on a phone; thirteen rows of twenty with a centre aisle is a theatre and is
/// the case that decides whether the grid, the labels and the compact view actually hold up. A
/// seeder with only the small one would have proved nothing.</para>
///
/// <para><b>Development only</b>, behind the same flag as the other demo seeders, and never run in
/// production. <b>Each block is independently idempotent</b>, gated on its own marker rather than
/// on one another, so a database that already has some of this gains only what it is missing —
/// the trap that once hid a past-event seed behind an unrelated early return.</para>
///
/// <para>Later phases extend this: bookings in every status, the menus, the passes, the staff and
/// the door. Phase 2 needs the plans and nothing else, and seeding rows no screen can yet show is
/// how a seeder comes to disagree with the schema.</para>
/// </remarks>
internal static class HostedEventDemoSeeder
{
    /// <summary>Stable ids, so a re-run finds its own work rather than making a second copy.</summary>
    private static readonly Guid VenueId = new("40000002-0000-0000-0000-000000000001");
    private static readonly Guid RoomsEventId = new("40000002-0000-0000-0000-000000000002");
    private static readonly Guid SeatsEventId = new("40000002-0000-0000-0000-000000000003");

    /// <summary>The rooms a small hotel offers, as a venue would describe them.</summary>
    /// <remarks>
    /// Two of them sleep four and one sleeps one, because a plan where every room is the same is a
    /// plan that never shows what "sleeps 2 more" refuses. The attic is deliberately NOT bookable:
    /// the tray must be seen leaving something out.
    /// </remarks>
    private static readonly (string Name, string Floor, int Sleeps, string Beds, bool Bookable)[] Rooms =
    [
        ("The Blue Room",     "First floor",  2, "One queen",              true),
        ("The Rose Room",     "First floor",  2, "Two twins",              true),
        ("The Colonel's Room","First floor",  1, "One single",             true),
        ("The Suite",         "Second floor", 4, "One king and a sofa bed",true),
        ("The Gable Room",    "Second floor", 3, "One queen and a twin",   true),
        ("The Back Bedroom",  "Second floor", 2, "Two twins",              true),
        ("The Attic",         "Attic",        4, "Four camp beds",         false),
    ];

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        if (!config.GetValue<bool>("SeedData:DevData:Enabled")) return;

        var ownerEmail = config["SeedData:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(ownerEmail)) return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        var owner = await userManager.FindByEmailAsync(ownerEmail);
        if (owner is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();

        // The group that hosts both. Tennessee Ghost Hunters is the one the other dev seeders
        // build out, so its members are already there to be organizers.
        var host = await db.Organizations.FirstOrDefaultAsync(
            o => o.UrlName == "paranormal365" || o.UrlName == "tgh");
        if (host is null) return;

        var now = DateTime.UtcNow;

        await SeedVenueAsync(db, host, owner.Id, now);
        await SeedRoomsEventAsync(db, host, owner.Id, now);
        await SeedSeatsEventAsync(db, host, owner.Id, now);
    }

    // ── the venue, and the rooms it has described ────────────────────────────

    private static async Task SeedVenueAsync(
        BenDataContext db, Organization host, Guid ownerId, DateTime now)
    {
        if (await db.Places.AnyAsync(p => p.Id == VenueId)) return;

        db.Places.Add(new Place
        {
            Id = VenueId,
            Name = "The Thomas House Hotel",
            StreetAddress1 = "520 E Main St",
            City = "Red Boiling Springs",
            State = "TN",
            ZipCode = "37150",
            Country = "US",
            // A venue is a venue: an event is published by definition, so a hosted event's place
            // is never a private residence.
            Kind = PlaceKind.PublicLocation,
            DateCreated = now,
            CreatedByAppUserId = ownerId,
        });

        var order = 0;
        foreach (var (name, floor, sleeps, beds, bookable) in Rooms)
        {
            db.PlaceRooms.Add(new PlaceRoom
            {
                Id = Guid.NewGuid(),
                OrganizationId = host.Id,
                PlaceId = VenueId,
                Name = name,
                Floor = floor,
                IsPublic = true,
                SortOrder = order++,
                Capacity = sleeps,
                IsBookable = bookable,
                BedNote = beds,
                IsActive = true,
                DateCreated = now,
                CreatedByAppUserId = ownerId,
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine(
            $"[HostedEventDemoSeeder] Created The Thomas House Hotel with {Rooms.Length} rooms, "
            + $"{Rooms.Count(r => r.Bookable)} of them bookable.");
    }

    // ── a weekend in the rooms ───────────────────────────────────────────────

    private static async Task SeedRoomsEventAsync(
        BenDataContext db, Organization host, Guid ownerId, DateTime now)
    {
        if (await db.HostedEvents.AnyAsync(e => e.Id == RoomsEventId)) return;

        // A weekend far enough out that it is always in the future, whenever this database was
        // built. A seeded event in the past is one the lifecycle job archives before anybody
        // opens it.
        var friday = NextFriday(now.Date.AddDays(30));

        db.HostedEvents.Add(new HostedEvent
        {
            Id = RoomsEventId,
            OrganizationId = host.Id,
            Name = "Thomas House Séance Weekend",
            UrlName = "thomas-house-seance-weekend",
            Tagline = "Two nights in the most haunted hotel in Tennessee.",
            Description =
                "Stay the weekend, hunt both nights, and eat with us in between. Rooms are "
                + "limited and go by request.",
            PlaceId = VenueId,
            TimeZoneId = "America/Chicago",
            StartsOn = friday,
            EndsOn = friday.AddDays(1),
            DatesAreSeparate = false,
            DefaultStartLocal = new TimeSpan(19, 0, 0),
            DefaultEndLocal = new TimeSpan(2, 0, 0),
            LayoutKind = HostedEventLayoutKind.Rooms,
            // Somebody who comes for Saturday and drives home. Priced, and never charged here.
            DayPassCapacity = 20,
            DayPassPrice = 45m,
            ContactLine = "Call the hotel on (615) 555-0142 to settle up.",
            IsPublished = false,
            DateCreated = now,
            CreatedByAppUserId = ownerId,
        });

        db.HostedEventNights.AddRange(
            Night(RoomsEventId, friday, "Friday — arrival and first hunt", 0, ownerId, now),
            Night(RoomsEventId, friday.AddDays(1), "Saturday — dinner and the séance", 1, ownerId, now));

        // Four of the six bookable rooms placed, two left in the tray on purpose: the tray with
        // something in it is the state the designer is actually used in, and an empty one would
        // have hidden every bug in placing.
        var bookable = await db.PlaceRooms
            .Where(r => r.PlaceId == VenueId && r.IsBookable && r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();

        var placed = 0;
        foreach (var room in bookable.Take(4))
        {
            db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit
            {
                Id = Guid.NewGuid(),
                HostedEventId = RoomsEventId,
                PlaceRoomId = room.Id,
                Section = room.Floor,
                // Null capacity on purpose: the room's own number is used unless the event says
                // otherwise, and a seeder that repeated it would hide that rule working.
                Capacity = null,
                Price = room.Capacity >= 4 ? 240m : 180m,
                LayoutRow = placed / 3,
                LayoutColumn = placed % 3,
                SortOrder = placed,
                DateCreated = now,
                CreatedByAppUserId = ownerId,
            });
            placed++;
        }

        await db.SaveChangesAsync();
        Console.WriteLine(
            $"[HostedEventDemoSeeder] Created the séance weekend: 2 nights, {placed} rooms on the "
            + "plan and 2 still in the list.");
    }

    // ── a theatre ────────────────────────────────────────────────────────────

    private static async Task SeedSeatsEventAsync(
        BenDataContext db, Organization host, Guid ownerId, DateTime now)
    {
        if (await db.HostedEvents.AnyAsync(e => e.Id == SeatsEventId)) return;

        var night = NextFriday(now.Date.AddDays(45));

        db.HostedEvents.Add(new HostedEvent
        {
            Id = SeatsEventId,
            OrganizationId = host.Id,
            Name = "An Evening of Evidence",
            UrlName = "an-evening-of-evidence",
            Tagline = "One night, two hundred and sixty seats.",
            Description = "A presentation of the year's recordings, in the ballroom.",
            PlaceId = VenueId,
            TimeZoneId = "America/Chicago",
            StartsOn = night,
            EndsOn = night,
            // A single evening: each date stands on its own even when there is only one of them.
            DatesAreSeparate = true,
            DefaultStartLocal = new TimeSpan(19, 30, 0),
            DefaultEndLocal = new TimeSpan(21, 30, 0),
            LayoutKind = HostedEventLayoutKind.Seats,
            DayPassCapacity = 0,
            ContactLine = "Tickets are settled at the door.",
            IsPublished = false,
            DateCreated = now,
            CreatedByAppUserId = ownerId,
        });

        db.HostedEventNights.Add(Night(SeatsEventId, night, null, 0, ownerId, now));

        // Thirteen rows of twenty with a gangway down the middle: rows A–N (I is skipped), seats
        // 1–10 then 11–20 with column 10 left empty. The aisle is a GAP IN THE GRID and not a
        // seat, which is the arrangement the whole row-and-column model exists for.
        const int rows = 13, half = 10;
        var order = 0;

        for (var r = 0; r < rows; r++)
        {
            var rowName = RowName(r);
            var section = r < 9 ? "Stalls" : "Balcony";

            for (var s = 0; s < half * 2; s++)
            {
                db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit
                {
                    Id = Guid.NewGuid(),
                    HostedEventId = SeatsEventId,
                    Label = $"{rowName}{s + 1}",
                    Section = section,
                    // A seat holds one person, always.
                    Capacity = 1,
                    Price = section == "Stalls" ? 24m : 18m,
                    LayoutRow = r,
                    LayoutColumn = s < half ? s : s + 1,   // the gangway is column 10
                    SortOrder = order++,
                    DateCreated = now,
                    CreatedByAppUserId = ownerId,
                });
            }
        }

        await db.SaveChangesAsync();
        Console.WriteLine(
            $"[HostedEventDemoSeeder] Created An Evening of Evidence: {order} seats in two "
            + "sections with a centre aisle.");
    }

    // ── small helpers ────────────────────────────────────────────────────────

    private static HostedEventNight Night(
        Guid eventId, DateTime date, string? title, int order, Guid ownerId, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            HostedEventId = eventId,
            Date = date,
            Title = title,
            SortOrder = order,
            DateCreated = now,
            CreatedByAppUserId = ownerId,
        };

    /// <summary>The first Friday on or after a date, so a seeded weekend is a weekend.</summary>
    private static DateTime NextFriday(DateTime from)
        => from.AddDays(((int)DayOfWeek.Friday - (int)from.DayOfWeek + 7) % 7);

    /// <summary>
    /// A row's letter, skipping I and O.
    /// </summary>
    /// <remarks>
    /// The same alphabet as <c>PlanLabels.RowAlphabet</c> on the website, and deliberately copied
    /// rather than shared: the API does not reference the Blazor component library, and one short
    /// constant duplicated is a far smaller cost than a project reference that exists so a seeder
    /// can name a row. On a printed ticket and a brass rail, I is a 1 and O is a 0.
    /// </remarks>
    private static string RowName(int index) => "ABCDEFGHJKLMNPQRSTUVWXYZ"[index].ToString();
}
