using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// An invitation's token and the letter carrying its link commit together, or neither does (item
/// 239b, the invitation letters).
/// </summary>
/// <remarks>
/// <para><b>Why these letters.</b> Every one of them carries a link to a token, and four of the
/// five ROTATE that token when somebody is invited again — which kills the link in the letter
/// they already have. They all used to save the token first and write the letter after, through
/// an outbox that swallows its own failures. So a letter that failed to write left somebody with a
/// dead link and nothing to replace it, and the endpoints that report "sent" reported it anyway.
/// </para>
///
/// <para><b>The failure injected</b> is <see cref="RefuseOutboxWrites"/>: the outbox table taking
/// nothing while everything else works, with the outbox wired exactly as production wires it (see
/// <see cref="TestOutbox"/>). Old design: the token commits, the letter's own save is refused and
/// swallowed. New design: they are one write, so both are refused. Each test asserts the harm
/// that the old design did — the dead link, the false "sent", the invite with no letter.</para>
/// </remarks>
public sealed class AnInvitationCommitsWithItsLetterTests
{
    private static readonly Guid HostId  = Guid.Parse("6b1f0000-0000-0000-0000-000000000001");
    private static readonly Guid OrgId   = Guid.Parse("6b1f0000-0000-0000-0000-000000000002");
    private static readonly Guid PlaceId = Guid.Parse("6b1f0000-0000-0000-0000-000000000003");
    private static readonly Guid EventId = Guid.Parse("6b1f0000-0000-0000-0000-000000000004");
    private static readonly Guid HostedId = Guid.Parse("6b1f0000-0000-0000-0000-000000000005");

    private const string Guest = "guest@example.test";
    private const string OldToken = "the-link-in-the-letter-they-already-have";

    // ── the public "confirm you're coming" ──────────────────────────────────

    /// <summary>
    /// Asking again, when the new letter cannot be written, leaves the link they already have
    /// working.
    /// </summary>
    [Fact]
    public async Task Asking_to_come_again_when_the_letter_cannot_be_written_leaves_the_old_link_working()
    {
        var refuse = new RefuseOutboxWrites();
        await using var sqlite = await SqliteTestDb.CreateAsync(refuse);
        await SeedPublicEventAsync(sqlite);
        await SeedAttendanceInviteAsync(sqlite);
        var outbox = TestOutbox.Real(sqlite.Factory);

        var ctrl = new PublicEventAttendanceController(
            sqlite.Factory, outbox, users: null!,
            Options.Create(new SiteIdentity { BaseUrl = "https://test.local" }),
            NullLogger<PublicEventAttendanceController>.Instance,
            new UserHandleService(sqlite.Factory), Support.SilentTourMail.Instance, outbox);

        refuse.Refusing = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctrl.RequestAttendance(
            EventId, new RequestEventAttendanceRequest(Guest, "A Guest"), default));

        await AssertTheOldLinkStillWorksAsync(sqlite);
    }

    // ── a guide signs somebody up ───────────────────────────────────────────

    /// <summary>The same, when it is a guide signing somebody up again.</summary>
    [Fact]
    public async Task A_guide_signing_somebody_up_again_when_the_letter_cannot_be_written_leaves_the_old_link_working()
    {
        var refuse = new RefuseOutboxWrites();
        await using var sqlite = await SqliteTestDb.CreateAsync(refuse);
        await SeedPublicEventAsync(sqlite);
        await SeedAttendanceInviteAsync(sqlite);
        var outbox = TestOutbox.Real(sqlite.Factory);

        var ctrl = new OrgCalendarEventController(
            sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, TheHostMayDoAnything().Object, outbox,
            Options.Create(new SiteIdentity { BaseUrl = "https://test.local" }),
            NullLogger<OrgCalendarEventController>.Instance,
            new CmsMarkupSanitizer(), Support.SilentTourMail.Instance, outbox)
        { ControllerContext = SignedInAs(HostId) };

        refuse.Refusing = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctrl.InviteGuest(
            OrgId, EventId, new InviteGuestRequest(Guest, "A Guest"), default));

        await AssertTheOldLinkStillWorksAsync(sqlite);
    }

    // ── a venue invites a guest ─────────────────────────────────────────────

    /// <summary>
    /// A venue's invitation that cannot be written is not reported to the host as sent — and the
    /// guest's first link keeps working.
    /// </summary>
    [Fact]
    public async Task A_venue_invitation_that_cannot_be_written_is_not_reported_as_sent()
    {
        var refuse = new RefuseOutboxWrites();
        await using var sqlite = await SqliteTestDb.CreateAsync(refuse);
        await SeedHostedEventAsync(sqlite);
        var outbox = TestOutbox.Real(sqlite.Factory);
        var board = Board(sqlite, outbox);

        // The first invitation goes normally: one letter, and the host is told so.
        var first = await board.InviteByEmail(OrgId, HostedId, new InviteHostedEventGuestRequest(Guest), default);
        Assert.True(Assert.IsType<HostedEventGuestInviteRecord>(Assert.IsType<OkObjectResult>(first.Result).Value).Sent);

        string firstToken;
        await using (var db = await sqlite.NewContextAsync())
        {
            firstToken = (await db.EventAttendanceInvites.SingleAsync(i => i.Email == Guest)).Token!;
            Assert.Equal(1, await db.OutboxEmails.CountAsync());
        }

        // Inviting again with the outbox refusing: the old design rotated the token, lost the
        // letter and answered Sent = true.
        refuse.Refusing = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => board.InviteByEmail(
            OrgId, HostedId, new InviteHostedEventGuestRequest(Guest), default));

        await using var read = await sqlite.NewContextAsync();
        Assert.Equal(firstToken, (await read.EventAttendanceInvites.SingleAsync(i => i.Email == Guest)).Token);
        Assert.Equal(1, await read.OutboxEmails.CountAsync());
    }

    // ── asking somebody to help ─────────────────────────────────────────────

    /// <summary>
    /// Resending a staff invitation that cannot be written leaves the link they already have
    /// working — the reissue is rolled back with the letter.
    /// </summary>
    [Fact]
    public async Task Resending_a_staff_invitation_that_cannot_be_written_leaves_the_old_link_working()
    {
        var refuse = new RefuseOutboxWrites();
        await using var sqlite = await SqliteTestDb.CreateAsync(refuse);
        await SeedHostedEventAsync(sqlite);

        var staffId = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventStaff.Add(new HostedEventStaff
            {
                Id = staffId, HostedEventId = HostedId, Email = Guest, DisplayName = "A Steward",
                RunsTheDoor = true, Token = OldToken, DateExpires = DateTime.UtcNow.AddDays(14),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });
            await db.SaveChangesAsync();
        }

        var security = TheHostMayDoAnything();
        var ctrl = new HostedEventStaffController(
            sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object), Mailer(TestOutbox.Real(sqlite.Factory)))
        { ControllerContext = SignedInAs(HostId) };

        refuse.Refusing = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctrl.Resend(OrgId, HostedId, staffId, default));

        await using var read = await sqlite.NewContextAsync();
        Assert.Equal(OldToken, (await read.HostedEventStaff.SingleAsync(s => s.Id == staffId)).Token);
        Assert.Equal(0, await read.OutboxEmails.CountAsync());
    }

    // ── a client invites somebody to their case ────────────────────────────

    /// <summary>
    /// A case invitation whose letter cannot be written leaves no invite behind claiming it was
    /// emailed.
    /// </summary>
    /// <remarks>
    /// The screen falls back to showing the link to copy when EmailSent is false, so the old
    /// design's "true" for a letter that never existed was the one answer that hid the fallback.
    /// </remarks>
    [Fact]
    public async Task A_case_invitation_that_cannot_be_written_leaves_no_invite_behind()
    {
        var refuse = new RefuseOutboxWrites();
        await using var sqlite = await SqliteTestDb.CreateAsync(refuse);
        var (caseId, clientId) = await SeedClientCaseAsync(sqlite);
        var outbox = TestOutbox.Real(sqlite.Factory);

        var storage = new Mock<IFileStorageService>();
        var ctrl = new MyCaseController(
            sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, storage.Object, new FileMetadataExtractorService(),
            new Mock<IAuditLogService>().Object, outbox, new ConfigurationBuilder().Build(),
            NullLogger<MyCaseController>.Instance, Options.Create(new SiteIdentity()),
            new PlatformMessageService(sqlite.Factory), TestMedia.Ingest(),
            new CmsMarkupSanitizer(), Ben.Data.WebApi.Services.LinkPreviews.LinkPreviewWarmer.None, outbox)
        { ControllerContext = SignedInAs(clientId) };

        refuse.Refusing = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctrl.InviteCoClient(
            caseId, new InviteCoClientRequest(Guest), default));

        await using var read = await sqlite.NewContextAsync();
        Assert.Equal(0, await read.CaseClientInvites.CountAsync());
        Assert.Equal(0, await read.OutboxEmails.CountAsync());
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static async Task AssertTheOldLinkStillWorksAsync(SqliteTestDb sqlite)
    {
        await using var read = await sqlite.NewContextAsync();
        var invite = await read.EventAttendanceInvites.SingleAsync(i => i.Email == Guest);
        Assert.Equal(OldToken, invite.Token);
        Assert.Equal(0, await read.OutboxEmails.CountAsync());
    }

    private static ControllerContext SignedInAs(Guid who) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, who.ToString())], "Bearer")),
        },
    };

    private static Mock<IOrganizationSecurityService> TheHostMayDoAnything()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _)
                => user == HostId);
        return security;
    }

    private static EventGuestMailer Mailer(OutboxEmailService outbox)
        => new(outbox, Options.Create(new SiteIdentity { Name = "IsHaunted", BaseUrl = "https://test.local" }),
               NullLogger<EventGuestMailer>.Instance, outbox);

    private static HostedEventBookingController Board(SqliteTestDb sqlite, OutboxEmailService outbox)
    {
        var security = TheHostMayDoAnything();
        return new HostedEventBookingController(
            sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventCalendarSync(), new HostedEventAccess(security.Object), Mailer(outbox), outbox,
            Options.Create(new SiteIdentity { Name = "IsHaunted", BaseUrl = "https://test.local" }),
            NullLogger<HostedEventBookingController>.Instance, outbox)
        { ControllerContext = SignedInAs(HostId) };
    }

    private static async Task SeedPeopleAndGroupAsync(BenDataContext db)
    {
        db.Users.Add(new AppUser
        {
            Id = HostId, Email = "host@example.test", UserName = "host@example.test",
            DisplayName = "The Host", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house-invitations",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        await Task.CompletedTask;
    }

    /// <summary>A public walk nobody has confirmed for yet.</summary>
    private static async Task SeedPublicEventAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        await SeedPeopleAndGroupAsync(db);
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = EventId, OrganizationId = OrgId, Title = "Ghost Walk", UrlName = "ghost-walk-invitations",
            StartDateTime = DateTime.UtcNow.AddDays(7), EndDateTime = DateTime.UtcNow.AddDays(7).AddHours(3),
            IsPublic = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Somebody who has already asked, and holds a link to <see cref="OldToken"/>.</summary>
    private static async Task SeedAttendanceInviteAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        db.EventAttendanceInvites.Add(new EventAttendanceInvite
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = EventId, Email = Guest, DisplayName = "A Guest",
            Token = OldToken, DateExpires = DateTime.UtcNow.AddDays(14),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A published weekend a venue can invite guests to.</summary>
    private static async Task SeedHostedEventAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        await SeedPeopleAndGroupAsync(db);
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = HostedId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in-invitations",
            StartsOn = DateTime.UtcNow.Date.AddDays(30), EndsOn = DateTime.UtcNow.Date.AddDays(30),
            LifecycleState = HostedEventLifecycleState.Published, DayPassCapacity = 10,
            TimeZoneId = "America/Chicago",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A client with a case of their own, as MyCaseControllerTests seeds one.</summary>
    private static async Task<(Guid CaseId, Guid ClientId)> SeedClientCaseAsync(SqliteTestDb sqlite)
    {
        var clientId = Guid.NewGuid();
        var caseId   = Guid.NewGuid();
        await using var db = await sqlite.NewContextAsync();
        await SeedPeopleAndGroupAsync(db);
        db.Users.Add(new AppUser
        {
            Id = clientId, UserName = "client@example.test", Email = "client@example.test",
            DisplayName = "The Client", DateCreated = DateTime.UtcNow,
        });
        var request = new ClientRequest
        {
            Id = Guid.NewGuid(), AppUserId = clientId, City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", StreetAddress1 = "1 Main", Description = "Desc", Status = ClientRequestStatus.Assigned,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        };
        db.ClientRequests.Add(request);
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = OrgId, ClientRequestId = request.Id,
            Title = "Test Case", CaseYear = DateTime.UtcNow.Year, OrgCaseNumber = 1, Status = CaseStatus.Accepted,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();
        return (caseId, clientId);
    }
}
