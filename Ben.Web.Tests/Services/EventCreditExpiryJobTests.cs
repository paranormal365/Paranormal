using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The thirty-day warning on an event credit (item 235, phase 1B.4).
/// </summary>
/// <remarks>
/// <para>Ben settled the warning on 2026-09-11 because the alternative is a support ticket about
/// ninety-nine dollars that expired last week, and that is a conversation nobody wins. So the
/// rules here are all about somebody's money: warned once, warned in time, never warned about a
/// credit they already used, and never left unwarned because the deployment has no mail server.
/// </para>
/// </remarks>
public sealed class EventCreditExpiryJobTests
{
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

    private static readonly Guid OrgId  = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static EventCreditExpiryJob Build(IDbContextFactory<BenDataContext> factory, IEmailService email)
        => new(factory, email, new PlatformMessageService(factory),
               Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://ishaunted.com" }),
               NullLogger<EventCreditExpiryJob>.Instance);

    /// <summary>A group whose creator can be written to, holding one credit.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(
        TimeSpan expiresIn, DateTime? spent = null, DateTime? refunded = null,
        DateTime? alreadyWarned = null, string? email = "owner@test.com")
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.Users.Add(new AppUser
        {
            Id = UserId, UserName = "owner@test.com", NormalizedUserName = "OWNER@TEST.COM",
            Email = email, NormalizedEmail = email?.ToUpperInvariant(),
            DisplayName = "Pat Owner", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });
        db.EventCredits.Add(new EventCredit
        {
            Id = Guid.NewGuid(),
            OwnerOrganizationId = OrgId,
            PriceAtPurchase = 99m,
            PurchasedUtc = DateTime.UtcNow.AddMonths(-11),
            ExpiresUtc = DateTime.UtcNow.Add(expiresIn),
            ExpiryWarningSentUtc = alreadyWarned,
            SpentUtc = spent,
            RefundedUtc = refunded,
            DateCreated = DateTime.UtcNow.AddMonths(-11),
            CreatedByAppUserId = UserId,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    // ── Who is warned, and when ───────────────────────────────────────────────

    [Fact]
    public async Task A_credit_inside_the_window_warns_its_group()
    {
        var factory = await SeedAsync(TimeSpan.FromDays(20));
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        var sent = Assert.Single(email.Sent);
        Assert.Equal("owner@test.com", sent.To);
        Assert.Contains("event credit", sent.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The Thomas House", sent.Body);
        // The link is the point of the mail: a warning with nowhere to go is an anxiety, not a
        // notice.
        Assert.Contains($"https://ishaunted.com/organizations/{OrgId}/billing", sent.Body);
    }

    [Fact]
    public async Task A_credit_beyond_the_window_is_left_alone()
    {
        // Thirty days is the lead. A credit with eleven months left is not news, and warning about
        // it would train people to ignore the one that matters.
        var factory = await SeedAsync(EventCredits.WarningLead + TimeSpan.FromDays(5));
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Running_twice_warns_once()
    {
        var factory = await SeedAsync(TimeSpan.FromDays(10));
        var email = new FakeEmail();
        var job = Build(factory, email);

        await job.RunAsync(default);
        await job.RunAsync(default);

        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task A_spent_credit_is_never_warned_about()
    {
        // It is already doing its job. Telling somebody the credit they used last week is about to
        // expire reads as "we lost your event".
        var factory = await SeedAsync(TimeSpan.FromDays(10), spent: DateTime.UtcNow.AddDays(-3));
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_refunded_credit_is_never_warned_about()
    {
        var factory = await SeedAsync(TimeSpan.FromDays(10), refunded: DateTime.UtcNow.AddDays(-3));
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_credit_that_already_lapsed_is_not_warned_about()
    {
        // The window has a lower bound as well as an upper one. Warning that something expired
        // yesterday is exactly the message this job exists to make unnecessary.
        var factory = await SeedAsync(TimeSpan.FromDays(-1));
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);
    }

    // ── Reaching them ─────────────────────────────────────────────────────────

    [Fact]
    public async Task With_no_mail_server_the_warning_goes_to_the_bell()
    {
        // The platform message is not a nicety. A credit somebody paid for must not lapse
        // unannounced because this deployment has no SMTP host — and on a dev machine that is
        // every deployment.
        var factory = await SeedAsync(TimeSpan.FromDays(10));
        var email = new FakeEmail { IsConfigured = false };

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);

        await using var db = await factory.CreateDbContextAsync();
        var message = await db.UserMessages.SingleAsync();
        Assert.Contains("event credit", message.MessageSubject, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == UserId));
    }

    [Fact]
    public async Task A_holder_with_no_address_gets_the_bell_instead()
    {
        var factory = await SeedAsync(TimeSpan.FromDays(10), email: null);
        var email = new FakeEmail();

        await Build(factory, email).RunAsync(default);

        Assert.Empty(email.Sent);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == UserId));
    }

    [Fact]
    public async Task A_failed_send_leaves_no_marker_and_is_retried()
    {
        // Same ordering decision as the event reminder: the marker is written after the send, so
        // the worst case is a duplicate warning rather than permanent silence about money.
        var factory = await SeedAsync(TimeSpan.FromDays(10));
        var email = new FakeEmail { ThrowOnSend = true };
        var job = Build(factory, email);

        await job.RunAsync(default);

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Null(await db.EventCredits.Select(c => c.ExpiryWarningSentUtc).SingleAsync());

        email.ThrowOnSend = false;
        await job.RunAsync(default);

        Assert.Single(email.Sent);
        await using (var db = await factory.CreateDbContextAsync())
            Assert.NotNull(await db.EventCredits.Select(c => c.ExpiryWarningSentUtc).SingleAsync());
    }
}
