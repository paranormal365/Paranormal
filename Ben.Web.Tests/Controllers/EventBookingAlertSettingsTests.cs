using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// How often somebody hears about a group's bookings: listed only where they decide, and changed
/// only there (item 235 phase 8).
/// </summary>
public sealed class EventBookingAlertSettingsTests
{
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Steward = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid HotelId = Guid.NewGuid();
    private static readonly Guid TheatreId = Guid.NewGuid();

    private static EventBookingAlertSettingsController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        // The group lets the manager decide at the hotel and nobody decide anywhere else.
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(Manager, HotelId, OrganizationSecurityTable.EventBooking,
                OrganizationSecurityAction.Update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new EventBookingAlertSettingsController(factory, new HostedEventAccess(security.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };
    }

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.AddRange(
            new Organization { Id = HotelId, Name = "The Thomas House", UrlName = "thomas-house",
                               DateCreated = DateTime.UtcNow, CreatedByAppUserId = Manager },
            new Organization { Id = TheatreId, Name = "The Orpheum", UrlName = "orpheum",
                               DateCreated = DateTime.UtcNow, CreatedByAppUserId = Manager });

        foreach (var (user, org) in new[] { (Manager, HotelId), (Member, HotelId), (Manager, TheatreId) })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org, AppUserId = user, IsActive = true,
                Role = OrganizationMemberRole.Member, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });

        var eventId = Guid.NewGuid();
        db.HostedEvents.Add(new HostedEvent
        {
            Id = eventId, OrganizationId = TheatreId, PlaceId = Guid.NewGuid(),
            Name = "Phantom Nights", UrlName = "phantom-nights",
            StartsOn = DateTime.UtcNow.Date.AddDays(10), EndsOn = DateTime.UtcNow.Date.AddDays(12),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Manager,
        });
        db.HostedEventStaff.Add(new HostedEventStaff
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, AppUserId = Steward, Decides = true,
            DateConfirmed = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Manager,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    private static EventBookingAlertSettingsRecord Settings(ActionResult<EventBookingAlertSettingsRecord> result)
        => Assert.IsType<EventBookingAlertSettingsRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task Only_the_groups_somebody_decides_for_are_listed()
    {
        // The manager is in two groups and decides for one; a card offering to quieten the theatre's
        // letters would be a switch for letters the theatre never sends them.
        var factory = await SeedAsync();

        var groups = Settings(await Build(factory, Manager).Get(default)).Groups;

        var hotel = Assert.Single(groups);
        Assert.Equal(HotelId, hotel.OrganizationId);
        Assert.Equal(EventBookingAlertMode.AsItHappens, hotel.Mode);
    }

    [Fact]
    public async Task A_plain_member_has_nothing_to_set()
        => Assert.Empty(Settings(await Build(await SeedAsync(), Member).Get(default)).Groups);

    [Fact]
    public async Task A_helper_handed_the_deciding_sees_the_group_and_is_told_why()
    {
        var groups = Settings(await Build(await SeedAsync(), Steward).Get(default)).Groups;

        var theatre = Assert.Single(groups);
        Assert.Equal(TheatreId, theatre.OrganizationId);
        Assert.Contains("Phantom Nights", theatre.Why);
    }

    [Fact]
    public async Task The_choice_is_kept_and_changed_again_without_a_second_row()
    {
        var factory = await SeedAsync();

        Settings(await Build(factory, Manager).Set(HotelId, new(EventBookingAlertMode.DigestOnly), default));
        var after = Settings(await Build(factory, Manager).Set(HotelId, new(EventBookingAlertMode.Off), default));

        Assert.Equal(EventBookingAlertMode.Off, Assert.Single(after.Groups).Mode);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.EventBookingAlertPreferences.Where(p => p.AppUserId == Manager).ToListAsync());
    }

    [Fact]
    public async Task A_group_they_do_not_decide_for_is_refused_in_words()
    {
        var factory = await SeedAsync();

        var result = await Build(factory, Member).Set(HotelId, new(EventBookingAlertMode.Off), default);

        var refused = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Contains("don't decide bookings", Assert.IsType<string>(refused.Value));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.EventBookingAlertPreferences.ToListAsync());
    }
}
