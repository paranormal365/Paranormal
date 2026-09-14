using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The SuperAdmin's Event health tab: whether hosted events are working (Ben, 2026-09-14).
/// </summary>
/// <remarks>
/// Each panel answers a question somebody developing the feature would otherwise answer by reading rows, so each test
/// builds exactly the rows that answer it and checks the panel says the same.
/// </remarks>
public sealed class HostedEventHealthTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid LiveEvent = Guid.NewGuid();
    private static readonly Guid EndedEvent = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 14, 15, 0, 0, DateTimeKind.Utc);

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        db.Users.Add(new AppUser { Id = Organizer, Email = "o@example.test", UserName = "o@example.test", DisplayName = "Olive", DateCreated = Now });
        db.Organizations.Add(new Organization { Id = OrgId, Name = "Night Watch", UrlName = "night-watch", DateCreated = Now, CreatedByAppUserId = Organizer });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Old Mill", DateCreated = Now, CreatedByAppUserId = Organizer });
        foreach (var (id, state) in new[] { (LiveEvent, HostedEventLifecycleState.Published), (EndedEvent, HostedEventLifecycleState.Ended) })
            db.HostedEvents.Add(new HostedEvent
            {
                Id = id, OrganizationId = OrgId, PlaceId = PlaceId, Name = $"Event {id:N}", UrlName = $"e-{id:N}",
                StartsOn = Now.Date.AddDays(5), EndsOn = Now.Date.AddDays(5), LifecycleState = state,
                ContactLine = "Call us.", DateCreated = Now.AddDays(-20), CreatedByAppUserId = Organizer,
            });

        // A guest per booking, so the one-live-booking-per-lead rule never gets in the way of the shapes below.
        HostedEventBooking Booking(Guid eventId, HostedEventBookingStatus status, DateTime created)
        {
            var lead = Guid.NewGuid();
            db.Users.Add(new AppUser { Id = lead, Email = $"{lead:N}@example.test", UserName = $"{lead:N}@example.test", DateCreated = Now });
            var booking = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = eventId, LeadAppUserId = lead, PartySize = 2,
                Kind = HostedEventBookingKind.Overnight, Status = status, DateCreated = created, CreatedByAppUserId = lead,
            };
            db.HostedEventBookings.Add(booking);
            return booking;
        }

        Booking(LiveEvent, HostedEventBookingStatus.Held, Now.AddHours(-1)).HoldExpiresUtc = Now.AddHours(2);      // lapses soon
        Booking(LiveEvent, HostedEventBookingStatus.Held, Now.AddHours(-1)).HoldExpiresUtc = Now.AddDays(3);       // lapses later
        Booking(LiveEvent, HostedEventBookingStatus.Requested, Now.AddHours(-10));                                  // the longest wait
        Booking(EndedEvent, HostedEventBookingStatus.Requested, Now.AddDays(-9));                                   // past: not waiting
        Booking(LiveEvent, HostedEventBookingStatus.Confirmed, Now.AddDays(-2)).DecidedUtc = Now.AddDays(-2).AddMinutes(30);
        Booking(LiveEvent, HostedEventBookingStatus.TurnedDown, Now.AddDays(-4)).DecidedUtc = Now.AddDays(-2);
        Booking(LiveEvent, HostedEventBookingStatus.Expired, Now.AddDays(-3)).HoldExpiresUtc = Now.AddDays(-1);

        db.OutboxEmails.AddRange(
            new OutboxEmail { Id = Guid.NewGuid(), To = "a@example.test", Subject = "Waiting", CreatedUtc = Now.AddMinutes(-3), NextAttemptUtc = Now },
            new OutboxEmail { Id = Guid.NewGuid(), To = "b@example.test", Subject = "Sent", CreatedUtc = Now.AddDays(-1), NextAttemptUtc = Now, AcceptedBySmtpUtc = Now.AddDays(-1) },
            new OutboxEmail { Id = Guid.NewGuid(), To = "c@example.test", Subject = "Failed", CreatedUtc = Now.AddDays(-1), NextAttemptUtc = Now, FailedUtc = Now.AddDays(-1) });

        db.RateLimitRefusals.AddRange(
            new RateLimitRefusal { Id = Guid.NewGuid(), PolicyName = RateLimiting.HostedBookingPolicy, Refusals = 12, DateFirstSeen = Now.AddDays(-5), DateLastSeen = Now.AddDays(-1) },
            new RateLimitRefusal { Id = Guid.NewGuid(), PolicyName = RateLimiting.AuthPolicy, Refusals = 99, DateFirstSeen = Now.AddDays(-5), DateLastSeen = Now.AddDays(-1) });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task<Ben.Service.Models.Admin.AdminHostedEventHealth> ReadAsync(SqliteTestDb sqlite, ScheduledJobLedger? ledger = null)
    {
        await using var db = await sqlite.NewContextAsync();
        ledger ??= new ScheduledJobLedger();
        return await HostedEventHealthStats.ReadAsync(db, 30, Now,
            HostedEventErrorLog.Result.Without("Not SQL Server in a test."), ledger.SinceUtc, ledger.Snapshot(), default);
    }

    [Fact]
    public async Task Holds_and_waits_are_read_as_they_stand_now()
    {
        await using var sqlite = await SeedAsync();
        var health = await ReadAsync(sqlite);

        Assert.Equal((2, 1), (health.HoldsLiveNow, health.HoldsLapsingNextDay));
        // Two holds and one ask at the event still taking bookings; the ask at the event that has ended is not waiting.
        Assert.Equal(3, health.WaitingNow);
        Assert.Equal(10.0, health.OldestWaitHours);
    }

    [Fact]
    public async Task Answers_are_bucketed_by_how_long_the_party_waited()
    {
        await using var sqlite = await SeedAsync();
        var health = await ReadAsync(sqlite);

        Assert.Equal(
            ["Within an hour: 1", "1 to 6 hours: 0", "6 to 24 hours: 0", "1 to 3 days: 1", "Over 3 days: 0"],
            health.TimeToAnswer.Select(s => $"{s.Label}: {s.Count}"));
        Assert.Equal(2, health.AnsweredPerDay.Sum(p => p.Count));
        Assert.Equal(1, health.HoldsLapsedPerDay.Sum(p => p.Count));
        Assert.Equal(30, health.BookingsMadePerDay.Count);
    }

    [Fact]
    public async Task Letters_and_the_booking_limits_are_counted_and_other_limits_are_not()
    {
        await using var sqlite = await SeedAsync();
        var health = await ReadAsync(sqlite);

        Assert.Equal((1, 1), (health.LettersWaitingNow, health.LettersFailedInPeriod));
        Assert.Equal((3, 1, 1), (health.LettersQueuedPerDay.Sum(p => p.Count), health.LettersSentPerDay.Sum(p => p.Count), health.LettersFailedPerDay.Sum(p => p.Count)));

        var refusal = Assert.Single(health.RateLimitRefusals);
        Assert.StartsWith(RateLimiting.HostedBookingPolicy, refusal.Label);
        Assert.Equal(12, refusal.Count);
    }

    [Fact]
    public async Task Where_the_error_log_cannot_be_read_the_tab_says_why_rather_than_showing_none()
    {
        await using var sqlite = await SeedAsync();
        var health = await ReadAsync(sqlite);

        Assert.Null(health.ErrorsPerDay);
        Assert.Equal("Not SQL Server in a test.", health.ErrorsUnavailable);
    }

    [Theory]
    [InlineData("/api/organizations/8fb7d9be-9de4-4aba-adda-0494dbb91fc2/events/40000002-0000-0000-0000-000000000002/bookings", true)]
    [InlineData("/api/public/hosted-events/40000002-0000-0000-0000-000000000002/holds", true)]
    [InlineData("/api/admin/hosted-events/health", true)]
    [InlineData("/media/event-photo/40000002-0000-0000-0000-000000000002", true)]
    [InlineData("/api/organizations/8fb7d9be-9de4-4aba-adda-0494dbb91fc2/cases", false)]
    [InlineData("/api/feed", false)]
    [InlineData(null, false)]
    public void Only_event_addresses_are_counted(string? path, bool counted)
        => Assert.Equal(counted, HostedEventErrorLog.IsEventAddress(path));

    [Fact]
    public void An_address_is_shown_with_its_ids_and_tokens_taken_out()
    {
        Assert.Equal("/api/organizations/{id}/events/{id}/bookings",
            HostedEventErrorLog.Normalise("/api/organizations/8fb7d9be-9de4-4aba-adda-0494dbb91fc2/events/40000002-0000-0000-0000-000000000002/bookings?night=1"));
        Assert.Equal("/api/public/event-passes/{token}.png",
            HostedEventErrorLog.Normalise("/api/public/event-passes/3f9c2a7be41d4c0a9b8e7d6c5b4a39281706f5e4.png"));
    }

    private sealed class Job(string name, bool fails) : IScheduledJob
    {
        public string Name => name;
        public Task RunAsync(CancellationToken ct) => fails ? throw new InvalidOperationException("The outbox is locked.\nat something") : Task.CompletedTask;
    }

    [Fact]
    public async Task The_scheduler_writes_down_every_run_and_every_failure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IScheduledJob>(new Job("hold-expiry", fails: false));
        services.AddSingleton<IScheduledJob>(new Job("mail-sender", fails: true));
        var ledger = new ScheduledJobLedger();
        var scheduler = new ScheduledWorkService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<ScheduledWorkService>.Instance, ledger);

        await scheduler.RunOnceAsync(default);
        await scheduler.RunOnceAsync(default);

        var jobs = ledger.Snapshot().ToDictionary(j => j.Job);
        Assert.Equal((2, 0, true, (string?)null), (jobs["hold-expiry"].Runs, jobs["hold-expiry"].Failures, jobs["hold-expiry"].LastSucceeded, jobs["hold-expiry"].LastError));
        Assert.Equal((2, 2, false, "The outbox is locked."), (jobs["mail-sender"].Runs, jobs["mail-sender"].Failures, jobs["mail-sender"].LastSucceeded, jobs["mail-sender"].LastError));

        await using var sqlite = await SeedAsync();
        Assert.Equal(2, (await ReadAsync(sqlite, ledger)).Jobs.Count);
    }
}
