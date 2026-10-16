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
/// A host marking an invited guest as coming (10/16/2026).
/// </summary>
/// <remarks>
/// The RSVP endpoint always allowed "the attendee themselves or an org admin", and no screen ever
/// called it — so an invited member of a private event could never become "coming", which is
/// what reminders and the head count read. The calendar's editor now calls it; these pin the rule
/// it calls into, including the group in the address, which the endpoint used to ignore.
/// </remarks>
public class CalendarHostRsvpTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherOwnerId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid BystanderId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid GuestRow = Guid.NewGuid();

    private static Ben.Data.Common.Interfaces.IEmailService UnconfiguredEmail()
    {
        var m = new Mock<Ben.Data.Common.Interfaces.IEmailService>();
        m.SetupGet(x => x.IsConfigured).Returns(false);
        return m.Object;
    }

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrgCalendarEventAttendeeRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is OrgCalendarEventAttendee a
            ? new OrgCalendarEventAttendeeRecord { Id = a.Id, AppUserId = a.AppUserId, RsvpStatus = a.RsvpStatus }
            : new OrgCalendarEventAttendeeRecord());
        return m.Object;
    }

    private static OrgCalendarEventController Build(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, Mapper(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f), UnconfiguredEmail(),
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrgCalendarEventController>.Instance,
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer(),
            Support.SilentTourMail.Instance, new ForwardingOutboxQueue(UnconfiguredEmail()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = OtherOrgId, Name = "Elsewhere", UrlName = "elsewhere", DateCreated = DateTime.UtcNow });

        foreach (var (id, name) in new[] { (OwnerId, "Owner"), (OtherOwnerId, "Other Owner"), (GuestId, "Guest"), (BystanderId, "Bystander") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        void Member(Guid id, Guid orgId, OrganizationMemberRole role)
            => db.OrganizationUserMemberships.Add(new OrganizationUserMembership
               {
                   Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = id,
                   Role = role, IsActive = true, DateCreated = DateTime.UtcNow,
               });

        Member(OwnerId, OrgId, OrganizationMemberRole.Owner);
        Member(GuestId, OrgId, OrganizationMemberRole.Member);
        Member(BystanderId, OrgId, OrganizationMemberRole.Member);
        // Owns a DIFFERENT group: admin rights there, none here.
        Member(OtherOwnerId, OtherOrgId, OrganizationMemberRole.Owner);

        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = EventId, OrganizationId = OrgId, Title = "Team briefing",
            StartDateTime = DateTime.UtcNow.AddDays(2), EndDateTime = DateTime.UtcNow.AddDays(2).AddHours(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
        {
            Id = GuestRow, OrgCalendarEventId = EventId, AppUserId = GuestId,
            RsvpStatus = RsvpStatus.Invited, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<RsvpStatus> AnswerAsync(IDbContextFactory<BenDataContext> f)
    {
        await using var db = await f.CreateDbContextAsync();
        return (await db.OrgCalendarEventAttendees.SingleAsync(a => a.Id == GuestRow)).RsvpStatus;
    }

    [Fact]
    public async Task The_groups_owner_can_mark_an_invited_member_as_coming_and_back()
    {
        var f = await SeedAsync();

        var coming = await Build(f, OwnerId).Rsvp(OrgId, EventId, GuestRow, new RsvpRequest(RsvpStatus.Accepted), default);
        Assert.IsType<OkObjectResult>(coming.Result);
        Assert.Equal(RsvpStatus.Accepted, await AnswerAsync(f));

        var back = await Build(f, OwnerId).Rsvp(OrgId, EventId, GuestRow, new RsvpRequest(RsvpStatus.Invited), default);
        Assert.IsType<OkObjectResult>(back.Result);
        Assert.Equal(RsvpStatus.Invited, await AnswerAsync(f));
    }

    [Fact]
    public async Task A_plain_member_cannot_answer_for_somebody_else()
    {
        var f = await SeedAsync();

        var result = await Build(f, BystanderId).Rsvp(OrgId, EventId, GuestRow, new RsvpRequest(RsvpStatus.Accepted), default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal(RsvpStatus.Invited, await AnswerAsync(f));
    }

    [Fact]
    public async Task Another_groups_owner_cannot_answer_by_naming_their_own_group()
    {
        var f = await SeedAsync();

        // Their own group in the address satisfies the admin check; the event and the guest are
        // somebody else's. The ids alone used to be enough.
        var result = await Build(f, OtherOwnerId).Rsvp(OtherOrgId, EventId, GuestRow, new RsvpRequest(RsvpStatus.Accepted), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(RsvpStatus.Invited, await AnswerAsync(f));
    }

    [Fact]
    public async Task Another_groups_owner_cannot_remove_a_guest_by_naming_their_own_group()
    {
        var f = await SeedAsync();

        var result = await Build(f, OtherOwnerId).RemoveAttendee(OtherOrgId, EventId, GuestRow, default);

        Assert.IsType<NotFoundResult>(result);
        await using var db = await f.CreateDbContextAsync();
        Assert.True(await db.OrgCalendarEventAttendees.AnyAsync(a => a.Id == GuestRow));
    }
}
