using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Venues;
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
    private static readonly Guid KitchenBookingId = new("40000002-0000-0000-0000-000000000004");

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
        await SeedTheVenueOnTheSiteAsync(db, host, owner.Id, now);
        await SeedRoomsEventAsync(db, host, owner.Id, now);
        await SeedSeatsEventAsync(db, host, owner.Id, now);
        await PutThemOnThePublicSiteAsync(scope.ServiceProvider, db, owner.Id);
        await SeedWhatTheVenueKeepsAsync(db, owner.Id, now);
        await SeedWhatIsServedAsync(db, owner.Id, now);
        await SeedAPartyTheKitchenMustWorkAroundAsync(db, userManager, config, owner.Id, now);
        await SeedTheProgrammeAsync(db, owner.Id, now);
        await SeedHowToGetInAsync(db);
    }

    /// <summary>
    /// What a guest needs to know about getting in and around (phase 17a) — added to events seeded before the field
    /// existed, and never over what somebody has since written.
    /// </summary>
    private static async Task SeedHowToGetInAsync(BenDataContext db)
    {
        var notes = new Dictionary<Guid, string>
        {
            [RoomsEventId] =
                "The hotel has no lift: the bedrooms are up one flight of stairs, and the ballroom and bar are on the "
                + "ground floor.\nSome of the hunt is in low light. Park on the street or in the lot behind the hotel.",
            [SeatsEventId] =
                "The ballroom is step-free from the side door on Main Street. Seats in row A have the most leg room; "
                + "tell us when you book if you need one.",
        };

        var events = await db.HostedEvents.Where(e => notes.Keys.Contains(e.Id) && e.AccessNotes == null).ToListAsync();
        if (events.Count == 0) return;
        foreach (var ev in events) ev.AccessNotes = notes[ev.Id];
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A room held back for one night and a pair of seats blocked for the whole run.
    /// </summary>
    /// <remarks>
    /// <para>Both cases, because they are the two a venue actually has and they read differently to
    /// a guest: the Suite is the owner's family on the Friday and free on the Saturday, and the two
    /// seats behind the pillar are never sold at all.</para>
    ///
    /// <para>The note on the house-held one is deliberately the kind of thing a host really writes,
    /// so that anything leaking it onto a public plan is obvious the moment somebody looks.</para>
    /// </remarks>
    private static async Task SeedWhatTheVenueKeepsAsync(
        BenDataContext db, Guid ownerId, DateTime now)
    {
        if (await db.HostedEventUnitBlocks.AnyAsync(
                b => b.HostedEventLayoutUnit.HostedEventId == RoomsEventId
                  || b.HostedEventLayoutUnit.HostedEventId == SeatsEventId))
            return;

        var friday = await db.HostedEventNights
            .Where(n => n.HostedEventId == RoomsEventId)
            .OrderBy(n => n.Date)
            .FirstOrDefaultAsync();

        var suite = await db.HostedEventLayoutUnits
            .FirstOrDefaultAsync(u => u.HostedEventId == RoomsEventId
                                   && u.PlaceRoom!.Name == "The Suite");

        if (friday is not null && suite is not null)
        {
            db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock
            {
                Id = Guid.NewGuid(),
                HostedEventLayoutUnitId = suite.Id,
                HostedEventNightId = friday.Id,
                Kind = HostedEventBlockKind.HouseHeld,
                Note = "Mrs Cole's family are in it on the Friday.",
                DateCreated = now,
                CreatedByAppUserId = ownerId,
            });
        }

        // Two seats behind the pillar, every night of the run.
        var behindThePillar = await db.HostedEventLayoutUnits
            .Where(u => u.HostedEventId == SeatsEventId
                     && (u.Label == "G1" || u.Label == "G2"))
            .ToListAsync();

        foreach (var seat in behindThePillar)
        {
            db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock
            {
                Id = Guid.NewGuid(),
                HostedEventLayoutUnitId = seat.Id,
                HostedEventNightId = null,
                Kind = HostedEventBlockKind.Blocked,
                Note = "Behind the pillar — nobody can see the stage.",
                DateCreated = now,
                CreatedByAppUserId = ownerId,
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine(
            "[HostedEventDemoSeeder] Held back the Suite on the Friday and blocked two seats "
            + "behind the pillar.");
    }

    /// <summary>
    /// Four sittings across the weekend, in the order they are served (item 235 phase 5).
    /// </summary>
    /// <remarks>
    /// <para><b>Friday runs dinner, then a late supper, then Saturday's breakfast</b> — three
    /// sittings on one night, and the last of them at eight in the morning. That is the case the
    /// ordering rule exists for: a screen that sorted a night by the clock would print breakfast
    /// first and read the weekend backwards, and a seed without it would never show that.</para>
    ///
    /// <para><b>The dishes carry courses and tags the way a kitchen writes them</b>, because the
    /// tag on a dish and a guest's own allergy are two different things with two different
    /// audiences, and a demo where only one of them exists cannot show the difference.</para>
    /// </remarks>
    private static async Task SeedWhatIsServedAsync(BenDataContext db, Guid ownerId, DateTime now)
    {
        if (await db.HostedEventMenus.AnyAsync(m => m.HostedEventNight.HostedEventId == RoomsEventId))
            return;

        var nights = await db.HostedEventNights
            .Where(n => n.HostedEventId == RoomsEventId)
            .OrderBy(n => n.Date)
            .ToListAsync();
        if (nights.Count < 2) return;

        (string Title, TimeSpan At, string? Note, (string? Course, string Name, string? Tags)[] Dishes)[]
            friday =
        [
            ("Dinner", new TimeSpan(19, 0, 0), "In the dining room, before we start.",
             [
                 ("Starter", "Tomato soup", "vegan"),
                 ("Main", "Roast chicken, greens and potatoes", null),
                 ("Main", "Mushroom pie", "vegetarian"),
                 ("Pudding", "Apple crumble", "contains nuts"),
             ]),
            ("Late supper", new TimeSpan(23, 30, 0), "Between the two hunts.",
             [
                 (null, "Sandwiches", null),
                 (null, "Tea and coffee", null),
             ]),
            ("Breakfast", new TimeSpan(8, 0, 0), "The morning after — yes, this is still Friday's night.",
             [
                 (null, "Eggs, bacon and toast", null),
                 (null, "Fruit and yoghurt", "vegetarian"),
             ]),
        ];

        (string Title, TimeSpan At, string? Note, (string? Course, string Name, string? Tags)[] Dishes)[]
            saturday =
        [
            ("Dinner", new TimeSpan(18, 30, 0), "Early, because the séance is at nine.",
             [
                 ("Starter", "Cornbread and honey butter", "vegetarian"),
                 ("Main", "Beef stew", null),
                 ("Main", "Butternut squash risotto", "vegan"),
                 ("Pudding", "Pecan pie", "contains nuts"),
             ]),
        ];

        var order = 0;
        foreach (var (night, sittings) in new[] { (nights[0], friday), (nights[1], saturday) })
        {
            foreach (var (title, at, note, dishes) in sittings)
            {
                var menu = new HostedEventMenu
                {
                    Id = Guid.NewGuid(),
                    HostedEventNightId = night.Id,
                    Title = title,
                    ServedAtLocal = at,
                    Notes = note,
                    SortOrder = order++,
                    DateCreated = now,
                    CreatedByAppUserId = ownerId,
                };
                db.HostedEventMenus.Add(menu);

                var dish = 0;
                foreach (var (course, name, tags) in dishes)
                {
                    db.HostedEventMenuItems.Add(new HostedEventMenuItem
                    {
                        Id = Guid.NewGuid(),
                        HostedEventMenuId = menu.Id,
                        Course = course,
                        Name = name,
                        DietaryTags = tags,
                        SortOrder = dish++,
                        DateCreated = now,
                    });
                }
            }
        }

        await db.SaveChangesAsync();
        Console.WriteLine(
            $"[HostedEventDemoSeeder] Wrote {order} sittings across the séance weekend.");
    }

    /// <summary>
    /// One confirmed party with allergies, so the kitchen's sheet opens on something (phase 5).
    /// </summary>
    /// <remarks>
    /// <para><b>Four people, three notes, and two of them the same words.</b> That is what makes
    /// the sheet worth looking at: two vegans are one line saying two, "no nuts" and "nut allergy"
    /// stay two lines on purpose, and the fourth person was never named — so the sheet says one
    /// person is unaccounted for, which is the number a kitchen gets caught by.</para>
    ///
    /// <para><b>Confirmed through <c>BookingTransitions</c></b> and not by setting the column,
    /// because that is the rule the whole of phase 4 is built on and a seeder that quietly broke
    /// it would be the first place the next inconsistency came from. The umbrella row is left to
    /// the calendar sync, which needs a request behind it; a demo booking without one is a
    /// booking, not a calendar entry.</para>
    /// </remarks>
    private static async Task SeedAPartyTheKitchenMustWorkAroundAsync(
        BenDataContext db, UserManager<AppUser> userManager, IConfiguration config,
        Guid ownerId, DateTime now)
    {
        if (await db.HostedEventBookings.AnyAsync(b => b.Id == KitchenBookingId)) return;

        // Daniel is the seeded guest with no group of his own — the person this feature is for.
        var leadEmail = config["SeedData:SeedOrganization:GuestEmail"] ?? "daniel.park@benco.dev";
        var lead = await userManager.FindByEmailAsync(leadEmail);
        if (lead is null) return;

        var nights = await db.HostedEventNights
            .Where(n => n.HostedEventId == RoomsEventId)
            .OrderBy(n => n.Date)
            .ToListAsync();
        var room = await db.HostedEventLayoutUnits
            .Where(u => u.HostedEventId == RoomsEventId)
            .OrderBy(u => u.SortOrder)
            .FirstOrDefaultAsync();
        if (nights.Count == 0 || room is null) return;

        var booking = new HostedEventBooking
        {
            Id = KitchenBookingId,
            HostedEventId = RoomsEventId,
            LeadAppUserId = lead.Id,
            Kind = HostedEventBookingKind.Overnight,
            PartySize = 4,
            Note = "Two of us are vegan and one cannot go near nuts.",
            DateCreated = now,
            CreatedByAppUserId = ownerId,
        };

        foreach (var night in nights)
        {
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                HostedEventNightId = night.Id,
                HostedEventLayoutUnitId = room.Id,
                DateCreated = now,
            });
        }

        var guests = new (string Name, string? Notes)[]
        {
            ("Ada Fielding", "no nuts"),
            ("Bertie Fielding", "vegan"),
            ("Clara Fielding", "vegan"),
        };

        var order = 0;
        foreach (var (name, notes) in guests)
        {
            booking.Guests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                DisplayName = name,
                DietaryNotes = notes,
                SortOrder = order++,
                DateCreated = now,
            });
        }

        db.HostedEventBookings.Add(booking);
        Services.Events.BookingTransitions.Confirm(
            booking, ownerId, "See you Friday — the Blue Room is yours.", now);

        await db.SaveChangesAsync();
        Console.WriteLine(
            "[HostedEventDemoSeeder] Confirmed a party of 4 with three dietary notes and one "
            + "person nobody named.");
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

    /// <summary>
    /// A published programme for the rooms weekend (item 235 phase 10): a dinner anybody comes to, a
    /// class that holds fifteen, and a séance that holds eight.
    /// </summary>
    /// <remarks>
    /// Three kinds on purpose, so the programme shows "just come", a count with room, and a small
    /// session that fills — the three things a guest reads it for.
    /// </remarks>
    private static async Task SeedTheProgrammeAsync(BenDataContext db, Guid ownerId, DateTime now)
    {
        if (await db.HostedEventSessions.AnyAsync(s => s.HostedEventId == RoomsEventId)) return;

        var hosted = await db.HostedEvents.Include(e => e.Nights).FirstOrDefaultAsync(e => e.Id == RoomsEventId);
        if (hosted is null || hosted.Nights.Count == 0) return;

        var nights = hosted.Nights.OrderBy(n => n.Date).ToList();
        var first = nights[0].Date;
        var last = nights[^1].Date;

        (string Title, DateTime Date, int From, int To, string Where, string? Leader, int? Capacity, bool SignUp)[] sessions =
        [
            ("Dinner in the dining room", first, 18, 20, "The dining room", null, null, false),
            ("Operating the Ovilus", first, 21, 22, "The parlour", "Ben", 15, true),
            ("Séance in the parlour", last, 22, 23, "The parlour", "Mrs Cole", 8, true),
        ];

        var order = 0;
        foreach (var x in sessions)
        {
            var (startsUtc, endsUtc) = Services.Events.SessionSignUps.ToUtc(
                hosted, x.Date, TimeSpan.FromHours(x.From), TimeSpan.FromHours(x.To));
            db.HostedEventSessions.Add(new HostedEventSession
            {
                Id = Guid.NewGuid(), HostedEventId = RoomsEventId, Title = x.Title,
                StartsAtUtc = startsUtc, EndsAtUtc = endsUtc, LocationText = x.Where, LedBy = x.Leader,
                Capacity = x.Capacity, RequiresSignUp = x.SignUp, SortOrder = order++,
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
        }

        hosted.ProgrammePublishedUtc ??= now;
        await db.SaveChangesAsync();
        Console.WriteLine("[HostedEventDemoSeeder] Published a programme of three sessions for the rooms weekend.");
    }

    /// <summary>
    /// The hotel as a confirmed venue on the site, with its story and its rules (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// Confirmed and published, so that another group's event at the same address meets the gate
    /// the moment it is created, and the venue page and the "run as a venue by" line have something
    /// real to show. The host's own two events are untouched: it is their own venue.
    /// </remarks>
    private static async Task SeedTheVenueOnTheSiteAsync(
        BenDataContext db, Organization host, Guid ownerId, DateTime now)
    {
        if (await db.OrganizationVenueProfiles.AnyAsync(v => v.PlaceId == VenueId)) return;

        db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
        {
            Id = Guid.NewGuid(),
            OrganizationId = host.Id,
            PlaceId = VenueId,
            History = "Built in 1890 as the Cloyd Hotel for visitors taking the mineral waters, the "
                    + "house has been a hotel ever since. Guests have reported footsteps on the second "
                    + "floor landing and a little girl on the stairs since at least the 1970s.",
            HouseRules = "No open flames upstairs.\nQuiet on the second floor after midnight.\n"
                       + "Park behind the building, not on Main Street.",
            MaxOvernightGuests = 22,
            IsPublished = true,
            VerifiedUtc = now,
            DateCreated = now,
            CreatedByAppUserId = ownerId,
        });

        await db.SaveChangesAsync();
        Console.WriteLine("[HostedEventDemoSeeder] Confirmed the host as the venue at The Thomas House Hotel.");
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
            // Created as a draft, because that is what creating an event does. It is put live a
            // few lines later by PutThemOnThePublicSiteAsync, which asks the entitlement and
            // writes the umbrella row rather than setting this column and hoping.
            LifecycleState = HostedEventLifecycleState.Draft,
            VenueArrangement = HostedEventVenueArrangement.Self,
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
            // A draft here too, and put live the same way. See the weekend above.
            LifecycleState = HostedEventLifecycleState.Draft,
            VenueArrangement = HostedEventVenueArrangement.Self,
            // A theatre's seats are picked by the guest; the hotel's rooms are asked for and
            // placed. One seeded event of each, so both booking modes have a screen to show.
            BookingMode = HostedEventBookingMode.Pick,
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

    // ── and then live, the way a person would do it ──────────────────────────

    /// <summary>Why the seeder's own credit exists, and the marker that stops it buying a second.</summary>
    private const string GrantedForTheDemo =
        "Seeded so the demo events can be published on a fresh database.";

    /// <summary>
    /// Puts the two seeded events on the public site, by the same route the publish button takes.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the seeder publishes at all.</b> Both events were created as drafts, on the
    /// reasoning that the plan screens are what the seed is for. But a draft has no public page by
    /// design — <see cref="HostedEventStates.OnThePublicSite"/> is Published, Live and Ended — so
    /// every visitor-facing surface built on top of them had nothing to open. On the long-lived
    /// test databases that was hidden, because earlier sessions had pressed publish by hand; a run
    /// on a brand-new database failed eight fixtures on the precondition alone. A seeded state that
    /// only works on databases somebody has already edited is the exact drift an isolated-database
    /// harness exists to catch.</para>
    ///
    /// <para><b>Not a column poke.</b> Setting <c>LifecycleState</c> and walking away would leave
    /// a shape no real publish produces: no <c>FirstPublishedUtc</c>, so the event would be charged
    /// for the first time the day somebody unpublished and republished it; no entitlement asked,
    /// so a deployment that does gate hosting would have seeded its way around its own rule; and no
    /// umbrella calendar row, which is what carries the <c>/o/{org}/events/{slug}</c> address, the
    /// share tags, the reminder and the sign-up. So this asks the same questions the endpoint asks,
    /// in the same order, and writes the same three things.</para>
    ///
    /// <para><b>The money is seeded rather than skipped.</b> If the deployment's price list puts
    /// this group on the credit path, a credit is granted first — the SuperAdmin grant shape, priced
    /// at zero with a reason on it, because no money changed hands and a $99 sale that never
    /// happened has no business in the ledger — and then spent by the ordinary path. Same reasoning
    /// as <c>BillingDemoSeeder</c> opening its subscriptions through <c>PeriodOpener</c>: a seeded
    /// row that did not come from the real code is a row that will quietly stop matching it.</para>
    ///
    /// <para><b>Idempotent and additive, on its own marker.</b> Only an event still in Draft that
    /// has never been live is touched. A second run finds nothing. An event somebody unpublished by
    /// hand while testing keeps its <c>FirstPublishedUtc</c> and is left alone, so the seeder never
    /// undoes a state a person chose.</para>
    /// </remarks>
    private static async Task PutThemOnThePublicSiteAsync(
        IServiceProvider scoped, BenDataContext db, Guid ownerId)
    {
        var drafts = await db.HostedEvents
            .Include(e => e.Nights)
            .Include(e => e.LayoutUnits)
            .Where(e => (e.Id == RoomsEventId || e.Id == SeatsEventId)
                     && e.LifecycleState == HostedEventLifecycleState.Draft
                     && e.FirstPublishedUtc == null)
            .OrderBy(e => e.StartsOn)
            .ToListAsync();

        if (drafts.Count == 0) return;

        var entitlement = scoped.GetRequiredService<HostedEventEntitlement>();
        var sync = scoped.GetRequiredService<HostedEventCalendarSync>();

        var published = 0;
        foreach (var hosted in drafts)
        {
            // The publish button's own checklist, read the same way the endpoint reads it: the
            // first item that is not done is the refusal. A seeder that published past it would be
            // seeding a state the site would not let anybody reach.
            var venue = await VenueGrants.ForEventAsync(db, hosted, default);
            if (HostedEventReadiness.Describe(hosted, venue).FirstOrDefault(i => !i.Done) is { } blocker)
            {
                Console.WriteLine(
                    $"[HostedEventDemoSeeder] Left \"{hosted.Name}\" a draft — {blocker.Sentence}");
                continue;
            }

            if (!await ThereIsSomethingToPublishAgainstAsync(db, entitlement, hosted, ownerId))
                continue;

            var (spent, refusal) = await entitlement.TakeForAsync(db, hosted, ownerId, default);
            if (refusal is not null)
            {
                Console.WriteLine(
                    $"[HostedEventDemoSeeder] Left \"{hosted.Name}\" a draft — {refusal}");
                continue;
            }

            var now = DateTime.UtcNow;
            hosted.FirstPublishedUtc = now;
            hosted.LifecycleState = HostedEventLifecycleState.Published;
            hosted.DateUpdated = now;
            hosted.UpdatedByAppUserId = ownerId;

            // The umbrella row, which is what every public surface actually resolves: the address,
            // the share card, the reminder, the sign-up. It reads IsOnThePublicSite off the event,
            // so it has to be written after the state above and not before.
            await sync.SyncAsync(db, hosted, ownerId);

            // One save per event, so a credit can never be spent without its event going live.
            await db.SaveChangesAsync();
            published++;

            Console.WriteLine(
                $"[HostedEventDemoSeeder] Put \"{hosted.Name}\" on the public site"
                + (spent is null ? " on the group's plan." : " for one event credit."));
        }

        if (published == 0)
            Console.WriteLine("[HostedEventDemoSeeder] Nothing was published; the demo events are still drafts.");
    }

    /// <summary>
    /// Makes sure publishing has something to spend, granting one credit when it needs one.
    /// </summary>
    /// <remarks>
    /// <para>Asked before the take rather than instead of it: the take is what actually spends, in
    /// the same save as the publish, and this only fills the pocket it reaches into.</para>
    ///
    /// <para><b>Only on the credit path, and only when the pocket is empty.</b> A group whose plan
    /// includes hosting spends nothing and is handed nothing — a credit sitting beside a plan that
    /// never uses it would be furniture that contradicts the group it belongs to. And a group that
    /// already holds credits gets none: the seeder's job is to unblock its own two events, not to
    /// top anybody up on every restart.</para>
    /// </remarks>
    private static async Task<bool> ThereIsSomethingToPublishAgainstAsync(
        BenDataContext db, HostedEventEntitlement entitlement, HostedEvent hosted, Guid ownerId)
    {
        var verdict = await entitlement.DescribeAsync(db, hosted.OrganizationId, hosted.Id);

        if (verdict.Kind is not HostedEventEntitlement.EntitlementKind.Credit)
        {
            // On a plan. Anything standing in the way is a cap somebody configured, and inventing
            // room under it is not the seeder's to do — it says so and leaves the draft alone.
            if (verdict.Refusal is not null)
            {
                Console.WriteLine(
                    $"[HostedEventDemoSeeder] Left \"{hosted.Name}\" a draft — {verdict.Refusal}");
                return false;
            }
            return true;
        }

        if (verdict.CreditsAvailable > 0) return true;

        var now = DateTime.UtcNow;
        db.EventCredits.Add(new EventCredit
        {
            Id                  = Guid.NewGuid(),
            OwnerOrganizationId = hosted.OrganizationId,
            // Zero and a reason, never a price: the grant path's shape. A $99 row here would put a
            // sale nobody made into the money trail.
            PriceAtPurchase     = 0m,
            Currency            = "USD",
            PurchasedUtc        = now,
            ExpiresUtc          = now.Add(EventCredits.Life),
            GrantedReason       = GrantedForTheDemo,
            DateCreated         = now,
            CreatedByAppUserId  = ownerId,
        });
        await db.SaveChangesAsync();

        Console.WriteLine(
            $"[HostedEventDemoSeeder] Granted one event credit so \"{hosted.Name}\" can be published.");
        return true;
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
