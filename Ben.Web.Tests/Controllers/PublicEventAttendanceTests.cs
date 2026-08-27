using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Coming to a public event without an account (backlog item #87b).
/// </summary>
/// <remarks>
/// <para>Two things carry the weight. <b>An email typed into a box proves nothing</b> — only
/// clicking the link sent to it does, which is what stops a hidden address being handed to anybody
/// who guesses. And <b>asking always answers the same way</b>, whether or not the address already
/// has an account, or the endpoint becomes a way of testing which emails are registered here.</para>
/// </remarks>
public sealed class PublicEventAttendanceTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ExistingUserId = Guid.NewGuid();
    private const string ExistingEmail = "already@here.test";
    private const string StrangerEmail = "stranger@elsewhere.test";

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid EventId, Guid PrivateEventId);

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>
    /// A UserManager over the same in-memory store, so an account created by confirming is really
    /// there afterwards.
    /// </summary>
    private static UserManager<AppUser> UserManagerFor(IDbContextFactory<BenDataContext> factory)
    {
        var db = factory.CreateDbContext();
        var store = new UserStore<AppUser, IdentityRole<Guid>, BenDataContext, Guid>(db);

        return new UserManager<AppUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<AppUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<AppUser>>.Instance);
    }

    private static PublicEventAttendanceController Build(
        IDbContextFactory<BenDataContext> factory, IEmailService? email = null)
    {
        var mail = email ?? UnconfiguredEmail();

        return new PublicEventAttendanceController(
            factory, mail, UserManagerFor(factory),
            Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            NullLogger<PublicEventAttendanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };
    }

    private static IEmailService UnconfiguredEmail()
    {
        var m = new Mock<IEmailService>();
        m.SetupGet(x => x.IsConfigured).Returns(false);
        return m.Object;
    }

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        db.Users.Add(new AppUser
        {
            Id = ExistingUserId, Email = ExistingEmail, UserName = ExistingEmail,
            NormalizedEmail = ExistingEmail.ToUpperInvariant(),
            NormalizedUserName = ExistingEmail.ToUpperInvariant(),
            DisplayName = "Already Here", DateCreated = DateTime.UtcNow,
        });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Old Mill", Kind = PlaceKind.PublicLocation,
            City = "Nashville", State = "TN",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ExistingUserId,
        });

        Guid AddEvent(string title, bool isPublic)
        {
            var id = Guid.NewGuid();
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = id, OrganizationId = OrgId, Title = title,
                UrlName = isPublic ? "2026-08-24-ghost-walk" : null,
                StartDateTime = DateTime.UtcNow.AddDays(7),
                EndDateTime = DateTime.UtcNow.AddDays(7).AddHours(3),
                IsPublic = isPublic, PlaceId = placeId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ExistingUserId,
            });
            return id;
        }

        var publicId  = AddEvent("Ghost Walk", true);
        var privateId = AddEvent("Team Meeting", false);

        await db.SaveChangesAsync();
        return new World(factory, publicId, privateId);
    }

    /// <summary>
    /// A crowd may sign up; a mailer may not (item 199).
    /// </summary>
    /// <remarks>
    /// <para>The per-caller rate limit cannot separate these two: thirty guests on the venue's
    /// wifi and one attacker with an address list arrive from the same NAT'd address. So the
    /// per-caller limit is deliberately generous for this endpoint and the real bound is per
    /// event, which is what these exercise.</para>
    ///
    /// <para>The floor is what applies here, because the seeded event states no capacity — which
    /// is also the common case in production and therefore the one worth testing.</para>
    /// </remarks>
    [Fact]
    public async Task A_crowd_of_new_guests_is_not_refused()
    {
        var w = await SeedAsync();

        // Ninety guests: three sessions of thirty, the night Ben described.
        for (var i = 0; i < 90; i++)
        {
            var result = await Build(w.Factory).RequestAttendance(
                w.EventId, new RequestEventAttendanceRequest($"guest{i}@example.com", $"Guest {i}"), default);

            Assert.IsType<OkResult>(result);
        }
    }

    /// <summary>Past the ceiling, a new address is refused rather than mailed.</summary>
    [Fact]
    public async Task An_event_stops_issuing_invitations_once_it_is_being_used_as_a_mailer()
    {
        var w = await SeedAsync();

        for (var i = 0; i < PublicEventAttendanceController.InviteCeilingFloor; i++)
            await Build(w.Factory).RequestAttendance(
                w.EventId, new RequestEventAttendanceRequest($"bulk{i}@example.com", null), default);

        var refused = await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest("one-too-many@example.com", null), default);

        Assert.IsType<ConflictObjectResult>(refused);
    }

    /// <summary>
    /// Somebody asking again for their own link is never the person the ceiling refuses.
    /// </summary>
    /// <remarks>
    /// The guest whose first email went to spam is the most likely person to re-request, and
    /// refusing them at the meeting point would be the exact failure this whole change exists to
    /// prevent. Only new addresses count toward the ceiling.
    /// </remarks>
    [Fact]
    public async Task A_guest_may_always_ask_again_for_their_own_link()
    {
        var w = await SeedAsync();

        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest("late@example.com", "Late"), default);

        for (var i = 0; i < PublicEventAttendanceController.InviteCeilingFloor; i++)
            await Build(w.Factory).RequestAttendance(
                w.EventId, new RequestEventAttendanceRequest($"bulk{i}@example.com", null), default);

        // The event is now at its ceiling, and this address already has a row.
        var again = await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest("late@example.com", "Late"), default);

        Assert.IsType<OkResult>(again);
    }

    private static async Task<string> TokenFor(World w, string email)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return (await db.EventAttendanceInvites.AsNoTracking()
            .FirstAsync(i => i.Email == email)).Token!;
    }

    // ── Asking ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A known address and an unknown one are answered identically. Anything else makes this a way
    /// of testing which emails have accounts here.
    /// </summary>
    [Fact]
    public async Task Asking_answers_the_same_whether_or_not_the_address_is_known()
    {
        var w = await SeedAsync();

        var known = await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(ExistingEmail, null), default);
        var unknown = await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        Assert.IsType<OkResult>(known);
        Assert.IsType<OkResult>(unknown);
    }

    [Fact]
    public async Task Asking_does_not_yet_make_anybody_an_attendee()
    {
        var w = await SeedAsync();
        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        await using var db = await w.Factory.CreateDbContextAsync();

        // A typed address is a claim, not a confirmation. Until the link is clicked, nobody is
        // counted as coming and no account exists.
        Assert.Empty(await db.OrgCalendarEventAttendees.ToListAsync());
        Assert.False(await db.Users.AnyAsync(u => u.Email == StrangerEmail));
    }

    [Fact]
    public async Task Asking_twice_reuses_the_one_pending_invitation()
    {
        var w = await SeedAsync();

        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);
        var first = await TokenFor(w, StrangerEmail);

        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var invites = await db.EventAttendanceInvites.Where(i => i.Email == StrangerEmail).ToListAsync();

        Assert.Single(invites);
        // A fresh token each time, so an old link stops working once a new one is sent.
        Assert.NotEqual(first, invites[0].Token);
    }

    [Fact]
    public async Task A_private_event_cannot_be_asked_about_at_all()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory).RequestAttendance(
            w.PrivateEventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task A_nonsense_address_is_refused()
    {
        var w = await SeedAsync();

        foreach (var bad in new[] { "", "   ", "not-an-email" })
            Assert.IsType<BadRequestObjectResult>(await Build(w.Factory).RequestAttendance(
                w.EventId, new RequestEventAttendanceRequest(bad, null), default));
    }

    // ── Confirming ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirming_creates_a_passwordless_account_and_records_the_attendance()
    {
        var w = await SeedAsync();
        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, "A Stranger"), default);

        var result = await Build(w.Factory).Confirm(await TokenFor(w, StrangerEmail), default);
        Assert.IsType<EventAttendanceConfirmation>(Assert.IsType<OkObjectResult>(result.Result).Value);

        await using var db = await w.Factory.CreateDbContextAsync();

        var user = await db.Users.SingleAsync(u => u.Email == StrangerEmail);
        Assert.Equal("A Stranger", user.DisplayName);
        // Confirmed by clicking a link sent to it, which is what confirmation means.
        Assert.True(user.EmailConfirmed);
        // No password: they never invented one, and are not being asked to.
        Assert.Null(user.PasswordHash);

        var attendee = await db.OrgCalendarEventAttendees.SingleAsync();
        Assert.Equal(user.Id, attendee.AppUserId);
        Assert.Equal(RsvpStatus.Accepted, attendee.RsvpStatus);
    }

    /// <summary>
    /// An address that already has an account attaches to it rather than making a second one.
    /// </summary>
    [Fact]
    public async Task Confirming_a_known_address_uses_the_account_it_already_has()
    {
        var w = await SeedAsync();
        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(ExistingEmail, null), default);

        await Build(w.Factory).Confirm(await TokenFor(w, ExistingEmail), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Single(await db.Users.Where(u => u.Email == ExistingEmail).ToListAsync());
        Assert.Equal(ExistingUserId, (await db.OrgCalendarEventAttendees.SingleAsync()).AppUserId);
    }

    /// <summary>
    /// A link works once. A forwarded email must not hand the event — and its address — to a
    /// mailing list.
    /// </summary>
    [Fact]
    public async Task A_confirmation_link_cannot_be_used_twice()
    {
        var w = await SeedAsync();
        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        var token = await TokenFor(w, StrangerEmail);
        Assert.IsType<OkObjectResult>((await Build(w.Factory).Confirm(token, default)).Result);
        Assert.IsType<NotFoundResult>((await Build(w.Factory).Confirm(token, default)).Result);
    }

    [Fact]
    public async Task An_expired_link_does_not_work()
    {
        var w = await SeedAsync();
        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        var token = await TokenFor(w, StrangerEmail);
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var invite = await db.EventAttendanceInvites.SingleAsync(i => i.Token == token);
            invite.DateExpires = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Build(w.Factory).Confirm(token, default)).Result);
        Assert.IsType<NotFoundResult>((await Build(w.Factory).GetInvite(token, default)).Result);
    }

    /// <summary>
    /// Capacity is re-checked when the link is used, not only when it was sent. A fortnight is long
    /// enough for an event to fill up.
    /// </summary>
    [Fact]
    public async Task An_event_that_filled_up_in_the_meantime_refuses_the_confirmation()
    {
        var w = await SeedAsync();
        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        var token = await TokenFor(w, StrangerEmail);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var ev = await db.OrgCalendarEvents.SingleAsync(e => e.Id == w.EventId);
            ev.AttendeeCapacity = 1;
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = w.EventId, AppUserId = ExistingUserId,
                RsvpStatus = RsvpStatus.Accepted, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = ExistingUserId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<ConflictObjectResult>((await Build(w.Factory).Confirm(token, default)).Result);
    }

    [Fact]
    public async Task The_link_page_says_what_it_is_for_before_it_is_used()
    {
        var w = await SeedAsync();
        await Build(w.Factory).RequestAttendance(
            w.EventId, new RequestEventAttendanceRequest(StrangerEmail, null), default);

        var result = await Build(w.Factory).GetInvite(await TokenFor(w, StrangerEmail), default);
        var info = Assert.IsType<EventAttendanceInviteInfo>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Ghost Walk", info.Title);
        Assert.Equal("Ghost Squad", info.OrganizationName);
        Assert.Equal(StrangerEmail, info.Email);
    }
}
