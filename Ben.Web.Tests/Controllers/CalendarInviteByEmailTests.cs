using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Inviting someone outside the group, by email address.
/// </summary>
/// <remarks>
/// The rule these all orbit: only an address its owner actually published can be resolved. The
/// sign-in address on AppUser is private by design, so matching against it would turn this into a
/// way of confirming somebody's private login from outside.
/// </remarks>
public class CalendarInviteByEmailTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrgCalendarEventAttendeeRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is OrgCalendarEventAttendee a
            ? new OrgCalendarEventAttendeeRecord { Id = a.Id, AppUserId = a.AppUserId }
            : new OrgCalendarEventAttendeeRecord());
        return m.Object;
    }

    private static OrgCalendarEventController Build(IDbContextFactory<BenDataContext> f)
        => new(f, Mapper())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, MemberId.ToString())], "Bearer"))
                }
            }
        };

    /// <param name="isPublic">Whether the outsider published the address.</param>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(
        Guid outsiderId, string address, bool isPublic = true, bool isHidden = false, bool isValidated = true)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
            Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = EventId, OrganizationId = OrgId, Title = "Team briefing",
            StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow.AddHours(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
        });
        // The owning account has to exist: a UserEmail without one cannot occur in the real
        // schema, and the endpoint reloads the attendee through its AppUser navigation.
        db.Users.Add(new AppUser
        { Id = outsiderId, UserName = address, Email = address, DisplayName = "A Guest" });
        db.UserEmails.Add(new UserEmail
        {
            Id = Guid.NewGuid(), AppUserId = outsiderId, UserEmailTypeId = Guid.NewGuid(),
            EmailAddress = address, IsPublic = isPublic, IsHidden = isHidden, IsValidated = isValidated,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = outsiderId,
        });
        await db.SaveChangesAsync();
        return factory;
    }

    [Fact]
    public async Task A_published_address_resolves_and_is_invited()
    {
        var outsider = Guid.NewGuid();
        var factory = await SeedAsync(outsider, "guest@example.com");

        var result = await Build(factory)
            .AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("guest@example.com"), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var attendee = await db.OrgCalendarEventAttendees.SingleAsync();
        Assert.Equal(outsider, attendee.AppUserId);
        Assert.Equal(RsvpStatus.Invited, attendee.RsvpStatus);
    }

    [Fact]
    public async Task Matching_ignores_case()
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com");

        var result = await Build(factory)
            .AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("  GUEST@Example.COM  "), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Theory]
    // Not published at all.
    [InlineData(false, false, true)]
    // Published then hidden.
    [InlineData(true, true, true)]
    // Published but never validated — the address may not even belong to them.
    [InlineData(true, false, false)]
    public async Task An_address_that_is_not_genuinely_published_does_not_resolve(
        bool isPublic, bool isHidden, bool isValidated)
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com", isPublic, isHidden, isValidated);

        var result = await Build(factory)
            .AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("guest@example.com"), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.OrgCalendarEventAttendees.ToListAsync());
    }

    [Fact]
    public async Task The_private_sign_in_address_is_not_searchable()
    {
        // The whole point. An account exists with this as its login, and no published address.
        var factory = TestDbFactory.Create();
        var outsider = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
                Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow,
            });
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = EventId, OrganizationId = OrgId, Title = "Team briefing",
                StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow.AddHours(1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            db.Users.Add(new AppUser
            { Id = outsider, UserName = "private@example.com", Email = "private@example.com" });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory)
            .AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("private@example.com"), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Inviting_the_same_person_twice_is_refused()
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com");
        var ctrl = Build(factory);

        await ctrl.AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("guest@example.com"), default);
        var second = await ctrl.AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("guest@example.com"), default);

        Assert.IsType<BadRequestObjectResult>(second.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.OrgCalendarEventAttendees.ToListAsync());
    }

    [Fact]
    public async Task A_non_member_cannot_invite_anyone()
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com");

        var ctrl = new OrgCalendarEventController(factory, Mapper())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"))
                }
            }
        };

        var result = await ctrl.AddAttendeeByEmail(
            OrgId, EventId, new AddAttendeeByEmailRequest("guest@example.com"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_address_is_refused(string? address)
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com");

        var result = await Build(factory)
            .AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest(address), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
