using System.Security.Claims;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Seating confirmed parties at tables, sitting by sitting (item 235 phase 13).
/// </summary>
public sealed class HostedEventDiningTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid FridayId = Guid.NewGuid();
    private static readonly Guid SaturdayId = Guid.NewGuid();
    private static readonly Guid FridayDinnerId = Guid.NewGuid();
    private static readonly Guid SaturdayDinnerId = Guid.NewGuid();
    private static readonly Guid TableOneId = Guid.NewGuid();
    private static readonly Guid TableTwoId = Guid.NewGuid();

    /// <summary>A party of 6 here both nights, a party of 4 on Friday only, one waiting, one cancelled.</summary>
    private static readonly Guid SixId = Guid.NewGuid();
    private static readonly Guid FourId = Guid.NewGuid();
    private static readonly Guid WaitingId = Guid.NewGuid();
    private static readonly Guid CancelledId = Guid.NewGuid();

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        db.Users.Add(new AppUser { Id = HostId, Email = "h@example.test", UserName = "h@example.test", DisplayName = "Host", DateCreated = now });
        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Séance Weekend", UrlName = "seance",
            StartsOn = now.Date.AddDays(30), EndsOn = now.Date.AddDays(31), LifecycleState = HostedEventLifecycleState.Published,
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = FridayId, HostedEventId = EventId, Date = now.Date.AddDays(30), DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventNights.Add(new HostedEventNight { Id = SaturdayId, HostedEventId = EventId, Date = now.Date.AddDays(31), SortOrder = 1, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventMenus.Add(new HostedEventMenu { Id = FridayDinnerId, HostedEventNightId = FridayId, Title = "Dinner", DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventMenus.Add(new HostedEventMenu { Id = SaturdayDinnerId, HostedEventNightId = SaturdayId, Title = "Dinner", DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventDiningTables.Add(new HostedEventDiningTable { Id = TableOneId, HostedEventId = EventId, Name = "Table 1", Seats = 8, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventDiningTables.Add(new HostedEventDiningTable { Id = TableTwoId, HostedEventId = EventId, Name = "Table 2", Seats = 8, SortOrder = 1, DateCreated = now, CreatedByAppUserId = HostId });

        void Party(Guid id, string name, int size, HostedEventBookingStatus status, params Guid[] nights)
        {
            var user = new AppUser { Id = Guid.NewGuid(), Email = $"{name}@example.test", UserName = $"{name}@example.test", DisplayName = name, DateCreated = now };
            db.Users.Add(user);
            var booking = new HostedEventBooking
            {
                Id = id, HostedEventId = EventId, LeadAppUserId = user.Id, PartySize = size, Status = status,
                Kind = HostedEventBookingKind.Overnight, DateCreated = now, CreatedByAppUserId = user.Id,
            };
            foreach (var night in nights)
                booking.Nights.Add(new HostedEventBookingNight { Id = Guid.NewGuid(), HostedEventBookingId = id, HostedEventNightId = night, DateCreated = now });
            booking.Guests.Add(new HostedEventBookingGuest { Id = Guid.NewGuid(), HostedEventBookingId = id, DisplayName = $"{name} junior", DietaryNotes = "No nuts", DateCreated = now });
            db.HostedEventBookings.Add(booking);
        }

        Party(SixId, "Parker", 6, HostedEventBookingStatus.Confirmed, FridayId, SaturdayId);
        Party(FourId, "Nguyen", 4, HostedEventBookingStatus.Confirmed, FridayId);
        Party(WaitingId, "Waiting", 2, HostedEventBookingStatus.Requested, FridayId);
        Party(CancelledId, "Gone", 2, HostedEventBookingStatus.Confirmed, FridayId);

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static HostedEventDiningController Dining(SqliteTestDb sqlite)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new HostedEventDiningController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object, new HostedEventAccess(security.Object))
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

    private static HostedEventDiningRecord Ok(ActionResult<HostedEventDiningRecord> result)
        => Assert.IsType<HostedEventDiningRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static string Refused(ActionResult<HostedEventDiningRecord> result)
        => (string)Assert.IsType<BadRequestObjectResult>(result.Result).Value!;

    [Fact]
    public async Task Only_confirmed_parties_here_that_night_are_offered()
    {
        await using var sqlite = await SeedAsync();

        var friday = Ok(await Dining(sqlite).Get(OrgId, EventId, FridayDinnerId, default));
        Assert.Equal(["Gone", "Nguyen", "Parker"], friday.Parties.Select(p => p.LeadName));
        Assert.Contains("Parker junior: No nuts", friday.Parties.Single(p => p.LeadName == "Parker").DietaryNotes);

        var saturday = Ok(await Dining(sqlite).Get(OrgId, EventId, SaturdayDinnerId, default));
        Assert.Equal(["Parker"], saturday.Parties.Select(p => p.LeadName));

        Assert.Contains("confirmed", Refused(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(WaitingId, TableOneId), default)));
        Assert.Contains("confirmed", Refused(await Dining(sqlite).Seat(OrgId, EventId, SaturdayDinnerId, new(FourId, TableOneId), default)));
    }

    [Fact]
    public async Task A_table_never_seats_more_than_its_chairs_and_says_how_many_are_left()
    {
        await using var sqlite = await SeedAsync();

        var seated = Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(SixId, TableOneId), default));
        Assert.Equal(6, seated.Seats.Single().People);

        var refusal = Refused(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(FourId, TableOneId), default));
        Assert.Contains("Table 1 seats 8 and has 2 chairs left", refusal);
        Assert.Contains("Nguyen's party needs 4", refusal);

        // Split: two here, two at the next table.
        Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(FourId, TableOneId, 2), default));
        var split = Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(FourId, TableTwoId), default));
        Assert.Equal(2, split.Seats.Single(s => s.TableId == TableTwoId).People);
        Assert.Equal(4, split.Parties.Single(p => p.BookingId == FourId).Seated);

        // And nobody is seated twice over.
        Assert.Contains("already seated", Refused(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(FourId, TableTwoId), default)));
    }

    [Fact]
    public async Task A_party_that_cancels_drops_off_its_table()
    {
        await using var sqlite = await SeedAsync();
        Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(CancelledId, TableTwoId), default));

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.SingleAsync(b => b.Id == CancelledId);
            booking.Status = HostedEventBookingStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var after = Ok(await Dining(sqlite).Get(OrgId, EventId, FridayDinnerId, default));
        Assert.Empty(after.Seats);
        Assert.DoesNotContain(after.Parties, p => p.BookingId == CancelledId);
    }

    [Fact]
    public async Task Seating_like_another_sitting_brings_back_only_the_people_who_are_there()
    {
        await using var sqlite = await SeedAsync();
        Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(SixId, TableTwoId), default));
        Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(FourId, TableOneId), default));

        var saturday = Ok(await Dining(sqlite).SameAs(OrgId, EventId, SaturdayDinnerId, FridayDinnerId, default));
        var seat = Assert.Single(saturday.Seats);
        Assert.Equal(SixId, seat.BookingId);
        Assert.Equal(TableTwoId, seat.TableId);
        Assert.Contains("6 people", saturday.Sentence);
    }

    [Fact]
    public async Task A_table_cannot_shrink_under_the_people_at_it_and_removing_it_unseats_them()
    {
        await using var sqlite = await SeedAsync();
        Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(SixId, TableOneId), default));

        var refusal = Refused(await Dining(sqlite).SetTables(OrgId, EventId, FridayDinnerId,
            new([new("Table 1", 4, TableOneId), new("Table 2", 8, TableTwoId)]), default));
        Assert.Contains("Table 1 has 6 people seated", refusal);

        var after = Ok(await Dining(sqlite).SetTables(OrgId, EventId, FridayDinnerId, new([new("Table 2", 8, TableTwoId)]), default));
        Assert.Single(after.Tables);
        Assert.Empty(after.Seats);
    }

    [Fact]
    public async Task Editing_the_menu_keeps_the_seating_for_the_sittings_still_there()
    {
        await using var sqlite = await SeedAsync();
        Ok(await Dining(sqlite).Seat(OrgId, EventId, FridayDinnerId, new(SixId, TableOneId), default));

        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var menus = new HostedEventMenuController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object, new HostedEventAccess(security.Object))
        {
            ControllerContext = Dining(sqlite).ControllerContext,
        };

        Assert.IsType<OkObjectResult>((await menus.Set(OrgId, EventId, new SetHostedEventMenusRequest(
        [
            new HostedEventMenuInput(FridayId, "Dinner — now with pudding", Items: [new("Pie")], Id: FridayDinnerId),
        ]), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(1, await db.HostedEventDiningSeats.CountAsync(s => s.HostedEventMenuId == FridayDinnerId));
        // Saturday's dinner was not sent back, so it has gone.
        Assert.False(await db.HostedEventMenus.AnyAsync(m => m.Id == SaturdayDinnerId));
    }
}
