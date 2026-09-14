using System.Security.Claims;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A new plan starts from the one used at the same venue before (item 235 phase 12).
/// </summary>
public sealed class HostedEventEarlierPlanTests
{
    private static readonly Guid UsId = Guid.NewGuid();
    private static readonly Guid VenueOrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid ElsewhereId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();

    private static readonly Guid NewEventId = Guid.NewGuid();
    private static readonly Guid VenuesEventId = Guid.NewGuid();
    private static readonly Guid OurOldEventId = Guid.NewGuid();
    private static readonly Guid OtherPublishedId = Guid.NewGuid();
    private static readonly Guid OtherDraftId = Guid.NewGuid();
    private static readonly Guid ElsewhereEventId = Guid.NewGuid();

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        db.Users.Add(new AppUser { Id = HostId, Email = "h@example.test", UserName = "h@example.test", DateCreated = now });
        foreach (var (id, name) in new[] { (UsId, "Us"), (VenueOrgId, "The Thomas House"), (OtherOrgId, "Others") })
            db.Organizations.Add(new Organization { Id = id, Name = name, UrlName = name.ToLowerInvariant().Replace(' ', '-'), DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = ElsewhereId, Name = "Somewhere else", DateCreated = now, CreatedByAppUserId = HostId });
        db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile { Id = Guid.NewGuid(), OrganizationId = VenueOrgId, PlaceId = PlaceId, VerifiedUtc = now, DateCreated = now, CreatedByAppUserId = HostId });

        void Event(Guid id, Guid org, Guid place, HostedEventLifecycleState state, int daysAgo, decimal price)
        {
            db.HostedEvents.Add(new HostedEvent
            {
                Id = id, OrganizationId = org, PlaceId = place, Name = $"Event {id:N}"[..14], UrlName = $"e-{id:N}"[..10],
                StartsOn = now.Date.AddDays(-daysAgo), EndsOn = now.Date.AddDays(-daysAgo), LifecycleState = state,
                LayoutKind = HostedEventLayoutKind.Seats, DateCreated = now, CreatedByAppUserId = HostId,
            });
            if (id == NewEventId) return;
            db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit
            {
                Id = Guid.NewGuid(), HostedEventId = id, Label = "A1", Section = "Stalls", Capacity = 1, Price = price, Note = "Behind a pillar",
                LayoutRow = 0, LayoutColumn = 0, DateCreated = now, CreatedByAppUserId = HostId,
            });
        }

        Event(NewEventId, UsId, PlaceId, HostedEventLifecycleState.Draft, -30, 0);
        Event(OurOldEventId, UsId, PlaceId, HostedEventLifecycleState.Archived, 200, 30);
        Event(VenuesEventId, VenueOrgId, PlaceId, HostedEventLifecycleState.Ended, 400, 18);
        Event(OtherPublishedId, OtherOrgId, PlaceId, HostedEventLifecycleState.Published, 10, 99);
        Event(OtherDraftId, OtherOrgId, PlaceId, HostedEventLifecycleState.Draft, 5, 99);
        Event(ElsewhereEventId, UsId, ElsewhereId, HostedEventLifecycleState.Ended, 50, 10);

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static HostedEventAfterController Controller(SqliteTestDb sqlite)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new HostedEventAfterController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object), new HostedEventCalendarSync(), new Mock<IMediaIngestService>().Object,
            new Mock<IFileStorageService>().Object, NullLogger<HostedEventAfterController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, HostId.ToString())], "Bearer")),
                },
            },
        };
    }

    [Fact]
    public async Task The_venues_plan_comes_first_then_ours_and_never_another_groups_draft_or_another_building()
    {
        await using var sqlite = await SeedAsync();
        await using var db = await sqlite.NewContextAsync();
        var ev = await db.HostedEvents.SingleAsync(e => e.Id == NewEventId);

        var plans = await HostedEventAfterController.EarlierAsync(db, ev, default);

        Assert.Equal([VenuesEventId, OurOldEventId, OtherPublishedId], plans.Select(p => p.HostedEventId));
        Assert.True(plans[0].FromTheVenue);
        Assert.True(plans[1].Ours);
    }

    [Fact]
    public async Task Another_groups_prices_and_notes_stay_with_them()
    {
        await using var sqlite = await SeedAsync();

        var theirs = Assert.IsType<EarlierPlanUnitsRecord>(Assert.IsType<OkObjectResult>(
            (await Controller(sqlite).EarlierPlanUnits(UsId, NewEventId, VenuesEventId, default)).Result).Value);
        var seat = Assert.Single(theirs.Units);
        Assert.Equal("A1", seat.Name);
        Assert.Equal("Stalls", seat.Section);
        Assert.Null(seat.Price);
        Assert.Null(seat.Note);

        var ours = Assert.IsType<EarlierPlanUnitsRecord>(Assert.IsType<OkObjectResult>(
            (await Controller(sqlite).EarlierPlanUnits(UsId, NewEventId, OurOldEventId, default)).Result).Value);
        Assert.Equal(30, Assert.Single(ours.Units).Price);
    }

    [Fact]
    public async Task A_plan_that_is_not_offered_cannot_be_read_by_its_id()
    {
        await using var sqlite = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Controller(sqlite).EarlierPlanUnits(UsId, NewEventId, OtherDraftId, default)).Result);
        Assert.IsType<NotFoundResult>((await Controller(sqlite).EarlierPlanUnits(UsId, NewEventId, ElsewhereEventId, default)).Result);
    }
}
