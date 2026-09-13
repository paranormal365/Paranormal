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
    /// <summary>A mailer with nothing behind it — what every environment has by default.</summary>
    /// <remarks>
    /// Injected per-action from phase 6, because the guest's own doors send letters now. Not
    /// configured, so nothing is sent and every one of these tests still tests the decision rather
    /// than the post.
    /// </remarks>
    private static EventGuestMailer NoMail()
    {
        var email = new Moq.Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);

        return new EventGuestMailer(
            email.Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EventGuestMailer>.Instance);
    }

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
            new HostedEventCalendarSync(), new HostedEventEntitlement(limits),
            new SiteSettingsService(f))
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
               DefaultEndLocal: new TimeSpan(23, 0, 0),
               // Publishable, because most of these tests are about what happens AFTER publishing
               // and the readiness gate is tested on its own. An event with no way to reach the
               // venue is genuinely not ready, and every one of these would otherwise be asserting
               // that fact over and over instead of the thing it is named for.
               ContactLine: "Call the hotel on (615) 555-0142 to settle up.");

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

        Assert.Equal(HostedEventLifecycleState.Draft, record.LifecycleState);
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

        Assert.Equal(HostedEventLifecycleState.Published, published.LifecycleState);
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

        await controller.Cancel(OrgId, record.Id, new CancelHostedEventRequest("Burst pipe"), NoMail(), default);

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
        Assert.Equal(HostedEventLifecycleState.Draft, still.LifecycleState);
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

        Assert.Equal(HostedEventLifecycleState.Published, published.LifecycleState);
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
    public async Task A_rooms_unit_cannot_be_pointed_at_a_different_room_and_the_refusal_says_what_to_do()
    {
        // The Blue Room's boiler fails and the venue tries to point that unit at the Red Room.
        // Refused, for two reasons that agree. A unit's room is its identity, so repointing it
        // would move whoever is confirmed into it without telling anybody — the honest way is to
        // take one off the plan and add the other, where the removal is refused by name if a party
        // is in it. And mechanically it cannot be allowed: (HostedEventId, PlaceRoomId) is uniquely
        // indexed, so two units swapping rooms in one save collide inside a single SaveChanges and
        // the venue would meet a database error instead of a sentence.
        var f = await SeedAsync();
        var (blue, red) = await RoomsAsync(f);
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var unit = Saved(await controller.SetLayout(OrgId, record.Id, Rooms((blue, null)), default)).Units.Single();
        await BookAsync(f, record, unit.Id);

        var refusal = Refused(await controller.SetLayout(OrgId, record.Id, Rooms((red, unit.Id)), default));

        Assert.Contains("Blue Room", refusal.Sentence);
        Assert.Contains("Take it off the plan and add the other room", refusal.Sentence);
        Assert.Equal([unit.Id], refusal.UnitIds);

        // Nothing moved: the party is still in the room the venue agreed to.
        await using var db = await f.CreateDbContextAsync();
        var stored = await db.HostedEventLayoutUnits.SingleAsync();
        Assert.Equal(blue, stored.PlaceRoomId);
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

    /// <summary>
    /// The refusal a save was expected to produce, as the record a designer can act on.
    /// </summary>
    /// <remarks>
    /// A 409 carrying <see cref="LayoutRefusalRecord"/> rather than a 400 carrying a sentence,
    /// because the designer needs the ids to ring the offending units and the status is how it
    /// knows they are there. A save that unexpectedly succeeded fails here by name.
    /// </remarks>
    private static LayoutRefusalRecord Refused(ActionResult<HostedEventLayoutRecord> result)
    {
        if (result.Result is OkObjectResult) Assert.Fail("The plan saved; a refusal was expected.");
        if (result.Result is BadRequestObjectResult bad)
            Assert.Fail($"Refused with a plain sentence, not a record a designer can use: {bad.Value}");

        return Assert.IsType<LayoutRefusalRecord>(
            Assert.IsType<ConflictObjectResult>(result.Result).Value);
    }

    // ── how guests get a place (phase 3) ─────────────────────────────────────

    [Fact]
    public async Task How_guests_get_a_place_can_be_switched_while_nobody_is_waiting()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        // Ask is the default, because it is what every event on the branch already did.
        Assert.Equal(HostedEventBookingMode.Ask, record.BookingMode);

        var switched = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.SetBookingMode(
                OrgId, record.Id, new SetHostedEventBookingModeRequest(HostedEventBookingMode.Pick),
                default)).Result).Value);

        Assert.Equal(HostedEventBookingMode.Pick, switched.BookingMode);
    }

    [Fact]
    public async Task Switching_how_guests_get_a_place_is_refused_once_anybody_is_waiting()
    {
        // The refusal exists because the switch would change what existing bookings MEAN: holds
        // become nothing, asks become places nobody chose. Both are silent, and both are found out
        // on the night.
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        await using (var db = await f.CreateDbContextAsync())
        {
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = record.Id, LeadAppUserId = OwnerId,
                Kind = HostedEventBookingKind.Overnight,
                Status = HostedEventBookingStatus.Requested, PartySize = 2,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var refusal = Assert.IsType<ConflictObjectResult>((await controller.SetBookingMode(
            OrgId, record.Id, new SetHostedEventBookingModeRequest(HostedEventBookingMode.Pick),
            default)).Result);

        var sentence = Assert.IsType<string>(refusal.Value);
        Assert.Contains("1 party is already booked or waiting", sentence);
        Assert.Contains("Decide the ones that are waiting", sentence);
    }

    [Fact]
    public async Task A_turned_down_party_does_not_stop_the_switch()
    {
        // Only the ones still waiting on an answer count. A party turned down last month has no
        // booking whose meaning could change.
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        await using (var db = await f.CreateDbContextAsync())
        {
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = record.Id, LeadAppUserId = OwnerId,
                Kind = HostedEventBookingKind.DayPass,
                Status = HostedEventBookingStatus.TurnedDown, PartySize = 2,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>((await controller.SetBookingMode(
            OrgId, record.Id, new SetHostedEventBookingModeRequest(HostedEventBookingMode.Pick),
            default)).Result);
    }

    // ── the settings that used to be unreachable ─────────────────────────────

    [Fact]
    public async Task The_settings_a_booking_needs_can_actually_be_saved()
    {
        // Every one of these was on the record and on the table with no way to set it. A field a
        // screen can read and nothing can write is a field that is always empty.
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var closes = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        var decideBy = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        var saved = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.Update(OrgId, record.Id, Weekend() with
            {
                DayPassPrice = 45m,
                BookingsCloseAtUtc = closes,
                HoldMinutes = 1440,
                VenueArrangement = HostedEventVenueArrangement.External,
                VenueContactName = "Mrs Cole",
                VenueAgreedOnUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                VenueReference = "INV-4021",
                MinimumGuests = 12,
                GoNoGoDeadlineUtc = decideBy,
            }, default)).Result).Value);

        Assert.Equal(45m, saved.DayPassPrice);
        Assert.Equal(closes, saved.BookingsCloseAtUtc);
        Assert.Equal(1440, saved.HoldMinutes);
        Assert.Equal(HostedEventVenueArrangement.External, saved.VenueArrangement);
        Assert.Equal("Mrs Cole", saved.VenueContactName);
        Assert.Equal("INV-4021", saved.VenueReference);
        Assert.Equal(12, saved.MinimumGuests);
        Assert.Equal(decideBy, saved.GoNoGoDeadlineUtc);
    }

    [Fact]
    public async Task A_hold_shorter_than_a_quarter_hour_is_refused_in_words()
    {
        // The database has this as a check constraint, which answers a 500. A person typing five
        // minutes into a box deserves a sentence.
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Update(OrgId, record.Id,
                Weekend() with { HoldMinutes = 5 }, default)).Result);

        Assert.Contains("between 15 minutes and 14 days", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task A_decision_date_after_the_event_starts_is_refused()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Update(OrgId, record.Id, Weekend() with
            {
                MinimumGuests = 10,
                GoNoGoDeadlineUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
            }, default)).Result);

        Assert.Contains("too late to be a decision", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task A_decision_date_with_nothing_to_decide_against_is_refused()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Update(OrgId, record.Id, Weekend() with
            {
                GoNoGoDeadlineUtc = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            }, default)).Result);

        Assert.Contains("needs a minimum number", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task Saying_it_is_your_own_venue_and_naming_who_agreed_is_refused()
    {
        // Two contradictory answers saved together is a record nobody can act on: which is it?
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Update(OrgId, record.Id, Weekend() with
            {
                VenueArrangement = HostedEventVenueArrangement.Self,
                VenueContactName = "Mrs Cole",
            }, default)).Result);

        Assert.Contains("Pick one", Assert.IsType<string>(refusal.Value));
    }

    // ── the checklist the button refuses from ────────────────────────────────

    [Fact]
    public async Task The_checklist_and_the_refusal_are_the_same_sentence()
    {
        // The property that makes the checklist worth having: what the card says and what the
        // button says are one string, so they can never read as two different problems.
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(
            OrgId, Weekend() with { ContactLine = null }, default));

        var checklist = Assert.IsType<List<HostedEventReadinessItem>>(
            Assert.IsType<OkObjectResult>(
                (await controller.Readiness(OrgId, record.Id, default)).Result).Value);

        var blocker = Assert.Single(checklist, i => !i.Done);

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Publish(OrgId, record.Id, default)).Result);

        Assert.Equal(blocker.Sentence, Assert.IsType<string>(refusal.Value));
    }

    // ── the credit, and what "finalized" means (phase 3) ─────────────────────

    /// <summary>
    /// Calling an event off well before it starts hands the credit back AND unpays the event.
    /// </summary>
    /// <remarks>
    /// The two are one fact. Handing back the credit while leaving the event marked as paid for
    /// would let somebody cancel, keep the credit, and publish the same event again for nothing.
    /// </remarks>
    [Fact]
    public async Task Calling_it_off_in_good_time_gives_the_credit_back_and_unpays_the_event()
    {
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);

        // Far enough out that the 48-hour window is nowhere near.
        var starts = DateTime.UtcNow.AddDays(60);
        var record = Created(await controller.Create(OrgId, Weekend(
            from: starts, to: starts.AddDays(1)), default));

        Guid creditId;
        await using (var db = await f.CreateDbContextAsync())
        {
            var credit = NewCredit(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(364));
            creditId = credit.Id;
            db.EventCredits.Add(credit);
            await db.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>((await controller.Publish(OrgId, record.Id, default)).Result);

        var off = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.Cancel(
                OrgId, record.Id, new CancelHostedEventRequest("Nobody could come"), NoMail(), default)).Result).Value);

        Assert.Contains("comes back", off.PlanNote ?? "");

        await using var after = await f.CreateDbContextAsync();
        var back = await after.EventCredits.FirstAsync(c => c.Id == creditId);
        Assert.Null(back.SpentUtc);
        Assert.Null(back.SpentOnHostedEventId);
        // Not refunded: no money moved. Ben, 2026-09-12: "We don't refund money, only credit."
        Assert.Null(back.RefundedUtc);

        var unpaid = await after.HostedEvents.FirstAsync(e => e.Id == record.Id);
        Assert.Null(unpaid.FirstPublishedUtc);
    }

    [Fact]
    public async Task Calling_it_off_at_the_last_minute_does_not_give_the_credit_back()
    {
        // The event was advertised, it took bookings, and people arranged a weekend around it.
        // Inside the window the host has had the benefit, and the event stays paid for — so
        // putting it back up costs nothing, which is the other half of the same fairness.
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);

        var starts = DateTime.UtcNow.AddHours(6);
        var record = Created(await controller.Create(OrgId, Weekend(
            from: starts, to: starts.AddDays(1)), default));

        Guid creditId;
        await using (var db = await f.CreateDbContextAsync())
        {
            var credit = NewCredit(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(364));
            creditId = credit.Id;
            db.EventCredits.Add(credit);
            await db.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>((await controller.Publish(OrgId, record.Id, default)).Result);

        var off = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.Cancel(
                OrgId, record.Id, new CancelHostedEventRequest("Called off"), NoMail(), default)).Result).Value);

        Assert.Contains("does not come back", off.PlanNote ?? "");

        await using var after = await f.CreateDbContextAsync();
        Assert.NotNull((await after.EventCredits.FirstAsync(c => c.Id == creditId)).SpentUtc);
        Assert.NotNull((await after.HostedEvents.FirstAsync(e => e.Id == record.Id)).FirstPublishedUtc);
    }

    [Fact]
    public async Task The_cancel_screen_can_ask_what_would_happen_before_anything_happens()
    {
        // Finding out afterwards that ninety-nine dollars did not come back is the conversation
        // this endpoint exists to avoid, and it answers in the same words the cancel will use.
        var f = await SeedAsync();
        var controller = Build(f);
        var starts = DateTime.UtcNow.AddDays(60);
        var record = Created(await controller.Create(OrgId, Weekend(
            from: starts, to: starts.AddDays(1)), default));

        var effect = Assert.IsType<HostedEventCancellationEffect>(
            Assert.IsType<OkObjectResult>(
                (await controller.CancellationEffect(OrgId, record.Id, default)).Result).Value);

        Assert.False(effect.CreditComesBack);
        Assert.Contains("nothing to come back", effect.Sentence);
    }

    [Fact]
    public async Task An_event_that_has_happened_cannot_be_walked_back_into_a_draft()
    {
        // Ben, 2026-09-12: "Unless credit refunded, we can assume the event completed and is
        // finalized." Without this the endpoint would take an ended event to Draft, and since its
        // credit never came back it would then publish again for nothing: one credit, two events.
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        Assert.IsType<OkObjectResult>((await controller.Publish(OrgId, record.Id, default)).Result);

        await using (var db = await f.CreateDbContextAsync())
        {
            var hosted = await db.HostedEvents.FirstAsync(e => e.Id == record.Id);
            hosted.LifecycleState = HostedEventLifecycleState.Ended;
            hosted.EndedAtUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Unpublish(OrgId, record.Id, default)).Result);

        Assert.Contains("already happened", Assert.IsType<string>(refusal.Value));
    }

    // ── minimum numbers ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_event_with_no_minimum_has_nothing_to_decide()
    {
        var f = await SeedAsync();
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        var refusal = Assert.IsType<BadRequestObjectResult>(
            (await controller.Go(OrgId, record.Id, NoMail(), default)).Result);

        Assert.Contains("nothing to decide", Assert.IsType<string>(refusal.Value));
    }

    [Fact]
    public async Task Saying_no_calls_it_off_under_exactly_the_same_rules()
    {
        // Routed through cancelling rather than being a state of its own: the people with places
        // have to be told, the listing has to say so, and the credit has to follow the same rule.
        var f = await SeedAsync();
        var controller = Build(f);
        var starts = DateTime.UtcNow.AddDays(60);
        var record = Created(await controller.Create(OrgId, Weekend(
            from: starts, to: starts.AddDays(1)) with { MinimumGuests = 20 }, default));

        var off = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.NoGo(OrgId, record.Id, NoMail(), default)).Result).Value);

        Assert.Equal(HostedEventLifecycleState.Cancelled, off.LifecycleState);
        Assert.Equal(HostedEventGoNoGo.NoGo, off.GoNoGoDecision);
        // Counted rather than claimed: nobody had a place at this one, and the note says so
        // rather than announcing letters that were never sent (item 235 phase 6).
        Assert.Contains("Nobody had a place to lose", off.PlanNote ?? "");
    }

    [Fact]
    public async Task Bringing_a_called_off_event_back_reopens_the_decision_that_called_it_off()
    {
        // Otherwise a live event would sit there permanently marked "no go", and its reminders
        // would never run again.
        var f = await SeedAsync();
        var controller = Build(f);
        var starts = DateTime.UtcNow.AddDays(60);
        var record = Created(await controller.Create(OrgId, Weekend(
            from: starts, to: starts.AddDays(1)) with { MinimumGuests = 20 }, default));

        Assert.IsType<OkObjectResult>((await controller.NoGo(OrgId, record.Id, NoMail(), default)).Result);

        var back = Assert.IsType<HostedEventRecord>(
            Assert.IsType<OkObjectResult>((await controller.Uncancel(OrgId, record.Id, default)).Result).Value);

        Assert.Equal(HostedEventLifecycleState.Draft, back.LifecycleState);
        Assert.Equal(HostedEventGoNoGo.Undecided, back.GoNoGoDecision);
    }

    // ── drafting is free, publishing is what costs ───────────────────────────

    /// <summary>
    /// A whole event can be built, edited and laid out without spending anything.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-12:</b> <i>"When creating an event, you can draft it out from
    /// beginning to end without it deducting the credit. When you set it live, then credit is
    /// deducted unless withdrawn."</i></para>
    ///
    /// <para>Worth a test of its own rather than trusting that no future phase adds a second place
    /// that charges. Every later phase adds screens to a draft — the programme, the staff, the
    /// files, the page — and any one of them could reach for the entitlement without anybody
    /// noticing until a host complained that setting up cost them ninety-nine dollars.</para>
    /// </remarks>
    [Fact]
    public async Task Drafting_a_whole_event_costs_nothing()
    {
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);

        Guid creditId;
        await using (var db = await f.CreateDbContextAsync())
        {
            var credit = NewCredit(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(364));
            creditId = credit.Id;
            db.EventCredits.Add(credit);
            await db.SaveChangesAsync();
        }

        // Build it out, end to end: create, rename, add a description, change the dates, lay a
        // plan out, and change the plan again.
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        Assert.IsType<OkObjectResult>((await controller.Update(OrgId, record.Id, Weekend(
            name: "Thomas House Séance Weekend") with
        {
            Description = "Two nights in the most haunted hotel in Tennessee.",
            DayPassCapacity = 20,
            DayPassPrice = 45m,
        }, default)).Result);

        Assert.IsType<OkObjectResult>((await controller.SetLayout(OrgId, record.Id,
            new SetHostedEventLayoutRequest(HostedEventLayoutKind.Seats,
                [new HostedEventLayoutUnitChoice(Label: "A1", Capacity: 1, LayoutRow: 0, LayoutColumn: 0),
                 new HostedEventLayoutUnitChoice(Label: "A2", Capacity: 1, LayoutRow: 0, LayoutColumn: 1)]),
            default)).Result);

        Assert.IsType<OkObjectResult>((await controller.SetLayout(OrgId, record.Id,
            new SetHostedEventLayoutRequest(HostedEventLayoutKind.Seats,
                [new HostedEventLayoutUnitChoice(Label: "A1", Capacity: 1, LayoutRow: 0, LayoutColumn: 0)]),
            default)).Result);

        // Nothing has been charged, and nothing is marked as ever having been live.
        await using var after = await f.CreateDbContextAsync();
        var untouched = await after.EventCredits.FirstAsync(c => c.Id == creditId);
        Assert.Null(untouched.SpentUtc);
        Assert.Null(untouched.SpentOnHostedEventId);

        var still = await after.HostedEvents.FirstAsync(e => e.Id == record.Id);
        Assert.Equal(HostedEventLifecycleState.Draft, still.LifecycleState);
        Assert.Null(still.FirstPublishedUtc);
    }

    [Fact]
    public async Task Setting_it_live_is_the_moment_it_costs_and_it_costs_once()
    {
        var f = await SeedAsync();
        await ExcludeHostingAsync(f);
        var controller = Build(f);
        var record = Created(await controller.Create(OrgId, Weekend(), default));

        await using (var db = await f.CreateDbContextAsync())
        {
            db.EventCredits.Add(NewCredit(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(364)));
            db.EventCredits.Add(NewCredit(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(363)));
            await db.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>((await controller.Publish(OrgId, record.Id, default)).Result);

        // Exactly one, and publishing again after taking it down never spends a second.
        await using (var db = await f.CreateDbContextAsync())
            Assert.Equal(1, await db.EventCredits.CountAsync(c => c.SpentUtc != null));

        Assert.IsType<OkObjectResult>((await controller.Unpublish(OrgId, record.Id, default)).Result);
        Assert.IsType<OkObjectResult>((await controller.Publish(OrgId, record.Id, default)).Result);

        await using (var db = await f.CreateDbContextAsync())
            Assert.Equal(1, await db.EventCredits.CountAsync(c => c.SpentUtc != null));
    }

    /// <summary>
    /// A venue pulling out gives the credit back whatever the timing.
    /// </summary>
    /// <remarks>
    /// The forty-eight hour window exists because an organizer who calls their own event off at the
    /// last minute has already had the benefit of it. None of that is true here: they did nothing
    /// wrong, and somebody whose venue withdrew two days beforehand has had the worse week.
    /// </remarks>
    [Fact]
    public void A_venue_withdrawing_returns_the_credit_however_late_it_is()
    {
        var now = DateTime.UtcNow;
        var credit = NewCredit(now.AddDays(-1), now.AddDays(364));
        var startsInAnHour = now.AddHours(1);

        var (organizerCancelled, theirSentence) = EventCredits.WhatCancellingDoesToTheCredit(
            credit, startsInAnHour, now, TimeSpan.FromHours(48));

        Assert.False(organizerCancelled);
        Assert.Contains("does not come back", theirSentence);

        var (venueWithdrew, itsSentence) = EventCredits.WhatCancellingDoesToTheCredit(
            credit, startsInAnHour, now, TimeSpan.FromHours(48), theVenueWithdrew: true);

        Assert.True(venueWithdrew);
        Assert.Contains("whatever the timing", itsSentence);
    }
}
