using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Creating a hosted event, and the umbrella calendar row that comes with it (item 235).
/// </summary>
/// <remarks>
/// <para>The umbrella is what lets a three-night weekend appear on the public list, send its
/// reminder, produce a calendar file and reach the phone already in people's pockets without any of
/// them being taught what a hosted event is. If it is not written, or written wrong, every one of
/// those is wrong at once.</para>
///
/// <para>And it is exactly ONE row. Three nights are one thing that happens; three rows would offer
/// a visitor three sign-ups to the same weekend.</para>
/// </remarks>
public sealed class HostedEventControllerTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid VenueId = Guid.NewGuid();
    private static readonly Guid HouseId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var f = CreateFactory();
        await using var db = await f.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Thomas House", UrlName = "thomas-house",
            Kind = OrganizationKind.HauntedProperty,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.Places.Add(new Place
        {
            Id = VenueId, Name = "The Thomas House Hotel", City = "Red Boiling Springs",
            State = "TN", Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.Places.Add(new Place
        {
            Id = HouseId, Name = "A family home", City = "Nashville", State = "TN",
            Kind = PlaceKind.PrivateResidence,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = OwnerId, IsActive = true,
            Role = OrganizationMemberRole.Owner,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        await db.SaveChangesAsync();
        return f;
    }

    /// <summary>
    /// Puts a free band on the site that does NOT include hosting events.
    /// </summary>
    /// <remarks>
    /// <para>Without any tiers at all, every capability resolves as included — the fail-open rule
    /// every limit here follows, so that a half-configured price list never locks anybody out. That
    /// is right, and it means the credit path is only reached once somebody has actually said which
    /// plans may host.</para>
    ///
    /// <para>So the tests that are about credits configure the site the way a real one is: a free
    /// band with <c>HostEvents</c> excluded, which is exactly the switch a SuperAdmin unticks.</para>
    /// </remarks>
    private static async Task ExcludeHostingAsync(IDbContextFactory<BenDataContext> f)
    {
        await using var db = await f.CreateDbContextAsync();

        var free = new SubscriptionTier
        {
            Id = Guid.NewGuid(), Name = "Free", MinMembers = 1, MaxMembers = null,
            SortOrder = 1, IsActive = true, IsBandedByMembers = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        };
        db.SubscriptionTiers.Add(free);
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = free.Id,
            Interval = BillingInterval.Monthly, Price = 0m, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.SubscriptionTierExcludedCapabilities.Add(new SubscriptionTierExcludedCapability
        {
            Id = Guid.NewGuid(), SubscriptionTierId = free.Id,
            Capability = TierCapability.HostEvents,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        await db.SaveChangesAsync();
    }

    private static HostedEventController Build(IDbContextFactory<BenDataContext> f)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sanitizer = new Mock<ICmsMarkupSanitizer>();
        sanitizer.Setup(s => s.SanitizeHtml(It.IsAny<string>())).Returns<string>(h => h);

        var mapper = new Mock<IMapper>();
        var limits = new SubscriptionLimitGuard(f);

        return new HostedEventController(
            f, mapper.Object, security.Object, sanitizer.Object,
            new HostedEventCalendarSync(), new HostedEventEntitlement(limits))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer")),
                },
            },
        };
    }

    private static UpsertHostedEventRequest Weekend(
        string name = "Thomas House Weekend", Guid? place = null,
        DateTime? from = null, DateTime? to = null)
        => new(name, place ?? VenueId,
               from ?? new DateTime(2026, 10, 30), to ?? new DateTime(2026, 11, 1),
               TimeZoneId: "America/Chicago",
               DefaultStartLocal: new TimeSpan(19, 0, 0),
               DefaultEndLocal: new TimeSpan(23, 0, 0));

    private static HostedEventRecord Created(ActionResult<HostedEventRecord> result)
    {
        // Says WHAT was refused rather than "not a CreatedAtActionResult", because the refusals
        // here are sentences and reading them is the whole point of the exercise.
        if (result.Result is BadRequestObjectResult bad)
            Assert.Fail($"Creating the event was refused: {bad.Value}");

        return Assert.IsType<HostedEventRecord>(
            Assert.IsType<CreatedAtActionResult>(result.Result).Value);
    }

    // ── the umbrella ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_weekend_gets_one_umbrella_row_spanning_the_whole_thing()
    {
        var f = await SeedAsync();
        var record = Created(await Build(f).Create(OrgId, Weekend(), default));

        Assert.Equal(3, record.Nights.Count);
        Assert.NotNull(record.UmbrellaEventId);

        await using var db = await f.CreateDbContextAsync();
        var rows = await db.OrgCalendarEvents.Where(e => e.HostedEventId == record.Id).ToListAsync();

        // One row, not three. Three nights are one thing that happens.
        Assert.Single(rows);

        var umbrella = rows[0];
        Assert.Equal("Thomas House Weekend", umbrella.Title);
        Assert.Equal(record.UrlName, umbrella.UrlName);
        Assert.Equal(VenueId, umbrella.PlaceId);
        Assert.Equal("America/Chicago", umbrella.TimeZoneId);

        // 7pm Central on the 30th to 11pm Central on the 1st — the whole weekend.
        var central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        Assert.Equal(new DateTime(2026, 10, 30, 19, 0, 0),
            TimeZoneInfo.ConvertTimeFromUtc(umbrella.StartDateTime, central));
        Assert.Equal(new DateTime(2026, 11, 1, 23, 0, 0),
            TimeZoneInfo.ConvertTimeFromUtc(umbrella.EndDateTime, central));
    }

    [Fact]
    public async Task A_new_event_is_a_draft_and_its_umbrella_is_not_public()
    {
        var f = await SeedAsync();
        var record = Created(await Build(f).Create(OrgId, Weekend(), default));

        Assert.False(record.IsPublished);
        Assert.Null(record.FirstPublishedUtc);

        await using var db = await f.CreateDbContextAsync();
        var umbrella = await db.OrgCalendarEvents.FirstAsync(e => e.HostedEventId == record.Id);

        // Creating costs nothing, so it must show nobody anything until somebody decides to
        // publish. A draft that leaked onto the public list would be the whole argument for
        // charging at publish undone.
        Assert.False(umbrella.IsPublic);
    }

    [Fact]
    public async Task Publishing_makes_the_umbrella_public_and_stamps_when()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var published = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.Publish(OrgId, record.Id, default)).Result).Value);

        Assert.True(published.IsPublished);
        Assert.NotNull(published.FirstPublishedUtc);

        await using var db = await f.CreateDbContextAsync();
        Assert.True((await db.OrgCalendarEvents.FirstAsync(e => e.HostedEventId == record.Id)).IsPublic);
    }

    [Fact]
    public async Task Taking_it_down_and_putting_it_back_up_never_charges_twice()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        await controller.Publish(OrgId, record.Id, default);

        await using (var db = await f.CreateDbContextAsync())
        {
            var first = (await db.HostedEvents.FirstAsync(e => e.Id == record.Id)).FirstPublishedUtc;
            Assert.NotNull(first);

            await controller.Unpublish(OrgId, record.Id, default);
            await controller.Publish(OrgId, record.Id, default);

            await using var after = await f.CreateDbContextAsync();
            // The same moment, untouched: one event, one charge, for the life of the event.
            Assert.Equal(first, (await after.HostedEvents.FirstAsync(e => e.Id == record.Id)).FirstPublishedUtc);
        }
    }

    [Fact]
    public async Task Cancelling_says_so_in_the_title_everybody_actually_reads()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));
        await controller.Publish(OrgId, record.Id, default);

        await controller.Cancel(OrgId, record.Id, new CancelHostedEventRequest("Burst pipe"), default);

        await using var db = await f.CreateDbContextAsync();
        var umbrella = await db.OrgCalendarEvents.FirstAsync(e => e.HostedEventId == record.Id);

        // The title is the one line every list, share card and phone notification shows. A
        // cancelled event that reads like a live one is the worst thing on this screen.
        Assert.StartsWith("CANCELLED", umbrella.Title);
    }

    // ── a run of separate dates ──────────────────────────────────────────────

    [Fact]
    public async Task A_run_keeps_only_the_dates_it_names_and_calls_them_dates()
    {
        var f = await SeedAsync();

        // The resident play company: one production, a date a month. Generating every day from
        // September to November would be a wrong answer delivered quickly.
        var request = new UpsertHostedEventRequest(
            "Murder at the Manor", VenueId,
            new DateTime(2026, 9, 12), new DateTime(2026, 11, 14),
            TimeZoneId: "America/Chicago",
            DatesAreSeparate: true,
            Dates: [new DateTime(2026, 9, 12), new DateTime(2026, 10, 10), new DateTime(2026, 11, 14)]);

        var record = Created(await Build(f).Create(OrgId, request, default));

        Assert.Equal(3, record.Nights.Count);
        Assert.Equal("date", record.DateNoun);
        Assert.Equal(
            [new DateTime(2026, 9, 12), new DateTime(2026, 10, 10), new DateTime(2026, 11, 14)],
            record.Nights.Select(n => n.Date).ToArray());
    }

    [Fact]
    public async Task A_stay_gets_every_day_between_its_ends_and_calls_them_nights()
    {
        var f = await SeedAsync();
        var record = Created(await Build(f).Create(OrgId, Weekend(), default));

        Assert.Equal("night", record.DateNoun);
        Assert.Equal(
            [new DateTime(2026, 10, 30), new DateTime(2026, 10, 31), new DateTime(2026, 11, 1)],
            record.Nights.Select(n => n.Date).ToArray());
    }

    [Fact]
    public async Task A_run_with_no_dates_is_refused_in_words()
    {
        var f = await SeedAsync();
        var request = Weekend() with { DatesAreSeparate = true, Dates = [] };

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await Build(f).Create(OrgId, request, default)).Result);

        Assert.Contains("run needs its dates", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task Moving_a_weekend_keeps_what_was_written_about_the_days_that_survive()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var saturday = record.Nights.First(n => n.Date == new DateTime(2026, 10, 31));
        await controller.UpdateNight(OrgId, record.Id, saturday.Id,
            new UpsertHostedEventNightRequest(Title: "The big one", Notes: "Séance at midnight"), default);

        // Extend the weekend by a day. The Saturday is still the Saturday.
        var moved = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>(
                (await controller.Update(OrgId, record.Id,
                    Weekend(to: new DateTime(2026, 11, 2)), default)).Result).Value);

        Assert.Equal(4, moved.Nights.Count);
        var kept = moved.Nights.First(n => n.Date == new DateTime(2026, 10, 31));
        Assert.Equal("The big one", kept.Title);
        Assert.Equal("Séance at midnight", kept.Notes);
    }

    // ── refusals ─────────────────────────────────────────────────────────────

    // ── credits ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_a_plan_or_a_credit_publishing_is_refused_and_names_the_price()
    {
        // A refusal that does not name the price is a refusal somebody has to go and research.
        // And nothing they built is lost — the event stays a draft with everything on it.
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Publish(OrgId, record.Id, default)).Result);

        var sentence = Assert.IsType<string>(refusal.Value);
        Assert.Contains("$99", sentence);

        await using var db = await f.CreateDbContextAsync();
        var still = await db.HostedEvents.FirstAsync(e => e.Id == record.Id);
        Assert.False(still.IsPublished);
        Assert.Null(still.FirstPublishedUtc);
        Assert.Equal(3, await db.HostedEventNights.CountAsync(n => n.HostedEventId == record.Id));
    }

    [Fact]
    public async Task Publishing_spends_the_oldest_credit_and_records_the_event_it_went_on()
    {
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        Guid oldestId;
        await using (var db = await f.CreateDbContextAsync())
        {
            var oldest = NewCredit(DateTime.UtcNow.AddDays(-300), DateTime.UtcNow.AddDays(65));
            var newest = NewCredit(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(364));
            oldestId = oldest.Id;
            db.EventCredits.AddRange(newest, oldest);
            await db.SaveChangesAsync();
        }

        var published = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.Publish(OrgId, record.Id, default)).Result).Value);

        Assert.True(published.IsPublished);
        Assert.Contains("credit spent", published.PlanNote ?? "");

        await using var after = await f.CreateDbContextAsync();
        var spent = await after.EventCredits.FirstAsync(c => c.Id == oldestId);
        Assert.NotNull(spent.SpentUtc);
        Assert.Equal(record.Id, spent.SpentOnHostedEventId);

        // And exactly one was taken.
        Assert.Equal(1, await after.EventCredits.CountAsync(c => c.SpentUtc != null));
    }

    [Fact]
    public async Task Putting_it_back_up_never_spends_a_second_credit()
    {
        // Ben's rule: one event, one credit, for the life of that event.
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        await using (var db = await f.CreateDbContextAsync())
        {
            db.EventCredits.AddRange(
                NewCredit(DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(355)),
                NewCredit(DateTime.UtcNow.AddDays(-9), DateTime.UtcNow.AddDays(356)));
            await db.SaveChangesAsync();
        }

        await controller.Publish(OrgId, record.Id, default);
        await controller.Unpublish(OrgId, record.Id, default);
        await controller.Publish(OrgId, record.Id, default);

        await using var after = await f.CreateDbContextAsync();
        Assert.Equal(1, await after.EventCredits.CountAsync(c => c.SpentUtc != null));
    }

    [Fact]
    public async Task A_credit_that_runs_out_before_the_event_is_refused_by_both_dates()
    {
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);

        // The weekend is 30 October 2026; the credit lapses well before it.
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        await using (var db = await f.CreateDbContextAsync())
        {
            db.EventCredits.Add(NewCredit(
                DateTime.UtcNow.AddDays(-360), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Publish(OrgId, record.Id, default)).Result);

        var sentence = Assert.IsType<string>(refusal.Value);
        Assert.Contains("09/20/2026", sentence);
        Assert.Contains("10/30/2026", sentence);

        // And it was not taken for an event it could not cover.
        await using var after = await f.CreateDbContextAsync();
        Assert.Equal(0, await after.EventCredits.CountAsync(c => c.SpentUtc != null));
    }

    private static EventCredit NewCredit(DateTime purchased, DateTime expires)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerOrganizationId = OrgId,
            PriceAtPurchase = 99m,
            PurchasedUtc = purchased,
            ExpiresUtc = expires,
            DateCreated = purchased,
            CreatedByAppUserId = OwnerId,
        };

    // ── a venue the site has never listed ────────────────────────────────────

    [Fact]
    public async Task A_venue_we_have_never_listed_can_be_entered_with_the_event()
    {
        // Ben, 2026-09-11: "They may need to enter the name and address and information about a
        // new venue we have not listed before." Which is how most of them will start.
        var f = await SeedAsync();
        var request = Weekend(place: Guid.Empty) with
        {
            NewVenue = new NewVenueRequest("Waverly Manor", "1 Waverly Road", null, "Lebanon", "TN"),
        };

        var record = Created(await Build(f).Create(OrgId, request, default));

        await using var db = await f.CreateDbContextAsync();
        var place = await db.Places.FirstAsync(p => p.Id == record.PlaceId);

        Assert.Equal("Waverly Manor", place.Name);
        Assert.Equal("Lebanon", place.City);
        // Public, not the private-residence default every other inline place takes. An event is
        // published by definition, so the cautious answer here is the opposite one.
        Assert.Equal(PlaceKind.PublicLocation, place.Kind);
    }

    [Fact]
    public async Task Entering_a_venue_that_already_exists_uses_the_one_that_exists()
    {
        // A venue is shared: the map, the archive and a building's history all hang off one place
        // row, and two rows for one building splits every one of them in half.
        var f = await SeedAsync();
        var controller = Build(f);

        var venue = new NewVenueRequest("Waverly Manor", "1 Waverly Road", null, "Lebanon", "TN");

        var first = Created(await controller.Create(
            OrgId, Weekend("First weekend", place: Guid.Empty) with { NewVenue = venue }, default));
        var second = Created(await controller.Create(
            OrgId, Weekend("Second weekend", place: Guid.Empty) with { NewVenue = venue }, default));

        Assert.Equal(first.PlaceId, second.PlaceId);

        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(1, await db.Places.CountAsync(p => p.Name == "Waverly Manor"));
    }

    [Fact]
    public async Task An_event_with_neither_a_venue_nor_a_new_one_is_refused_in_words()
    {
        var f = await SeedAsync();

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await Build(f).Create(OrgId, Weekend(place: Guid.Empty), default)).Result);

        Assert.Contains("needs a venue", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task A_private_residence_is_refused_as_a_venue()
    {
        var f = await SeedAsync();

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await Build(f).Create(OrgId, Weekend(place: HouseId), default)).Result);

        Assert.Contains("private residence", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task Two_events_of_the_same_name_are_refused_by_name()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        await controller.Create(OrgId, Weekend(), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Create(OrgId, Weekend(), default)).Result);

        Assert.Contains("Thomas House Weekend", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task An_event_with_no_dates_cannot_be_published()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        await using (var db = await f.CreateDbContextAsync())
        {
            db.HostedEventNights.RemoveRange(db.HostedEventNights.Where(n => n.HostedEventId == record.Id));
            await db.SaveChangesAsync();
        }

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Publish(OrgId, record.Id, default)).Result);

        Assert.Contains("at least one night", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task An_end_before_its_start_is_refused()
    {
        var f = await SeedAsync();
        var request = Weekend(from: new DateTime(2026, 11, 1), to: new DateTime(2026, 10, 30));

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await Build(f).Create(OrgId, request, default)).Result);

        Assert.Contains("before the first", Assert.IsType<string>(refusal.Value));
    }

    // ── the layout: matched by id, refused with ids ──────────────────────────
    //
    // Item 235 phase 1, defect 13: renaming a booked seat used to read as delete-and-create, and
    // the delete was refused because the seat was booked. A choice now carries the unit's id, and
    // the one refusal a designer has to DRAW — "these seats still have parties in them" — comes
    // back as a 409 with the ids, so a four-hundred-seat plan can ring the two that matter.

    [Fact]
    public async Task Renaming_a_seat_with_a_confirmed_party_keeps_its_id_and_its_booking()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var plan = Saved(await controller.SetLayout(OrgId, record.Id, Seats(("C4", null), ("C5", null)), default));
        var c4 = plan.Units.Single(u => u.Name == "C4");
        var c5 = plan.Units.Single(u => u.Name == "C5");
        await BookAsync(f, record, c4.Id);

        // Before the id existed this was "remove C4, add C4a", and C4 is booked, so it was refused.
        var renamed = Saved(await controller.SetLayout(OrgId, record.Id,
            Seats(("C4a", c4.Id), ("C5", c5.Id)), default));

        var still = Assert.Single(renamed.Units, u => u.Id == c4.Id);
        Assert.Equal("C4a", still.Name);
        Assert.Equal(2, renamed.Units.Count);

        await using var db = await f.CreateDbContextAsync();
        var night = await db.HostedEventBookingNights.SingleAsync();
        // The party is still in the same seat, whatever it is called now.
        Assert.Equal(c4.Id, night.HostedEventLayoutUnitId);
    }

    [Fact]
    public async Task A_choice_carrying_another_events_unit_id_is_refused_by_name()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var ours = Created(await controller.Create(OrgId, Weekend("Ours"), default));
        var theirs = Created(await controller.Create(OrgId, Weekend("Theirs"), default));

        var theirSeat = Saved(await controller.SetLayout(OrgId, theirs.Id, Seats(("H9", null)), default))
            .Units.Single();

        // Sent against OUR event with THEIR seat's id. Matching it would move their booked seat
        // onto our plan; creating a new unit for it would give it a second identity. Neither.
        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.SetLayout(OrgId, ours.Id, Seats(("H9", theirSeat.Id)), default)).Result);

        var sentence = Assert.IsType<string>(refusal.Value);
        Assert.Contains("H9", sentence);
        Assert.Contains("another event", sentence);

        await using var db = await f.CreateDbContextAsync();
        // Nothing was written on either side.
        Assert.Equal(0, await db.HostedEventLayoutUnits.CountAsync(u => u.HostedEventId == ours.Id));
        Assert.Equal(theirs.Id, (await db.HostedEventLayoutUnits.SingleAsync(u => u.Id == theirSeat.Id)).HostedEventId);
    }

    [Fact]
    public async Task Removing_booked_units_answers_409_naming_exactly_those_units()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var plan = Saved(await controller.SetLayout(OrgId, record.Id,
            Seats(("C4", null), ("C5", null), ("C6", null)), default));
        var c4 = plan.Units.Single(u => u.Name == "C4");
        var c5 = plan.Units.Single(u => u.Name == "C5");
        var c6 = plan.Units.Single(u => u.Name == "C6");
        // Two parties in C4 and C5 across the weekend; C6 is empty. Three nights in C4, so the
        // sentence would read "C4 and C4 and C4 and C5" if the ids were not distinct.
        await BookAsync(f, record, c4.Id, c4.Id, c4.Id);
        await BookAsync(f, record, c5.Id);

        // Keep only C6. C4 and C5 would go, and they cannot.
        var refusal = Assert.IsType<ConflictObjectResult>(
            (await controller.SetLayout(OrgId, record.Id, Seats(("C6", c6.Id)), default)).Result);

        var body = Assert.IsType<LayoutRefusalRecord>(refusal.Value);
        // The exact sentence a person read before the ids arrived — the record adds, it does not
        // reword.
        Assert.Equal("C4 and C5 still have confirmed bookings. Move those parties first.", body.Sentence);
        // Exactly the booked units, once each, and not the empty one being kept.
        Assert.Equal([c4.Id, c5.Id], body.UnitIds);

        await using var db = await f.CreateDbContextAsync();
        // And nothing was removed — refused means refused.
        Assert.Equal(3, await db.HostedEventLayoutUnits.CountAsync(u => u.HostedEventId == record.Id));
    }

    [Fact]
    public async Task The_same_unit_sent_twice_is_refused_by_name()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));
        var c4 = Saved(await controller.SetLayout(OrgId, record.Id, Seats(("C4", null)), default)).Units.Single();

        // Two choices, one row: whichever was written last would silently win.
        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.SetLayout(OrgId, record.Id, Seats(("C4", c4.Id), ("C4b", c4.Id)), default)).Result);

        Assert.Contains("C4", Assert.IsType<string>(refusal.Value));
        Assert.Contains("twice", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task A_rooms_plan_still_matches_by_room_when_no_id_is_sent()
    {
        // The designer's first save, and any older client, sends no ids. A Rooms plan matched by
        // the room before ids existed and must go on doing so, or that first save would delete and
        // recreate every unit and every booking would lose its room.
        var f = await SeedAsync();
        var (blue, red) = await RoomsAsync(f);
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var first = Saved(await controller.SetLayout(OrgId, record.Id, Rooms((blue, null), (red, null)), default));
        var blueUnit = first.Units.Single(u => u.PlaceRoomId == blue);
        await BookAsync(f, record, blueUnit.Id);

        // Saved again with no ids and a new capacity on the Blue Room — the unit is the same unit.
        var second = Saved(await controller.SetLayout(OrgId, record.Id,
            new SetHostedEventLayoutRequest(HostedEventLayoutKind.Rooms,
            [
                new HostedEventLayoutUnitChoice(PlaceRoomId: blue, Capacity: 3),
                new HostedEventLayoutUnitChoice(PlaceRoomId: red),
            ]), default));

        var kept = Assert.Single(second.Units, u => u.PlaceRoomId == blue);
        Assert.Equal(blueUnit.Id, kept.Id);
        Assert.Equal(3, kept.Holds);
        Assert.Equal(first.Units.Single(u => u.PlaceRoomId == red).Id,
                     second.Units.Single(u => u.PlaceRoomId == red).Id);
    }

    [Fact]
    public async Task A_rooms_unit_matched_by_id_can_move_to_another_room_and_keep_its_party()
    {
        // "Whatever its label or room now says." The Blue Room's boiler fails on the Thursday and
        // the venue points that unit at the Red Room instead; the party confirmed into it goes too.
        var f = await SeedAsync();
        var (blue, red) = await RoomsAsync(f);
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var unit = Saved(await controller.SetLayout(OrgId, record.Id, Rooms((blue, null)), default)).Units.Single();
        await BookAsync(f, record, unit.Id);

        var moved = Saved(await controller.SetLayout(OrgId, record.Id, Rooms((red, unit.Id)), default)).Units.Single();

        Assert.Equal(unit.Id, moved.Id);
        Assert.Equal(red, moved.PlaceRoomId);
        Assert.Equal("Red Room", moved.Name);

        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(unit.Id, (await db.HostedEventBookingNights.SingleAsync()).HostedEventLayoutUnitId);
    }

    private static SetHostedEventLayoutRequest Seats(params (string Label, Guid? Id)[] seats)
        => new(HostedEventLayoutKind.Seats,
               seats.Select(s => new HostedEventLayoutUnitChoice(Id: s.Id, Label: s.Label)).ToList());

    private static SetHostedEventLayoutRequest Rooms(params (Guid PlaceRoomId, Guid? Id)[] rooms)
        => new(HostedEventLayoutKind.Rooms,
               rooms.Select(r => new HostedEventLayoutUnitChoice(Id: r.Id, PlaceRoomId: r.PlaceRoomId)).ToList());

    /// <summary>Two of the venue's rooms, described by this group, so a Rooms plan has something to offer.</summary>
    private static async Task<(Guid Blue, Guid Red)> RoomsAsync(IDbContextFactory<BenDataContext> f)
    {
        await using var db = await f.CreateDbContextAsync();
        var blue = Guid.NewGuid();
        var red = Guid.NewGuid();
        foreach (var (id, name) in new[] { (blue, "Blue Room"), (red, "Red Room") })
        {
            db.PlaceRooms.Add(new PlaceRoom
            {
                Id = id, OrganizationId = OrgId, PlaceId = VenueId, Name = name, Capacity = 2,
                IsBookable = true, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
        }
        await db.SaveChangesAsync();
        return (blue, red);
    }

    /// <summary>
    /// A confirmed party in the given units, one night each in order — the same unit three times is
    /// a three-night stay.
    /// </summary>
    private static async Task BookAsync(
        IDbContextFactory<BenDataContext> f, HostedEventRecord record, params Guid[] unitIds)
    {
        await using var db = await f.CreateDbContextAsync();
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = record.Id, LeadAppUserId = OwnerId,
            PartySize = 1, Kind = HostedEventBookingKind.Overnight,
            Status = HostedEventBookingStatus.Confirmed,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        };
        for (var i = 0; i < unitIds.Length; i++)
        {
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                HostedEventNightId = record.Nights[i].Id, HostedEventLayoutUnitId = unitIds[i],
                DateCreated = DateTime.UtcNow,
            });
        }
        db.HostedEventBookings.Add(booking);
        await db.SaveChangesAsync();
    }

    private static HostedEventLayoutRecord Saved(ActionResult<HostedEventLayoutRecord> result)
    {
        // As with Created: the refusals are sentences, so a failure here reads the sentence out.
        if (result.Result is BadRequestObjectResult bad)
            Assert.Fail($"Saving the plan was refused: {bad.Value}");
        if (result.Result is ConflictObjectResult conflict)
            Assert.Fail($"Saving the plan was refused: {(conflict.Value as LayoutRefusalRecord)?.Sentence ?? conflict.Value}");

        return Assert.IsType<HostedEventLayoutRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
    }
}
