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
}
