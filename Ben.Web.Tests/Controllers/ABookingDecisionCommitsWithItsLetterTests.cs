using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Mail;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A venue's decision and the letter carrying the pass commit together, or neither does (item
/// 239b, the confirmation letter).
/// </summary>
/// <remarks>
/// <para><b>The rule this replaced.</b> The decision used to be saved first and the letter sent
/// "after the save, and best effort", because a confirmed guest whose letter bounced is still a
/// confirmed guest. That was right while the letter was a call out to a mail system. It is now a
/// row in the same database, sent later by the outbox and retried there — so the only way it fails
/// to write is the database failing, and then the host is better told to try again than left
/// believing a guest has their pass.</para>
///
/// <para><b>And a resend that told the truth only on paper.</b> The "send the pass again" endpoint
/// promised a refusal when nothing was posted, but the send swallowed its own failures, so the pass
/// was stamped as emailed and the host told 200 for a letter that did not exist. The third test is
/// that promise, held.</para>
///
/// <para>SQLite, because InMemory has no transactions and the rollback below would not happen.</para>
/// </remarks>
public sealed class ABookingDecisionCommitsWithItsLetterTests
{
    private static readonly Guid HostId    = Guid.Parse("6b1e0000-0000-0000-0000-000000000001");
    private static readonly Guid GuestId   = Guid.Parse("6b1e0000-0000-0000-0000-000000000002");
    private static readonly Guid OrgId     = Guid.Parse("6b1e0000-0000-0000-0000-000000000003");
    private static readonly Guid PlaceId   = Guid.Parse("6b1e0000-0000-0000-0000-000000000004");
    private static readonly Guid EventId   = Guid.Parse("6b1e0000-0000-0000-0000-000000000005");
    private static readonly Guid BookingId = Guid.Parse("6b1e0000-0000-0000-0000-000000000006");

    private static readonly string GuestEmail = $"{GuestId:N}@example.com";

    /// <summary>The ordinary case: confirmed, a pass issued, and one letter carrying it.</summary>
    [Fact]
    public async Task A_confirmation_leaves_the_decision_the_pass_and_the_letter_carrying_it()
    {
        await using var sqlite = await SeedAsync(HostedEventBookingStatus.Requested);

        var result = await Board(sqlite, Outbox(sqlite.Factory))
            .Confirm(OrgId, EventId, BookingId, new ConfirmHostedEventBookingRequest(), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(HostedEventBookingStatus.Confirmed,
            (await db.HostedEventBookings.SingleAsync(b => b.Id == BookingId)).Status);

        var pass = await db.HostedEventPasses.SingleAsync(p => p.HostedEventBookingId == BookingId);
        Assert.NotNull(pass.EmailedUtc);

        var letter = Assert.Single(await db.OutboxEmails.ToListAsync());
        Assert.Equal(GuestEmail, letter.To);
        Assert.Equal(MailKinds.BookingDecided.Key, letter.Kind);
    }

    /// <summary>
    /// When the letter cannot be queued, the booking is not confirmed without it.
    /// </summary>
    /// <remarks>
    /// The queue throws AFTER the decision's first save — the exact window the transaction closes.
    /// Without it that save has already committed: a confirmed booking and a live pass that
    /// nobody was ever sent.
    /// </remarks>
    [Fact]
    public async Task A_letter_that_cannot_be_queued_leaves_the_booking_undecided()
    {
        await using var sqlite = await SeedAsync(HostedEventBookingStatus.Requested);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Board(sqlite, Unreachable())
            .Confirm(OrgId, EventId, BookingId, new ConfirmHostedEventBookingRequest(), default));

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(HostedEventBookingStatus.Requested,
            (await db.HostedEventBookings.SingleAsync(b => b.Id == BookingId)).Status);
        Assert.Equal(0, await db.HostedEventPasses.CountAsync());
        Assert.Equal(0, await db.OutboxEmails.CountAsync());
    }

    /// <summary>
    /// Sending the pass again, when the letter cannot be queued, says so — and does not mark the
    /// pass as sent.
    /// </summary>
    [Fact]
    public async Task Sending_the_pass_again_that_cannot_be_queued_says_so_and_stamps_nothing()
    {
        await using var sqlite = await SeedAsync(HostedEventBookingStatus.Confirmed);
        await using (var db = await sqlite.NewContextAsync())
        {
            await EventPasses.EnsureAsync(
                db, await db.HostedEventBookings.SingleAsync(b => b.Id == BookingId), HostId, default);
            await db.SaveChangesAsync();
        }

        var result = await Board(sqlite, Unreachable()).EmailPass(OrgId, EventId, BookingId, default);

        Assert.IsType<ConflictObjectResult>(result.Result);

        await using var read = await sqlite.NewContextAsync();
        Assert.Null((await read.HostedEventPasses.SingleAsync()).EmailedUtc);
        Assert.Equal(0, await read.OutboxEmails.CountAsync());
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>A queue that cannot be written to.</summary>
    private static IOutboxEmailQueue Unreachable()
    {
        var queue = new Mock<IOutboxEmailQueue>();
        queue.Setup(q => q.EnqueueAsync(
                 It.IsAny<BenDataContext>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("the outbox is unreachable"));
        return queue.Object;
    }

    /// <summary>The real outbox, so the letter lands in the real table.</summary>
    private static OutboxEmailService Outbox(IDbContextFactory<BenDataContext> factory)
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com" });
        return new OutboxEmailService(
            factory, sender: null!, site,
            new MailComposer(factory, new MemoryCache(new MemoryCacheOptions()), site,
                             NullLogger<MailComposer>.Instance),
            NullLogger<OutboxEmailService>.Instance);
    }

    /// <summary>The host's board, with mail switched on and letters going to <paramref name="queue"/>.</summary>
    private static HostedEventBookingController Board(SqliteTestDb sqlite, IOutboxEmailQueue queue)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _)
                => user == HostId);

        // Configured, or the mailer writes nothing and the tests prove nothing about writing.
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);

        var site = Options.Create(new SiteIdentity { Name = "IsHaunted", BaseUrl = "https://test.local" });
        var mailer = new EventGuestMailer(email.Object, site, NullLogger<EventGuestMailer>.Instance, queue);

        return new HostedEventBookingController(
            sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventCalendarSync(), new HostedEventAccess(security.Object), mailer, email.Object,
            site, NullLogger<HostedEventBookingController>.Instance, new ForwardingOutboxQueue(email.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, HostId.ToString())], "Bearer")),
                },
            },
        };
    }

    /// <summary>A published event selling day passes, and one guest's booking for two.</summary>
    private static async Task<SqliteTestDb> SeedAsync(HostedEventBookingStatus status)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (HostId, "The Host"), (GuestId, "A Guest") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.com", UserName = $"{id:N}@example.com",
                DisplayName = name, DateCreated = now,
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house-decision",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in-decision",
            StartsOn = now.Date.AddDays(30), EndsOn = now.Date.AddDays(30),
            LifecycleState = HostedEventLifecycleState.Published, DayPassCapacity = 10,
            TimeZoneId = "America/Chicago",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventBookings.Add(new HostedEventBooking
        {
            Id = BookingId, HostedEventId = EventId, LeadAppUserId = GuestId,
            PartySize = 2, Kind = HostedEventBookingKind.DayPass, Status = status,
            DateCreated = now, CreatedByAppUserId = GuestId,
        });

        await db.SaveChangesAsync();
        return sqlite;
    }
}
