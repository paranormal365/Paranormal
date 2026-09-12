using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// An event moving through its own life, on the venue's clock (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>The clock is the whole difficulty.</b> An event at the Thomas House starts at seven
/// Central whatever the server thinks the time is, and the job has to agree with the calendar row
/// about when tonight began — so the tests are written in Central and the assertions are about the
/// instant the state flips, not about a date.</para>
///
/// <para><b>Running twice must do nothing twice.</b> The scheduler promises only "not more than
/// once at a time", so every claim here is checked after two passes as well as one.</para>
/// </remarks>
public sealed class HostedEventLifecycleJobTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    /// <summary>A Friday-and-Saturday weekend, seven in the evening to two in the morning.</summary>
    private static HostedEvent Weekend(HostedEventLifecycleState state)
    {
        var friday = new DateTime(2026, 10, 30);
        var hosted = new HostedEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrgId,
            Name = "Thomas House Weekend",
            UrlName = "thomas-house-weekend",
            TimeZoneId = "America/Chicago",
            StartsOn = friday,
            EndsOn = friday.AddDays(1),
            DefaultStartLocal = new TimeSpan(19, 0, 0),
            DefaultEndLocal = new TimeSpan(23, 0, 0),
            LifecycleState = state,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = OwnerId,
        };
        hosted.Nights.Add(new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = hosted.Id, Date = friday, SortOrder = 0,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        hosted.Nights.Add(new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = hosted.Id, Date = friday.AddDays(1), SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        return hosted;
    }

    /// <summary>Central time as an instant, which is the only way these boundaries mean anything.</summary>
    private static DateTime Central(int year, int month, int day, int hour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        return TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified), zone);
    }

    // ── the boundaries ───────────────────────────────────────────────────────

    [Fact]
    public void An_event_is_not_on_yet_at_six_and_is_on_at_seven()
    {
        // Seven Central on the Friday is when it begins. At six it has not, and the difference is
        // one hour on a clock in Tennessee, not on the one in the rack.
        var hosted = Weekend(HostedEventLifecycleState.Published);

        Assert.Equal(Central(2026, 10, 30, 19),
            Ben.Data.WebApi.Services.Events.HostedEventCalendarSync
                .Window(hosted, [.. hosted.Nights]).StartUtc);
    }

    [Fact]
    public void It_is_over_when_the_last_night_ends_and_not_when_the_last_date_does()
    {
        // Eleven on the Saturday evening, not midnight at the start of it. An event that read as
        // over at 00:00 would close its own door six hours before anybody arrived.
        var hosted = Weekend(HostedEventLifecycleState.Live);

        Assert.Equal(Central(2026, 10, 31, 23),
            Ben.Data.WebApi.Services.Events.HostedEventCalendarSync
                .Window(hosted, [.. hosted.Nights]).EndUtc);
    }

    [Fact]
    public void A_fortnight_is_what_stands_between_over_and_filed_away()
        => Assert.Equal(TimeSpan.FromDays(14), HostedEventLifecycleJob.ArchiveAfter);

    // ── what the states themselves promise ───────────────────────────────────

    [Fact]
    public void An_event_that_is_on_is_still_taking_bookings_and_one_that_is_over_is_not()
    {
        // The pair of answers no single boolean could give, and the reason the job writes a state
        // rather than every screen comparing two dates.
        var live = Weekend(HostedEventLifecycleState.Live);
        var over = Weekend(HostedEventLifecycleState.Ended);

        Assert.True(live.IsTakingBookings);
        Assert.True(live.IsOnThePublicSite);

        Assert.False(over.IsTakingBookings);
        Assert.True(over.IsOnThePublicSite);
    }

    [Fact]
    public void A_filed_away_event_is_off_the_lists_entirely()
    {
        var filed = Weekend(HostedEventLifecycleState.Archived);

        Assert.False(filed.IsActive);
        Assert.False(filed.IsOnThePublicSite);
        Assert.False(filed.IsTakingBookings);
    }

    // ── the job, against a real database ─────────────────────────────────────

    /// <summary>An email service that is switched off, which is the honest default here.</summary>
    /// <remarks>
    /// The job must work on a deployment with no mail server: the platform message is the path
    /// that always exists, and the letter is the extra. Wiring a fake sender instead would have
    /// tested the wrong half.
    /// </remarks>
    private sealed class NoMailServer : IEmailService
    {
        public bool IsConfigured => false;
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => throw new InvalidOperationException("Nothing may send mail on a machine with none.");
    }

    private static HostedEventLifecycleJob Build(SqliteTestDb db)
        => new(db.Factory,
               new NoMailServer(),
               new PlatformMessageService(db.Factory),
               Options.Create(new SiteIdentity { Name = "IsHaunted.com" }),
               NullLogger<HostedEventLifecycleJob>.Instance);

    private static readonly Guid PlaceId = Guid.NewGuid();

    /// <summary>A database with just enough world for one event to exist in.</summary>
    /// <remarks>
    /// The owner, the group and the venue are all real foreign keys and SQLite enforces them here,
    /// which is the point of using it: an event with a dangling organization would save happily
    /// under the in-memory provider and tell us nothing.
    /// </remarks>
    private static async Task<SqliteTestDb> WithAsync(HostedEvent hosted)
    {
        var db = await SqliteTestDb.CreateAsync();
        await using var context = await db.NewContextAsync();

        var now = DateTime.UtcNow;
        context.Users.Add(new AppUser
        {
            Id = OwnerId, Email = "owner@example.test", UserName = "owner@example.test",
            DisplayName = "The owner", DateCreated = now,
        });
        context.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = now, CreatedByAppUserId = OwnerId,
        });
        context.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = now, CreatedByAppUserId = OwnerId,
        });

        hosted.PlaceId = PlaceId;
        context.HostedEvents.Add(hosted);
        await context.SaveChangesAsync();
        return db;
    }

    private static async Task<HostedEvent> ReadAsync(SqliteTestDb db, Guid id)
    {
        await using var context = await db.NewContextAsync();
        return await context.HostedEvents.FirstAsync(e => e.Id == id);
    }

    [Fact]
    public async Task A_published_event_whose_first_night_has_started_is_on_now()
    {
        var hosted = Weekend(HostedEventLifecycleState.Published);
        // Backdated, because the job reads the real clock and the weekend above is in October.
        Shift(hosted, DateTime.UtcNow.AddHours(-2));

        await using var db = await WithAsync(hosted);
        await Build(db).RunAsync(default);

        var after = await ReadAsync(db, hosted.Id);
        Assert.Equal(HostedEventLifecycleState.Live, after.LifecycleState);
        Assert.NotNull(after.LiveAtUtc);
    }

    [Fact]
    public async Task An_event_that_has_not_started_is_left_alone()
    {
        var hosted = Weekend(HostedEventLifecycleState.Published);
        Shift(hosted, DateTime.UtcNow.AddDays(30));

        await using var db = await WithAsync(hosted);
        await Build(db).RunAsync(default);

        var after = await ReadAsync(db, hosted.Id);
        Assert.Equal(HostedEventLifecycleState.Published, after.LifecycleState);
        Assert.Null(after.LiveAtUtc);
    }

    [Fact]
    public async Task A_draft_never_moves_on_its_own_however_long_ago_its_dates_were()
    {
        // Publishing is a decision that costs money. A job that published anything would be the
        // site spending somebody's ninety-nine dollars for them.
        var hosted = Weekend(HostedEventLifecycleState.Draft);
        Shift(hosted, DateTime.UtcNow.AddDays(-90));

        await using var db = await WithAsync(hosted);
        await Build(db).RunAsync(default);

        Assert.Equal(HostedEventLifecycleState.Draft, (await ReadAsync(db, hosted.Id)).LifecycleState);
    }

    [Fact]
    public async Task A_called_off_event_stays_called_off_whatever_the_dates_say()
    {
        var hosted = Weekend(HostedEventLifecycleState.Cancelled);
        Shift(hosted, DateTime.UtcNow.AddDays(-90));

        await using var db = await WithAsync(hosted);
        await Build(db).RunAsync(default);

        Assert.Equal(HostedEventLifecycleState.Cancelled, (await ReadAsync(db, hosted.Id)).LifecycleState);
    }

    [Fact]
    public async Task An_event_takes_one_step_per_pass_and_reaches_the_end_by_running_again()
    {
        // One step at a time on purpose: each move has a letter or a consequence attached, and
        // doing four inside one loop means three of them happen with no chance to fail safely in
        // between. So a long-finished event needs several passes, and gets there.
        var hosted = Weekend(HostedEventLifecycleState.Published);
        Shift(hosted, DateTime.UtcNow.AddDays(-30));

        await using var db = await WithAsync(hosted);
        var job = Build(db);

        await job.RunAsync(default);
        Assert.Equal(HostedEventLifecycleState.Ended, (await ReadAsync(db, hosted.Id)).LifecycleState);

        await job.RunAsync(default);
        Assert.Equal(HostedEventLifecycleState.Archived, (await ReadAsync(db, hosted.Id)).LifecycleState);

        // And a third pass changes nothing, which is what "safe to run at any time" means.
        await job.RunAsync(default);
        Assert.Equal(HostedEventLifecycleState.Archived, (await ReadAsync(db, hosted.Id)).LifecycleState);
    }

    [Fact]
    public async Task An_event_that_has_only_just_ended_is_not_filed_away_yet()
    {
        var hosted = Weekend(HostedEventLifecycleState.Ended);
        Shift(hosted, DateTime.UtcNow.AddDays(-3));
        hosted.EndedAtUtc = DateTime.UtcNow.AddDays(-2);

        await using var db = await WithAsync(hosted);
        await Build(db).RunAsync(default);

        Assert.Equal(HostedEventLifecycleState.Ended, (await ReadAsync(db, hosted.Id)).LifecycleState);
    }

    [Fact]
    public async Task Nobody_is_left_waiting_on_an_event_that_is_over()
    {
        // Decision 11. A request nobody ever answered is worse than one turned down: the person is
        // still, as far as they know, waiting to hear. The site answers in the venue's absence and
        // says exactly that.
        var hosted = Weekend(HostedEventLifecycleState.Ended);
        Shift(hosted, DateTime.UtcNow.AddDays(-30));
        hosted.EndedAtUtc = DateTime.UtcNow.AddDays(-20);

        await using var db = await WithAsync(hosted);

        Guid bookingId;
        await using (var context = await db.NewContextAsync())
        {
            var booking = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = hosted.Id, LeadAppUserId = OwnerId,
                Kind = HostedEventBookingKind.DayPass,
                Status = HostedEventBookingStatus.Requested, PartySize = 2,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            };
            bookingId = booking.Id;
            context.HostedEventBookings.Add(booking);
            await context.SaveChangesAsync();
        }

        var job = Build(db);
        await job.RunAsync(default);

        await using (var context = await db.NewContextAsync())
        {
            var booking = await context.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            Assert.Equal(HostedEventBookingStatus.TurnedDown, booking.Status);
            Assert.Equal("The event has passed.", booking.DecisionNote);
        }

        // Telling them and filing it away are separate passes, so a failure in the first cannot
        // silently lose the second.
        Assert.Equal(HostedEventLifecycleState.Ended, (await ReadAsync(db, hosted.Id)).LifecycleState);

        await job.RunAsync(default);
        Assert.Equal(HostedEventLifecycleState.Archived, (await ReadAsync(db, hosted.Id)).LifecycleState);
    }

    [Fact]
    public async Task A_confirmed_booking_on_a_finished_event_is_left_exactly_as_it_is()
    {
        // What happened, happened. Rewriting a confirmed booking a fortnight later would erase the
        // record of a party that actually came.
        var hosted = Weekend(HostedEventLifecycleState.Ended);
        Shift(hosted, DateTime.UtcNow.AddDays(-30));
        hosted.EndedAtUtc = DateTime.UtcNow.AddDays(-20);

        await using var db = await WithAsync(hosted);

        Guid bookingId;
        await using (var context = await db.NewContextAsync())
        {
            var booking = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = hosted.Id, LeadAppUserId = OwnerId,
                Kind = HostedEventBookingKind.DayPass,
                Status = HostedEventBookingStatus.Confirmed, PartySize = 2,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            };
            bookingId = booking.Id;
            context.HostedEventBookings.Add(booking);
            await context.SaveChangesAsync();
        }

        await Build(db).RunAsync(default);

        await using (var context = await db.NewContextAsync())
        {
            var booking = await context.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            Assert.Equal(HostedEventBookingStatus.Confirmed, booking.Status);
        }
    }

    // ── the decision reminders ───────────────────────────────────────────────

    [Fact]
    public async Task A_week_out_the_organizer_is_reminded_once_and_not_again()
    {
        var hosted = Weekend(HostedEventLifecycleState.Published);
        Shift(hosted, DateTime.UtcNow.AddDays(20));
        hosted.MinimumGuests = 12;
        hosted.GoNoGoDeadlineUtc = DateTime.UtcNow.AddDays(5);

        await using var db = await WithAsync(hosted);
        var job = Build(db);

        await job.RunAsync(default);
        var after = await ReadAsync(db, hosted.Id);

        // Five days out the week's letter is due and the day's is not, so only one goes.
        Assert.True(after.GoNoGoWeekReminderSent);
        Assert.False(after.GoNoGoDayReminderSent);

        // And a second pass sends nothing more. Without the marker a host would be reminded every
        // five minutes for five days, which is the same as not being reminded.
        await job.RunAsync(default);
        var again = await ReadAsync(db, hosted.Id);
        Assert.True(again.GoNoGoWeekReminderSent);
        Assert.False(again.GoNoGoDayReminderSent);
    }

    [Fact]
    public async Task A_deadline_set_at_short_notice_gets_one_letter_and_not_two()
    {
        // A deadline tomorrow means both the week's reminder and the day's are overdue at once.
        // Two letters a second apart saying the same thing is not two reminders, so the day's
        // wins and both markers are stamped.
        var hosted = Weekend(HostedEventLifecycleState.Published);
        Shift(hosted, DateTime.UtcNow.AddDays(20));
        hosted.MinimumGuests = 12;
        hosted.GoNoGoDeadlineUtc = DateTime.UtcNow.AddHours(12);

        await using var db = await WithAsync(hosted);
        await Build(db).RunAsync(default);

        var after = await ReadAsync(db, hosted.Id);
        Assert.True(after.GoNoGoWeekReminderSent);
        Assert.True(after.GoNoGoDayReminderSent);
    }

    [Fact]
    public async Task An_event_with_no_minimum_is_never_reminded_about_a_decision()
    {
        var hosted = Weekend(HostedEventLifecycleState.Published);
        Shift(hosted, DateTime.UtcNow.AddDays(20));

        await using var db = await WithAsync(hosted);
        await Build(db).RunAsync(default);

        var after = await ReadAsync(db, hosted.Id);
        Assert.False(after.GoNoGoWeekReminderSent);
        Assert.False(after.GoNoGoDayReminderSent);
    }

    /// <summary>Moves a whole weekend so its first night starts at a given instant.</summary>
    /// <remarks>
    /// The dates are written as a real October weekend for readability, and then shifted, because
    /// the job reads the actual clock and a test pinned to 2026 would start failing in 2027.
    /// </remarks>
    private static void Shift(HostedEvent hosted, DateTime firstNightStartsUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(hosted.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(firstNightStartsUtc, zone);

        hosted.StartsOn = local.Date;
        hosted.EndsOn = local.Date.AddDays(1);
        hosted.DefaultStartLocal = local.TimeOfDay;
        hosted.DefaultEndLocal = local.TimeOfDay;

        var ordered = hosted.Nights.OrderBy(n => n.SortOrder).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Date = local.Date.AddDays(i);
    }
}
