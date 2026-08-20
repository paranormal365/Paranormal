using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The event reminder job: who gets one, who does not, and — the point of the whole design —
/// that nobody gets two.
/// </summary>
/// <remarks>
/// <para>A note on what these prove and what they cannot. At runtime the guarantee against sending
/// twice has two layers: this job excludes anyone with a marker row, and a unique index on
/// (event, user) refuses a second marker outright. The in-memory provider these tests run against
/// does not enforce unique indexes, so what is exercised here is the <b>query</b> — which is the
/// layer that operates on every normal pass. The index is the backstop for two instances racing,
/// and it is asserted structurally rather than behaviourally, in
/// <see cref="The_marker_table_is_unique_on_event_and_user"/>.</para>
/// </remarks>
public sealed class EventReminderJobTests
{
    /// <summary>Records what it was asked to send, and can be told to fail.</summary>
    private sealed class FakeEmail : IEmailService
    {
        public bool IsConfigured { get; set; } = true;
        public bool ThrowOnSend { get; set; }
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (ThrowOnSend) throw new InvalidOperationException("SMTP refused.");
            Sent.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private static EventReminderJob Build(IDbContextFactory<BenDataContext> factory, IEmailService email)
        => new(factory, email,
               Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://ishaunted.com" }),
               NullLogger<EventReminderJob>.Instance);

    /// <summary>
    /// An organisation, a person, an event starting in twelve hours, and that person's RSVP.
    /// </summary>
    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid EventId, Guid UserId)>
        SeedAsync(RsvpStatus rsvp = RsvpStatus.Accepted, TimeSpan? startsIn = null, bool eventsEnabled = true)
    {
        var factory = TestDbFactory.Create();
        var userId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Users.Add(new AppUser
        {
            Id = userId, UserName = "attendee@test.com", NormalizedUserName = "ATTENDEE@TEST.COM",
            Email = "attendee@test.com", NormalizedEmail = "ATTENDEE@TEST.COM",
            DisplayName = "Pat Attendee", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = "test",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = eventId, OrganizationId = orgId, Title = "Night at the mill", UrlName = "night-at-the-mill",
            StartDateTime = DateTime.UtcNow.Add(startsIn ?? TimeSpan.FromHours(12)),
            EndDateTime   = DateTime.UtcNow.Add((startsIn ?? TimeSpan.FromHours(12)) + TimeSpan.FromHours(3)),
            Location = "The old mill", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = eventId, AppUserId = userId,
            RsvpStatus = rsvp, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        if (!eventsEnabled)
        {
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.FeatureEvents, Value = "false",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }

        await db.SaveChangesAsync();
        return (factory, eventId, userId);
    }

    // ── The guarantee ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Running_twice_sends_one_reminder()
    {
        var (factory, _, _) = await SeedAsync();
        var email = new FakeEmail();
        var job = Build(factory, email);

        await job.RunAsync(default);
        await job.RunAsync(default);

        Assert.Single(email.Sent);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.EventReminderSents.CountAsync());
    }

    /// <summary>
    /// The marker is written only after a successful send, so a failure is retried.
    /// </summary>
    /// <remarks>
    /// This is the ordering decision made explicit. Writing the marker first would make a failed
    /// send permanent silence; writing it after means the worst case is a duplicate, which is much
    /// the better of the two for somebody who is expected somewhere tomorrow.
    /// </remarks>
    [Fact]
    public async Task A_failed_send_leaves_no_marker_and_is_retried()
    {
        var (factory, _, _) = await SeedAsync();
        var email = new FakeEmail { ThrowOnSend = true };
        var job = Build(factory, email);

        await job.RunAsync(default);

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(0, await db.EventReminderSents.CountAsync());

        email.ThrowOnSend = false;
        await job.RunAsync(default);

        Assert.Single(email.Sent);
    }

    [Fact]
    public void The_marker_table_is_unique_on_event_and_user()
    {
        // Asserted against the model rather than by inserting twice, because the in-memory provider
        // these tests use does not enforce unique indexes. This index is what stops two instances
        // of the scheduler both sending; losing it would leave no error, only duplicate mail.
        using var db = TestDbFactory.Create().CreateDbContext();

        var index = db.Model.FindEntityType(typeof(EventReminderSent))!
            .GetIndexes()
            .SingleOrDefault(i => i.IsUnique
                && i.Properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                    .SequenceEqual(new[] { nameof(EventReminderSent.AppUserId), nameof(EventReminderSent.OrgCalendarEventId) }
                        .OrderBy(n => n, StringComparer.Ordinal)));

        Assert.True(index is not null,
            "EventReminderSent has no unique index across (OrgCalendarEventId, AppUserId). That index "
            + "is the idempotency mechanism, not a tidiness constraint — without it, two schedulers "
            + "running at once send the same person the same reminder twice.");
    }

    // ── Who gets one ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Someone_who_accepted_is_reminded()
    {
        var (factory, _, _) = await SeedAsync(RsvpStatus.Accepted);
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        var sent = Assert.Single(email.Sent);
        Assert.Equal("attendee@test.com", sent.To);
        Assert.Contains("Night at the mill", sent.Subject);
        Assert.Contains("Pat Attendee", sent.Body);
        Assert.Contains("The old mill", sent.Body);
        Assert.Contains("https://ishaunted.com/o/test/events/night-at-the-mill", sent.Body);
    }

    [Theory]
    [InlineData(RsvpStatus.Invited)]
    [InlineData(RsvpStatus.Declined)]
    [InlineData(RsvpStatus.Tentative)]
    public async Task Anyone_who_did_not_accept_is_left_alone(RsvpStatus rsvp)
    {
        // An invitation nobody answered is not a commitment, and mailing someone about a thing they
        // did not agree to attend is unsolicited mail. Tentative is the closest call — see the
        // job's own remarks.
        var (factory, _, _) = await SeedAsync(rsvp);
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task An_event_further_out_than_the_lead_time_waits()
    {
        var (factory, _, _) = await SeedAsync(startsIn: EventReminderJob.LeadTime + TimeSpan.FromHours(2));
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task An_event_that_has_already_started_is_not_a_reminder()
    {
        // Without a lower bound on the window, a past event stays permanently inside "starts within
        // 24 hours" and everyone who came gets told to come.
        var (factory, _, _) = await SeedAsync(startsIn: TimeSpan.FromHours(-1));
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    // ── When it does nothing at all ───────────────────────────────────────────

    [Fact]
    public async Task Turning_events_off_sitewide_stops_the_mail()
    {
        // A disabled section that carries on writing to people is worse than one that only hides
        // its pages — the switch has to reach the outbound mail too.
        var (factory, _, _) = await SeedAsync(eventsEnabled: false);
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task With_no_email_provider_configured_it_does_nothing()
    {
        var (factory, _, _) = await SeedAsync();
        var email = new FakeEmail { IsConfigured = false };

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.EventReminderSents.CountAsync());
    }
}
