using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// After the event: who may review it, and the thank-you (item 235 phase 12).
/// </summary>
public sealed class HostedEventAfterTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid NextEventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid CameId = Guid.NewGuid();
    private static readonly Guid BroughtId = Guid.NewGuid();
    private static readonly Guid AskedId = Guid.NewGuid();

    /// <summary>
    /// The run: the two days before today, so a test written this year still means the same next year.
    /// </summary>
    private static readonly DateTime FirstDay = DateTime.UtcNow.Date.AddDays(-3);
    private static readonly DateTime LastDay = FirstDay.AddDays(1);

    /// <summary>When the last night ends on the venue's clock (11 PM by default), in UTC.</summary>
    private static readonly DateTime EndsUtc = HostedEventCalendarSync.Window(
        new HostedEvent { TimeZoneId = "America/Chicago", StartsOn = FirstDay, EndsOn = LastDay }, []).EndUtc;

    private sealed class Letters : IEmailService
    {
        public readonly List<(string To, string Subject, string Body)> Sent = [];
        public bool IsConfigured => true;

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private static async Task<SqliteTestDb> SeedAsync(HostedEventLifecycleState state = HostedEventLifecycleState.Ended)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (HostId, "host"), (CameId, "came"), (BroughtId, "brought"), (AskedId, "asked") })
            db.Users.Add(new AppUser { Id = id, Email = $"{name}@example.test", UserName = $"{name}@example.test", DisplayName = name, DateCreated = now });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });

        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Séance Weekend", UrlName = "seance-weekend",
            TimeZoneId = "America/Chicago", StartsOn = FirstDay, EndsOn = LastDay,
            LifecycleState = state, ThankYouNote = "We loved having you.\n\nSee you in March.",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = EventId, Date = FirstDay, DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventNights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = EventId, Date = LastDay, SortOrder = 1, DateCreated = now, CreatedByAppUserId = HostId });

        db.HostedEvents.Add(new HostedEvent
        {
            Id = NextEventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Spring Séance", UrlName = "spring-seance",
            StartsOn = DateTime.UtcNow.Date.AddDays(120), EndsOn = DateTime.UtcNow.Date.AddDays(121),
            LifecycleState = HostedEventLifecycleState.Published, DateCreated = now, CreatedByAppUserId = HostId,
        });

        var came = new HostedEventBooking { Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = CameId, Status = HostedEventBookingStatus.Confirmed, PartySize = 2, DateCreated = now, CreatedByAppUserId = CameId };
        came.Guests.Add(new HostedEventBookingGuest { Id = Guid.NewGuid(), HostedEventBookingId = came.Id, DisplayName = "brought", AppUserId = BroughtId, DateCreated = now });
        db.HostedEventBookings.Add(came);
        db.HostedEventBookings.Add(new HostedEventBooking { Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = AskedId, Status = HostedEventBookingStatus.TurnedDown, DateCreated = now, CreatedByAppUserId = AskedId });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static PublicHostedEventReviewController As(SqliteTestDb sqlite, Guid? userId)
        => new(sqlite.Factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = userId is { } id
                        ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"))
                        : new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };

    private static async Task<HostedEvent> EventAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        return await db.HostedEvents.AsNoTracking().SingleAsync(e => e.Id == EventId);
    }

    // ── reviews ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Somebody_with_a_confirmed_place_may_review_and_so_may_the_guest_they_named()
    {
        await using var sqlite = await SeedAsync();
        await using var db = await sqlite.NewContextAsync();
        var ev = await EventAsync(sqlite);
        var now = EndsUtc.AddDays(2);

        Assert.Null(await HostedEventReviews.WhyNotAsync(db, ev, CameId, now, default));
        Assert.Null(await HostedEventReviews.WhyNotAsync(db, ev, BroughtId, now, default));
        Assert.Equal("Reviews come from people who had a place at the event.",
            await HostedEventReviews.WhyNotAsync(db, ev, AskedId, now, default));
        Assert.Equal("Sign in to leave a review.", await HostedEventReviews.WhyNotAsync(db, ev, null, now, default));
    }

    [Fact]
    public async Task Reviews_open_when_the_event_is_over_and_close_two_months_later()
    {
        await using var sqlite = await SeedAsync(HostedEventLifecycleState.Live);
        await using var db = await sqlite.NewContextAsync();
        var ev = await EventAsync(sqlite);

        Assert.Equal("Reviews open once the event is over.",
            await HostedEventReviews.WhyNotAsync(db, ev, CameId, EndsUtc.AddDays(-1), default));

        ev.LifecycleState = HostedEventLifecycleState.Archived;
        Assert.Null(await HostedEventReviews.WhyNotAsync(db, ev, CameId, EndsUtc.AddDays(59), default));
        Assert.Equal("Reviews for this event have closed.",
            await HostedEventReviews.WhyNotAsync(db, ev, CameId, EndsUtc.AddDays(62), default));

        ev.AllowReviews = false;
        Assert.Equal("This event isn't taking reviews.",
            await HostedEventReviews.WhyNotAsync(db, ev, CameId, EndsUtc.AddDays(2), default));
    }

    [Fact]
    public async Task A_hidden_review_leaves_the_average_and_the_page_but_not_its_author()
    {
        await using var sqlite = await SeedAsync();

        // Left through the door, which checks the rule itself.
        Assert.IsType<OkObjectResult>((await As(sqlite, CameId).Upsert(EventId, new UpsertTourReviewRequest(5, "Wonderful"), default)).Result);
        Assert.IsType<OkObjectResult>((await As(sqlite, BroughtId).Upsert(EventId, new UpsertTourReviewRequest(1, "Cold"), default)).Result);

        await using (var db = await sqlite.NewContextAsync())
        {
            var cold = await db.HostedEventReviews.SingleAsync(r => r.AppUserId == BroughtId);
            cold.HiddenAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var stranger = Assert.IsType<PublicHostedEventReviewsRecord>(
            Assert.IsType<OkObjectResult>((await As(sqlite, null).Get(EventId, default)).Result).Value);
        Assert.Equal(5.0m, stranger.Reviews.Average);
        Assert.Single(stranger.Reviews.Reviews);

        var author = Assert.IsType<PublicHostedEventReviewsRecord>(
            Assert.IsType<OkObjectResult>((await As(sqlite, BroughtId).Get(EventId, default)).Result).Value);
        Assert.Contains(author.Reviews.Reviews, r => r.IsMine && r.IsHidden);

        // Rewriting it clears the hiding: new words are judged afresh.
        await As(sqlite, BroughtId).Upsert(EventId, new UpsertTourReviewRequest(3, "Cold, but the séance was good"), default);
        await using var check = await sqlite.NewContextAsync();
        Assert.Null((await check.HostedEventReviews.SingleAsync(r => r.AppUserId == BroughtId)).HiddenAtUtc);
    }

    [Fact]
    public async Task Somebody_who_was_turned_down_cannot_review()
    {
        await using var sqlite = await SeedAsync();
        var refused = Assert.IsType<BadRequestObjectResult>(
            (await As(sqlite, AskedId).Upsert(EventId, new UpsertTourReviewRequest(1, "Never got in"), default)).Result);
        Assert.Contains("had a place", (string)refused.Value!);
    }

    [Fact]
    public async Task The_next_event_shows_what_guests_made_of_the_earlier_ones()
    {
        await using var sqlite = await SeedAsync();
        await As(sqlite, CameId).Upsert(EventId, new UpsertTourReviewRequest(4, null), default);

        await using var db = await sqlite.NewContextAsync();
        var (average, count) = await HostedEventReviews.PastRatingAsync(db, OrgId, NextEventId, default);
        Assert.Equal(4.0m, average);
        Assert.Equal(1, count);

        var (own, ownCount) = await HostedEventReviews.PastRatingAsync(db, OrgId, EventId, default);
        Assert.Null(own);
        Assert.Equal(0, ownCount);
    }

    // ── the thank-you ────────────────────────────────────────────────────────

    private static (HostedEventThankYouJob Job, Letters Letters) Job(SqliteTestDb sqlite)
    {
        var letters = new Letters();
        var mailer = new EventGuestMailer(letters,
            Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            NullLogger<EventGuestMailer>.Instance);
        return (new HostedEventThankYouJob(sqlite.Factory, mailer, NullLogger<HostedEventThankYouJob>.Instance), letters);
    }

    [Fact]
    public async Task The_thank_you_goes_the_next_morning_once_to_each_party_that_came()
    {
        await using var sqlite = await SeedAsync();
        var (job, letters) = Job(sqlite);

        await job.RunAtAsync(EndsUtc.AddHours(6), default);
        Assert.Empty(letters.Sent);

        await job.RunAtAsync(EndsUtc.AddHours(13), default);
        var letter = Assert.Single(letters.Sent);
        Assert.Equal("came@example.test", letter.To);
        Assert.Contains("We loved having you.", letter.Body);
        Assert.Contains("/my-events/" + EventId + "/review", letter.Body);
        Assert.Contains("/o/thomas-house/events/spring-seance", letter.Body);
        // No pictures were added, so there is no link to none.
        Assert.DoesNotContain("hosted-gallery", letter.Body);

        await job.RunAtAsync(EndsUtc.AddHours(20), default);
        Assert.Single(letters.Sent);

        await using var db = await sqlite.NewContextAsync();
        Assert.NotNull((await db.HostedEvents.SingleAsync(e => e.Id == EventId)).ThankYouSentUtc);
    }

    [Fact]
    public async Task No_thank_you_when_the_organizer_turned_it_off_or_the_event_is_long_past()
    {
        await using var sqlite = await SeedAsync();
        var (job, letters) = Job(sqlite);

        // A week and more after the end: the day this ships, every old event.
        await job.RunAtAsync(EndsUtc.AddDays(8), default);
        Assert.Empty(letters.Sent);

        await using (var db = await sqlite.NewContextAsync())
        {
            var ev = await db.HostedEvents.SingleAsync(e => e.Id == EventId);
            ev.SendThankYou = false;
            await db.SaveChangesAsync();
        }

        await job.RunAtAsync(EndsUtc.AddHours(13), default);
        Assert.Empty(letters.Sent);
    }

    [Fact]
    public async Task The_pictures_are_linked_when_there_are_some()
    {
        await using var sqlite = await SeedAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            var fileId = Guid.NewGuid();
            var typeId = Guid.NewGuid();
            db.UploadFileTypes.Add(new UploadFileType { Id = typeId, Name = "Pictures", DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId });
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = typeId, FileName = "a.jpg", StoredFileName = "a.jpg",
                ContentType = "image/jpeg", DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });
            db.HostedEventGalleryImages.Add(new HostedEventGalleryImage
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, UploadFileId = fileId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });
            await db.SaveChangesAsync();
        }

        var (job, letters) = Job(sqlite);
        await job.RunAtAsync(EndsUtc.AddHours(13), default);

        Assert.Contains("/o/thomas-house/events/seance-weekend#hosted-gallery", Assert.Single(letters.Sent).Body);
    }
}
