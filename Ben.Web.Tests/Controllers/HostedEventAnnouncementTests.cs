using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Web.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A host writing to the event's guests (item 235 phase 17a, audit finding A4): who may, who it reaches, and when it
/// is refused.
/// </summary>
public sealed class HostedEventAnnouncementTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid FridayId = Guid.NewGuid();
    private static readonly Guid SaturdayId = Guid.NewGuid();
    private static readonly Guid SeatId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid FridayGuestId = Guid.NewGuid();
    private static readonly Guid SaturdayGuestId = Guid.NewGuid();
    private static readonly Guid DayPassGuestId = Guid.NewGuid();
    private static readonly Guid AskingGuestId = Guid.NewGuid();

    private static async Task<SqliteTestDb> SeedAsync(HostedEventLifecycleState state = HostedEventLifecycleState.Published)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (HostId, "Hana Host"), (MemberId, "Mo Member"), (FridayGuestId, "Fay <Friday>"),
                     (SaturdayGuestId, "Sam Saturday"), (DayPassGuestId, "Dee Daypass"), (AskingGuestId, "Ash Asking") })
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test", DisplayName = name, DateCreated = now,
            });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", PublicEmail = "desk@thomas.test", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Seance Weekend", UrlName = "seance-weekend",
            StartsOn = now.Date.AddDays(20), EndsOn = now.Date.AddDays(21), LifecycleState = state,
            LayoutKind = HostedEventLayoutKind.Seats, BookingMode = HostedEventBookingMode.Ask, TimeZoneId = "America/Chicago",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = FridayId, HostedEventId = EventId, Date = now.Date.AddDays(20), DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventNights.Add(new HostedEventNight { Id = SaturdayId, HostedEventId = EventId, Date = now.Date.AddDays(21), DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit { Id = SeatId, HostedEventId = EventId, Label = "A1", Capacity = 1, DateCreated = now, CreatedByAppUserId = HostId });
        await db.SaveChangesAsync();

        Book(db, FridayGuestId, HostedEventBookingStatus.Confirmed, 2, FridayId);
        Book(db, SaturdayGuestId, HostedEventBookingStatus.Confirmed, 3, SaturdayId);
        Book(db, DayPassGuestId, HostedEventBookingStatus.Confirmed, 1, null);
        Book(db, AskingGuestId, HostedEventBookingStatus.Requested, 4, FridayId);
        await db.SaveChangesAsync();
        return sqlite;
    }

    private static void Book(Ben.Data.Source.Context.BenDataContext db, Guid lead, HostedEventBookingStatus status, int party, Guid? night)
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = lead, PartySize = party, Kind = HostedEventBookingKind.DayPass,
            Status = status, DateCreated = DateTime.UtcNow, CreatedByAppUserId = lead,
        };
        if (night is { } n)
            booking.Nights.Add(new HostedEventBookingNight { Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, HostedEventNightId = n, DateCreated = DateTime.UtcNow });
        db.HostedEventBookings.Add(booking);
    }

    private static HostedEventAnnouncementController Controller(SqliteTestDb sqlite, Guid who, IEmailService? email = null)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _) => user == HostId);

        if (email is null)
        {
            var none = new Mock<IEmailService>();
            none.SetupGet(e => e.IsConfigured).Returns(false);
            email = none.Object;
        }
        var mailer = new EventGuestMailer(email, Options.Create(new Ben.Data.Common.SiteIdentity { Name = "IsHaunted", BaseUrl = "https://test.local" }),
            NullLogger<EventGuestMailer>.Instance);

        return new HostedEventAnnouncementController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object), mailer, new PlatformMessageService(sqlite.Factory))
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

    private static SendHostedEventAnnouncementRequest Letter(Guid? night = null, bool unconfirmed = false, string subject = "Parking has moved")
        => new(subject, "Park behind the church on Main Street tonight.", night, unconfirmed);

    private static async Task<HashSet<Guid>> BellRecipientsAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        return [.. await db.UserMessageTos.Select(t => t.ToAppUserId).ToListAsync()];
    }

    [Fact]
    public async Task A_plain_member_may_not_write_to_the_guests_or_see_what_was_sent()
    {
        await using var sqlite = await SeedAsync();

        Assert.IsType<ForbidResult>((await Controller(sqlite, MemberId).Send(OrgId, EventId, Letter(), default)).Result);
        Assert.IsType<ForbidResult>((await Controller(sqlite, MemberId).Get(OrgId, EventId, default)).Result);
        Assert.IsType<ForbidResult>((await Controller(sqlite, MemberId).Audience(OrgId, EventId, null, false, default)).Result);
        Assert.Empty(await BellRecipientsAsync(sqlite));
    }

    [Fact]
    public async Task A_letter_reaches_every_confirmed_party_once_and_is_recorded()
    {
        await using var sqlite = await SeedAsync();

        var result = Assert.IsType<HostedEventAnnouncementsRecord>(Assert.IsType<OkObjectResult>(
            (await Controller(sqlite, HostId).Send(OrgId, EventId, Letter(), default)).Result).Value);

        Assert.Equal(new HashSet<Guid> { FridayGuestId, SaturdayGuestId, DayPassGuestId }, await BellRecipientsAsync(sqlite));
        var sent = Assert.Single(result.Letters);
        Assert.Equal(3, sent.Recipients);
        Assert.Equal(0, sent.Emailed);
        Assert.Equal("Hana Host", sent.SentByName);
        Assert.Contains("3 parties", result.Note);
    }

    [Fact]
    public async Task Unconfirmed_asks_are_included_only_when_the_host_says_so()
    {
        await using var sqlite = await SeedAsync();
        var host = Controller(sqlite, HostId);

        var without = Assert.IsType<HostedEventAnnouncementAudienceRecord>(Assert.IsType<OkObjectResult>(
            (await host.Audience(OrgId, EventId, null, false, default)).Result).Value);
        Assert.Equal((3, 6), (without.Parties, without.People));

        var with = Assert.IsType<HostedEventAnnouncementAudienceRecord>(Assert.IsType<OkObjectResult>(
            (await host.Audience(OrgId, EventId, null, true, default)).Result).Value);
        Assert.Equal((4, 10), (with.Parties, with.People));
    }

    [Fact]
    public async Task One_night_reaches_that_nights_parties_and_whole_event_passes_but_not_the_other_night()
    {
        await using var sqlite = await SeedAsync();

        Assert.IsType<OkObjectResult>((await Controller(sqlite, HostId).Send(OrgId, EventId, Letter(FridayId), default)).Result);

        var reached = await BellRecipientsAsync(sqlite);
        Assert.Contains(FridayGuestId, reached);
        Assert.Contains(DayPassGuestId, reached);
        Assert.DoesNotContain(SaturdayGuestId, reached);
        Assert.DoesNotContain(AskingGuestId, reached);
    }

    [Fact]
    public async Task The_email_names_the_event_escapes_what_the_host_wrote_and_says_why_it_came()
    {
        await using var sqlite = await SeedAsync();
        var sent = new List<EmailMessage>();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback((EmailMessage m, CancellationToken _) => sent.Add(m)).Returns(Task.CompletedTask);

        var result = Assert.IsType<HostedEventAnnouncementsRecord>(Assert.IsType<OkObjectResult>(
            (await Controller(sqlite, HostId, email.Object).Send(OrgId, EventId,
                new SendHostedEventAnnouncementRequest("Doors at eight", "Bring a torch <b>and</b> a coat.\nThe lift is out.", FridayId, false), default)).Result).Value);

        Assert.Equal(2, sent.Count);
        Assert.All(sent, m => Assert.Equal("Seance Weekend: Doors at eight", m.Subject));
        Assert.All(sent, m => Assert.Equal("desk@thomas.test", m.ReplyTo));
        var fay = Assert.Single(sent, m => m.HtmlBody.Contains("Fay &lt;Friday&gt;"));
        Assert.Contains("Bring a torch &lt;b&gt;and&lt;/b&gt; a coat.<br />The lift is out.", fay.HtmlBody);
        Assert.Contains("because you have a place at", fay.HtmlBody);
        Assert.Equal(2, Assert.Single(result.Letters).Emailed);
    }

    [Theory]
    [InlineData(HostedEventLifecycleState.Draft, "Publish the event first")]
    [InlineData(HostedEventLifecycleState.Cancelled, "called off")]
    public async Task An_event_nobody_can_be_coming_to_is_refused_in_words(HostedEventLifecycleState state, string words)
    {
        await using var sqlite = await SeedAsync(state);

        var refused = Assert.IsType<BadRequestObjectResult>((await Controller(sqlite, HostId).Send(OrgId, EventId, Letter(), default)).Result);
        Assert.Contains(words, (string)refused.Value!);
        Assert.Empty(await BellRecipientsAsync(sqlite));
    }

    [Fact]
    public async Task An_empty_subject_and_the_eleventh_letter_in_a_day_are_refused()
    {
        await using var sqlite = await SeedAsync();
        var host = Controller(sqlite, HostId);

        Assert.IsType<BadRequestObjectResult>((await host.Send(OrgId, EventId, Letter(subject: "  "), default)).Result);

        for (var i = 0; i < HostedEventAnnouncements.MaxPerDay; i++)
            Assert.IsType<OkObjectResult>((await host.Send(OrgId, EventId, Letter(subject: $"Update {i}"), default)).Result);

        var refused = Assert.IsType<BadRequestObjectResult>((await host.Send(OrgId, EventId, Letter(subject: "One more"), default)).Result);
        Assert.Contains("in the last day", (string)refused.Value!);
    }

    [Fact]
    public async Task A_night_nobody_has_a_place_on_says_so_and_a_date_of_another_event_is_refused()
    {
        await using var sqlite = await SeedAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            await db.HostedEventBookings.Where(b => b.LeadAppUserId != FridayGuestId).ExecuteDeleteAsync();
        }
        var host = Controller(sqlite, HostId);

        var nobody = Assert.IsType<ConflictObjectResult>((await host.Send(OrgId, EventId, Letter(SaturdayId), default)).Result);
        Assert.Contains("Nobody has a place on that date", (string)nobody.Value!);
        Assert.IsType<BadRequestObjectResult>((await host.Send(OrgId, EventId, Letter(Guid.NewGuid()), default)).Result);
    }
}
