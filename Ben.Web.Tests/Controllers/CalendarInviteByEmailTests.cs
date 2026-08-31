using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    /// <summary>Email off: these suites are about permission and data, never about delivery.</summary>
    private static Ben.Data.Common.Interfaces.IEmailService UnconfiguredEmail()
    {
        var m = new Moq.Mock<Ben.Data.Common.Interfaces.IEmailService>();
        m.SetupGet(x => x.IsConfigured).Returns(false);
        return m.Object;
    }

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
        => new(f, Mapper(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f), UnconfiguredEmail(),
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrgCalendarEventController>.Instance,
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer())
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

        var ctrl = new OrgCalendarEventController(factory, Mapper(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory), UnconfiguredEmail(),
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrgCalendarEventController>.Instance,
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer())
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

    /// <summary>
    /// The walk-up: somebody with no account at all gets a sign-up link, which the account-based
    /// paths could never give them.
    /// </summary>
    /// <remarks>
    /// This is the gap Ben pointed at. <c>AddAttendeeByEmail</c> answers NotFound for an address
    /// no account publishes — correctly, since it resolves existing accounts — and that is exactly
    /// the person standing on the pavement handing the guide cash.
    /// </remarks>
    [Fact]
    public async Task A_walk_up_with_no_account_can_be_sent_a_link()
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com");

        // The account-based path cannot help this person.
        var byEmail = await Build(factory).AddAttendeeByEmail(
            OrgId, EventId, new AddAttendeeByEmailRequest("nobody@example.com"), default);
        Assert.IsType<NotFoundObjectResult>(byEmail.Result);

        // The guest-invite path can.
        var invited = await Build(factory).InviteGuest(
            OrgId, EventId, new InviteGuestRequest("nobody@example.com", "Walk Up"), default);
        Assert.IsType<OkObjectResult>(invited.Result);

        await using var db = await factory.CreateDbContextAsync();
        var invite = await db.EventAttendanceInvites.SingleAsync(i => i.Email == "nobody@example.com");
        Assert.Equal(MemberId, invite.InvitedByAppUserId);
        Assert.False(string.IsNullOrWhiteSpace(invite.Token));
    }

    /// <summary>
    /// Inviting a guest is editing the calendar, so it answers to the calendar permission — the
    /// grant a tour operator gives a hired guide.
    /// </summary>
    [Fact]
    public async Task A_non_member_cannot_send_a_guest_a_link()
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com");

        var ctrl = new OrgCalendarEventController(
            factory, Mapper(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory),
            UnconfiguredEmail(),
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrgCalendarEventController>.Instance,
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer())
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

        var result = await ctrl.InviteGuest(
            OrgId, EventId, new InviteGuestRequest("nobody@example.com", null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    /// <summary>
    /// It answers identically whether or not that address already has an account.
    /// </summary>
    /// <remarks>
    /// A guide is a hired stranger, not an administrator. The public flow is careful not to become
    /// an account-existence oracle, and this endpoint must not undo that from the inside — so the
    /// response to a known address and an unknown one is the same.
    /// </remarks>
    [Fact]
    public async Task It_does_not_reveal_whether_an_address_has_an_account()
    {
        var factory = await SeedAsync(Guid.NewGuid(), "guest@example.com");

        var known   = await Build(factory).InviteGuest(
            OrgId, EventId, new InviteGuestRequest("guest@example.com", null), default);
        var unknown = await Build(factory).InviteGuest(
            OrgId, EventId, new InviteGuestRequest("stranger@example.com", null), default);

        Assert.IsType<OkObjectResult>(known.Result);
        Assert.IsType<OkObjectResult>(unknown.Result);
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

    /// <summary>
    /// The whole self-service chain, end to end: add an address, confirm it, publish it, and only
    /// then can somebody invite you by it.
    /// </summary>
    /// <remarks>
    /// This endpoint shipped before there was any way for a person to publish an address at all —
    /// it had never once matched a real row, because every UserEmail in the database was created by
    /// the admin surface with validation hardcoded off. This test is what proves that gap closed,
    /// and it fails at a different step for each half that regresses.
    /// </remarks>
    [Fact]
    public async Task An_address_published_through_the_self_service_flow_becomes_invitable()
    {
        var outsider = Guid.NewGuid();
        var emailTypeId = Guid.NewGuid();
        var factory = TestDbFactory.Create();

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
            { Id = outsider, UserName = "self@example.com", Email = "private-login@example.com", DisplayName = "A Guest" });
            db.UserEmailTypes.Add(new UserEmailType
            { Id = emailTypeId, Name = "Personal", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var contact = new MyContactInfoController(factory, new Mock<IAuditLogService>().Object, Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, outsider.ToString())], "Bearer"))
                }
            }
        };

        // 1. Add the address. It starts private and unvalidated, so an invite must not find it yet.
        var created = Assert.IsType<MyEmailRecord>(Assert.IsType<OkObjectResult>(
            (await contact.CreateEmail(new UpsertMyEmailRequest(emailTypeId, "self@example.com", false, false), default)).Result).Value);

        Assert.IsType<NotFoundObjectResult>((await Build(factory)
            .AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("self@example.com"), default)).Result);

        // 2. Issue a link.
        var mail = new Mock<IEmailService>();
        mail.SetupGet(x => x.IsConfigured).Returns(false);
        var config = new ConfigurationBuilder().Build();

        Assert.IsType<OkObjectResult>((await contact.SendValidation(created.Id, mail.Object, config, default)).Result);

        string token;
        await using (var db = await factory.CreateDbContextAsync())
            token = (await db.UserEmails.FirstAsync(e => e.Id == created.Id)).ValidationToken!;

        // 3. Redeem it as an anonymous visitor would.
        var redeem = new PublicEmailValidationController(factory, new Mock<IAuditLogService>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.IsType<NoContentResult>(await redeem.Confirm(token, default));

        // 4. Publish it — only possible now that it is validated.
        var published = Assert.IsType<MyEmailRecord>(Assert.IsType<OkObjectResult>(
            (await contact.UpdateEmail(created.Id,
                new UpsertMyEmailRequest(emailTypeId, "self@example.com", false, IsPublic: true), default)).Result).Value);
        Assert.True(published.IsPublic);
        Assert.True(published.IsValidated);

        // 5. Now the invite resolves.
        Assert.IsType<OkObjectResult>((await Build(factory)
            .AddAttendeeByEmail(OrgId, EventId, new AddAttendeeByEmailRequest("self@example.com"), default)).Result);

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(outsider, (await check.OrgCalendarEventAttendees.SingleAsync()).AppUserId);
    }
}
