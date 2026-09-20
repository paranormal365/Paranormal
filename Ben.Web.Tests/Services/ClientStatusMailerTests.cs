using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>The client's mail says what the site says, goes only to confirmed addresses, and never gets in the way (item 206).</summary>
public sealed class ClientStatusMailerTests
{
    [Fact]
    public void Every_status_has_a_label_and_a_sentence_of_its_own()
    {
        foreach (var status in Enum.GetValues<CaseStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(CaseStatusWording.Label(status)));
            Assert.DoesNotContain("status has changed", CaseStatusWording.ClientSentence(status));   // the fallback, never reached
        }
    }

    private static IDbContextFactory<BenDataContext> Factory()
        => new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (ClientStatusMailer Mailer, Mock<IEmailService> Email) Build(bool configured = true)
    {
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(configured);
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted", BaseUrl = "https://ishaunted.test" });
        return (new ClientStatusMailer(email.Object, site, NullLogger<ClientStatusMailer>.Instance), email);
    }

    private static async Task<(IDbContextFactory<BenDataContext> F, Case Case)> SeedAsync(bool confirmed = true, bool secondClientUnconfirmed = false)
    {
        var f = Factory();
        var c = new Case { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Title = "The Belmont house", CaseYear = 2026, OrgCaseNumber = 3, Status = CaseStatus.Accepted, City = "Nashville", DateCaseOpened = DateTime.UtcNow };
        await using (var db = await f.CreateDbContextAsync())
        {
            var client = new AppUser { Id = Guid.NewGuid(), UserName = "client@example.com", Email = "client@example.com", EmailConfirmed = confirmed };
            db.AppUsers.Add(client);
            db.Cases.Add(c);
            db.CaseClientAccesses.Add(new CaseClientAccess { Id = Guid.NewGuid(), CaseId = c.Id, AppUserId = client.Id, CreatedByAppUserId = client.Id });
            if (secondClientUnconfirmed)
            {
                var other = new AppUser { Id = Guid.NewGuid(), UserName = "unconfirmed@example.com", Email = "unconfirmed@example.com", EmailConfirmed = false };
                db.AppUsers.Add(other);
                db.CaseClientAccesses.Add(new CaseClientAccess { Id = Guid.NewGuid(), CaseId = c.Id, AppUserId = other.Id, CreatedByAppUserId = other.Id });
            }
            await db.SaveChangesAsync();
        }
        return (f, c);
    }

    [Fact]
    public async Task A_status_change_mails_the_client_the_sites_own_words()
    {
        var (f, c) = await SeedAsync();
        var (mailer, email) = Build();
        string? subject = null, body = null;
        email.Setup(e => e.SendAsync(It.Is<EmailMessage>(m => m.To == "client@example.com"), It.IsAny<CancellationToken>()))
             .Callback<EmailMessage, CancellationToken>((m, _) => { subject = m.Subject; body = m.HtmlBody; })
             .Returns(Task.CompletedTask);

        await using var db = await f.CreateDbContextAsync();
        await mailer.CaseStatusChangedAsync(db, c, CaseStatus.Proposed, default);

        Assert.Equal("Your case #2026-003 is now Accepted", subject);
        Assert.Contains(CaseStatusWording.ClientSentence(CaseStatus.Accepted), body);
        Assert.Contains($"/my-cases/{c.Id}", body);
    }

    [Fact]
    public async Task An_unchanged_status_sends_nothing()
    {
        var (f, c) = await SeedAsync();
        var (mailer, email) = Build();
        await using var db = await f.CreateDbContextAsync();
        await mailer.CaseStatusChangedAsync(db, c, c.Status, default);
        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Only_confirmed_addresses_are_mailed()
    {
        var (f, c) = await SeedAsync(secondClientUnconfirmed: true);
        var (mailer, email) = Build();
        await using var db = await f.CreateDbContextAsync();
        await mailer.CaseStatusChangedAsync(db, c, CaseStatus.Proposed, default);
        email.Verify(e => e.SendAsync(It.Is<EmailMessage>(m => m.To == "client@example.com"), It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.SendAsync(It.Is<EmailMessage>(m => m.To == "unconfirmed@example.com"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unconfigured_mail_is_a_quiet_no_op()
    {
        var (f, c) = await SeedAsync();
        var (mailer, email) = Build(configured: false);
        await using var db = await f.CreateDbContextAsync();
        await mailer.CaseStatusChangedAsync(db, c, CaseStatus.Proposed, default);
        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_failing_send_is_logged_and_never_thrown_into_the_change()
    {
        var (f, c) = await SeedAsync();
        var (mailer, email) = Build();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("SMTP said no"));
        await using var db = await f.CreateDbContextAsync();
        await mailer.CaseStatusChangedAsync(db, c, CaseStatus.Proposed, default);   // does not throw
    }

    [Fact]
    public async Task A_scheduled_visit_names_when_and_where()
    {
        var (f, c) = await SeedAsync();
        var (mailer, email) = Build();
        string? subject = null, body = null;
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .Callback<EmailMessage, CancellationToken>((m, _) => { subject = m.Subject; body = m.HtmlBody; })
             .Returns(Task.CompletedTask);
        var visit = new Investigation { Id = Guid.NewGuid(), CaseId = c.Id, OrganizationId = c.OrganizationId, Title = "First visit",
                                        ScheduledDateTime = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc), Location = "The cellar" };
        await using var db = await f.CreateDbContextAsync();
        await mailer.VisitScheduledAsync(db, c, visit, default);

        Assert.Equal("A visit is scheduled for your case: #2026-003", subject);
        Assert.Contains("Sunday, October 25, 2026 at 1:00 AM UTC", body);
        Assert.Contains("The cellar", body);
    }

    // ── The primary client, who has no access row (2026-09-17 audit) ────────────────────────
    //
    // CaseClientAccess rows exist only for CO-clients. The client who ASKED for the
    // investigation reaches their case through Case.ClientRequest.AppUserId, so a mailer that
    // asked only the access table never wrote to them — and for the common shape, a case with no
    // co-clients, it logged "no client to mail" and wrote to nobody at all.
    //
    // Every test above seeds an access row for "the client", which is why this passed throughout.
    // SubscriptionLapseJob had the same bug, fixed it, and wrote a paragraph about it.

    /// <summary>A case reached the way a real one is: through the request its client submitted.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> F, Case C, string Email)>
        SeedPrimaryOnlyAsync()
    {
        var f = Factory();
        var email = "primary@example.com";
        var clientId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var c = new Case
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Title = "The Belmont house",
            CaseYear = 2026, OrgCaseNumber = 4, Status = CaseStatus.Accepted, City = "Nashville",
            DateCaseOpened = DateTime.UtcNow,
            ClientRequestId = requestId,
        };

        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = clientId, UserName = email, Email = email, EmailConfirmed = true,
        });
        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = clientId, Status = ClientRequestStatus.Assigned,
            StreetAddress1 = "1 Elm", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        db.Cases.Add(c);
        // Deliberately NO CaseClientAccess row — this is the shape the mailer could not see.
        await db.SaveChangesAsync();

        return (f, c, email);
    }

    [Fact]
    public async Task A_status_change_mails_the_primary_client_who_has_no_access_row()
    {
        var (f, c, address) = await SeedPrimaryOnlyAsync();
        var (mailer, email) = Build();

        var sentTo = new List<string>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .Callback<EmailMessage, CancellationToken>((m, _) => sentTo.Add(m.To))
             .Returns(Task.CompletedTask);

        await using var db = await f.CreateDbContextAsync();
        await mailer.CaseStatusChangedAsync(db, c, CaseStatus.Proposed, default);

        Assert.Contains(address, sentTo);
    }

    [Fact]
    public async Task A_scheduled_visit_reaches_the_primary_client_too()
    {
        var (f, c, address) = await SeedPrimaryOnlyAsync();
        var (mailer, email) = Build();

        var sentTo = new List<string>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .Callback<EmailMessage, CancellationToken>((m, _) => sentTo.Add(m.To))
             .Returns(Task.CompletedTask);

        var visit = new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = c.OrganizationId, CaseId = c.Id,
            Title = "Night one", ScheduledDateTime = DateTime.UtcNow.AddDays(3),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        };

        await using var db = await f.CreateDbContextAsync();
        await mailer.VisitScheduledAsync(db, c, visit, default);

        Assert.Contains(address, sentTo);
    }

    /// <summary>
    /// And never twice, when the primary client also holds an access row — which happens, because
    /// nothing stops a client being invited to their own case.
    /// </summary>
    [Fact]
    public async Task The_primary_client_is_not_written_to_twice()
    {
        var (f, c, address) = await SeedPrimaryOnlyAsync();

        await using (var seed = await f.CreateDbContextAsync())
        {
            var clientId = (await seed.ClientRequests.FirstAsync()).AppUserId;
            seed.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = c.Id, AppUserId = clientId,
                CreatedByAppUserId = clientId,
            });
            await seed.SaveChangesAsync();
        }

        var (mailer, email) = Build();
        var sentTo = new List<string>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .Callback<EmailMessage, CancellationToken>((m, _) => sentTo.Add(m.To))
             .Returns(Task.CompletedTask);

        await using var db = await f.CreateDbContextAsync();
        await mailer.CaseStatusChangedAsync(db, c, CaseStatus.Proposed, default);

        Assert.Single(sentTo, address);
    }
}
