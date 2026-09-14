using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Web.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The host's at-a-glance numbers (item 235 phase 17a, audit finding A5): places counted per night, holds counted as
/// taken, blocks never counted twice, and the numbers kept from a member who may not read the board.
/// </summary>
public sealed class HostedEventSummaryTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        var guests = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in guests.Append(HostId).Append(MemberId))
            db.Users.Add(new AppUser { Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test", DateCreated = now });

        var placeId = Guid.NewGuid();
        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = placeId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = placeId, Name = "Seance Weekend", UrlName = "seance-weekend",
            StartsOn = now.Date.AddDays(-1), EndsOn = now.Date, LifecycleState = HostedEventLifecycleState.Live,
            LayoutKind = HostedEventLayoutKind.Seats, DayPassCapacity = 40, MinimumGuests = 12,
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        var friday = Guid.NewGuid();
        var saturday = Guid.NewGuid();
        db.HostedEventNights.Add(new HostedEventNight { Id = friday, HostedEventId = EventId, Date = now.Date.AddDays(-1), DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventNights.Add(new HostedEventNight { Id = saturday, HostedEventId = EventId, Date = now.Date, DateCreated = now, CreatedByAppUserId = HostId });

        var units = new[] { "A1", "A2", "A3" }.Select(label => new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Label = label, Capacity = 1, DateCreated = now, CreatedByAppUserId = HostId,
        }).ToArray();
        db.HostedEventLayoutUnits.AddRange(units);

        // A3 held back every night — and also named for Saturday, which must not take it away twice. A2 held back Friday.
        db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock { Id = Guid.NewGuid(), HostedEventLayoutUnitId = units[2].Id, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock { Id = Guid.NewGuid(), HostedEventLayoutUnitId = units[2].Id, HostedEventNightId = saturday, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock { Id = Guid.NewGuid(), HostedEventLayoutUnitId = units[1].Id, HostedEventNightId = friday, DateCreated = now, CreatedByAppUserId = HostId });
        await db.SaveChangesAsync();

        HostedEventBooking Book(Guid lead, HostedEventBookingStatus status, int party, HostedEventBookingKind kind, params (Guid Night, Guid Unit)[] places)
        {
            var booking = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = lead, PartySize = party, Kind = kind, Status = status,
                DateCreated = now, CreatedByAppUserId = lead,
            };
            foreach (var (night, unit) in places)
                booking.Nights.Add(new HostedEventBookingNight
                {
                    Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, HostedEventNightId = night, HostedEventLayoutUnitId = unit,
                    ReleasedUtc = status is HostedEventBookingStatus.TurnedDown ? now : null, DateCreated = now,
                });
            db.HostedEventBookings.Add(booking);
            return booking;
        }

        var coming = Book(guests[0], HostedEventBookingStatus.Confirmed, 2, HostedEventBookingKind.Overnight, (friday, units[0].Id), (saturday, units[0].Id));
        Book(guests[1], HostedEventBookingStatus.Held, 1, HostedEventBookingKind.Overnight, (saturday, units[1].Id));
        Book(guests[2], HostedEventBookingStatus.Requested, 3, HostedEventBookingKind.Overnight);
        Book(guests[3], HostedEventBookingStatus.TurnedDown, 2, HostedEventBookingKind.Overnight, (saturday, units[1].Id));
        Book(guests[4], HostedEventBookingStatus.Confirmed, 3, HostedEventBookingKind.DayPass);
        await db.SaveChangesAsync();

        foreach (var night in new[] { friday, saturday })
            db.HostedEventCheckIns.Add(new HostedEventCheckIn
            {
                Id = Guid.NewGuid(), HostedEventBookingId = coming.Id, HostedEventNightId = night, ArrivedUtc = now,
                RecordedByAppUserId = HostId, DateCreated = now, CreatedByAppUserId = HostId,
            });
        db.HostedEventWalkUps.Add(new HostedEventWalkUp { Id = Guid.NewGuid(), HostedEventNightId = friday, People = 4, ArrivedUtc = now, RecordedByAppUserId = HostId, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventReviews.Add(new HostedEventReview { Id = Guid.NewGuid(), HostedEventId = EventId, AppUserId = guests[0], Stars = 5, DateCreated = now, CreatedByAppUserId = guests[0] });
        db.HostedEventReviews.Add(new HostedEventReview { Id = Guid.NewGuid(), HostedEventId = EventId, AppUserId = guests[4], Stars = 4, DateCreated = now, CreatedByAppUserId = guests[4] });
        db.HostedEventReviews.Add(new HostedEventReview { Id = Guid.NewGuid(), HostedEventId = EventId, AppUserId = guests[5], Stars = 1, HiddenAtUtc = now, DateCreated = now, CreatedByAppUserId = guests[5] });
        await db.SaveChangesAsync();
        return sqlite;
    }

    private static HostedEventSummaryController Controller(SqliteTestDb sqlite, Guid who)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _) => user == HostId);
        return new HostedEventSummaryController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, who.ToString())], "Bearer")),
                },
            },
        };
    }

    [Fact]
    public async Task The_numbers_count_places_per_night_holds_as_taken_and_blocks_once()
    {
        await using var sqlite = await SeedAsync();

        var summary = Assert.IsType<HostedEventSummaryRecord>(Assert.IsType<OkObjectResult>(
            (await Controller(sqlite, HostId).Get(OrgId, EventId, default)).Result).Value);

        Assert.Equal(1, summary.Asking);
        Assert.Equal(1, summary.Holding);
        Assert.Equal((2, 5), (summary.ConfirmedParties, summary.ConfirmedPeople));
        Assert.Equal(1, summary.NotComing);
        // Three seats over two nights is six; A3 is gone both nights and A2 on Friday: three left to sell.
        Assert.Equal(3, summary.PlacesOffered);
        // A1 both nights for the confirmed party, A2 Saturday for the hold; the turned-down party's released A2 is not.
        Assert.Equal(3, summary.PlacesTaken);
        Assert.Equal((40, 3), (summary.DayPassCapacity, summary.DayPassPeople));
        Assert.Equal(1, summary.ArrivedParties);
        Assert.Equal(4, summary.WalkUpPeople);
        Assert.Equal((4.5m, 2), (summary.ReviewAverage, summary.ReviewCount));
        Assert.Equal(12, summary.MinimumGuests);
    }

    [Fact]
    public async Task A_member_who_may_not_read_the_board_is_refused_the_numbers()
    {
        await using var sqlite = await SeedAsync();

        Assert.IsType<ForbidResult>((await Controller(sqlite, MemberId).Get(OrgId, EventId, default)).Result);
        Assert.IsType<NotFoundResult>((await Controller(sqlite, HostId).Get(Guid.NewGuid(), EventId, default)).Result);
    }
}
