using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Mail;
using Ben.Service.Models.Entities;
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
/// A guest's request for a place, and the letter saying "nothing is held yet", commit together or
/// not at all (item 239b).
/// </summary>
/// <remarks>
/// <para><b>What the queue's own tests cannot show.</b> <c>ALetterCommitsWithWhatItIsAboutTests</c>
/// proves <c>EnqueueAsync</c> adds without saving. That is necessary and nowhere near sufficient:
/// the controller still has to open the transaction, save twice inside it, and let a failure reach
/// it. Drop the transaction and every queue test stays green while the first save commits the
/// request on its own — which is the bug, back, with nothing red to say so.</para>
///
/// <para><b>Why the failing case matters more.</b> A request with no letter is a guest who assumes
/// silence means yes and turns up with a suitcase. So the second test makes queueing fail after
/// the request has been written, and requires that the request is not there afterwards.</para>
///
/// <para>SQLite rather than InMemory, because InMemory has no transactions: the rollback below
/// would not happen and the test would pass against the bug it exists to catch.</para>
/// </remarks>
public sealed class ABookingRequestCommitsWithItsLetterTests
{
    private static readonly Guid HostId  = Guid.Parse("6b1d0000-0000-0000-0000-000000000001");
    private static readonly Guid GuestId = Guid.Parse("6b1d0000-0000-0000-0000-000000000002");
    private static readonly Guid OrgId   = Guid.Parse("6b1d0000-0000-0000-0000-000000000003");
    private static readonly Guid PlaceId = Guid.Parse("6b1d0000-0000-0000-0000-000000000004");
    private static readonly Guid EventId = Guid.Parse("6b1d0000-0000-0000-0000-000000000005");

    private static readonly string GuestEmail = $"{GuestId:N}@example.com";

    private static readonly RequestHostedEventBookingRequest ADayPass =
        new(HostedEventBookingKind.DayPass, PartySize: 2,
            FirstName: "A", LastName: "Guest", Phone: "555-0100");

    /// <summary>The ordinary case: one request, one letter, both there afterwards.</summary>
    [Fact]
    public async Task A_request_that_succeeds_leaves_the_request_and_its_letter()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        var result = await Guest(sqlite).RequestAPlace(
            EventId, ADayPass, Mailer(Outbox(sqlite.Factory)), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(1, await db.HostedEventBookings.CountAsync(b => b.HostedEventId == EventId));

        var letter = Assert.Single(await db.OutboxEmails.ToListAsync());
        Assert.Equal(GuestEmail, letter.To);
        Assert.Equal(MailKinds.BookingAsked.Key, letter.Kind);
    }

    /// <summary>
    /// And when the letter cannot be queued, the request is not left behind without it.
    /// </summary>
    /// <remarks>
    /// The queue throws AFTER the request's first save, which is the exact window the transaction
    /// closes. Without it that save has already committed, and this finds one booking.
    /// </remarks>
    [Fact]
    public async Task A_letter_that_cannot_be_queued_takes_the_request_with_it()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        var broken = new Mock<IOutboxEmailQueue>();
        broken.Setup(q => q.EnqueueAsync(
                  It.IsAny<BenDataContext>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("the outbox is unreachable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Guest(sqlite).RequestAPlace(
            EventId, ADayPass, Mailer(broken.Object), default));

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(0, await db.HostedEventBookings.CountAsync(b => b.HostedEventId == EventId));
        Assert.Equal(0, await db.OutboxEmails.CountAsync());
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static PublicHostedEventBookingController Guest(SqliteTestDb sqlite)
        => new(sqlite.Factory, new HostedEventCalendarSync())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, GuestId.ToString())], "Bearer")),
                },
            },
        };

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

    /// <summary>
    /// The mailer, told mail is configured — SendAskedAsync writes nothing when it is not, and a
    /// test that queues nothing proves nothing about queueing.
    /// </summary>
    private static EventGuestMailer Mailer(IOutboxEmailQueue queue)
    {
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        return new EventGuestMailer(
            email.Object,
            Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" }),
            NullLogger<EventGuestMailer>.Instance,
            queue);
    }

    private static async Task SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        foreach (var (id, name) in new[] { (HostId, "The Host"), (GuestId, "A Guest") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.com", UserName = $"{id:N}@example.com",
                DisplayName = name, DateCreated = DateTime.UtcNow,
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house-239b",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in-239b",
            StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
            LifecycleState = HostedEventLifecycleState.Published, DayPassCapacity = 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        await db.SaveChangesAsync();
    }
}
