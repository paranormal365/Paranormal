using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The test send on Outgoing Mail: recorded under Recent letters, marked as a test, and able to
/// send any of the site's letters filled in with made-up details.
/// </summary>
/// <remarks>
/// Ben, 10/13/2026: the test said it was sent, nothing arrived, and nothing appeared in Recent
/// letters — the send went straight to the mail server and left no row behind.
/// </remarks>
public sealed class AdminMailTestSendTests
{
    private const string To = "ben@example.test";

    /// <summary>Remembers what reached the mail server, or refuses it.</summary>
    private sealed class FakeSender(bool configured = true, string? refusal = null) : IEmailService
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool IsConfigured => configured;

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => SendAsync(new EmailMessage(to, subject, htmlBody), ct);

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (refusal is not null) throw new InvalidOperationException(refusal);
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoAudit : IAuditLogService
    {
        public Task LogCreateAsync(string t, Guid i, object e, Guid u, string s) => Task.CompletedTask;
        public Task LogUpdateAsync(string t, Guid i, object b, object a, Guid u, string s) => Task.CompletedTask;
        public Task LogDeleteAsync(string t, Guid i, object e, Guid u, string s) => Task.CompletedTask;
        public Task LogReadAsync(string t, Guid i, object w, Guid u, string s) => Task.CompletedTask;
    }

    private static AdminMailDiagnosticsController Controller(SqliteTestDb sqlite, IEmailService sender)
        => new(sender,
               Options.Create(new SmtpOptions()),
               Options.Create(new SiteIdentity { BaseUrl = "https://ishaunted.test" }),
               sqlite.Factory, new NoAudit(),
               NullLogger<AdminMailDiagnosticsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static async Task<OutboxEmail> OnlyRowAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        return Assert.Single(await db.OutboxEmails.AsNoTracking().ToListAsync());
    }

    private static MailTestResult Ok(ActionResult<MailTestResult> answer)
        => Assert.IsType<MailTestResult>(Assert.IsType<OkObjectResult>(answer.Result).Value);

    [Fact]
    public async Task A_plain_test_is_recorded_as_accepted_and_says_it_is_a_test()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var sender = new FakeSender();

        var result = Ok(await Controller(sqlite, sender).SendTest(new MailTestRequest(To), default));

        Assert.True(result.Sent);
        var row = await OnlyRowAsync(sqlite);
        Assert.Equal(To, row.To);
        Assert.Equal("test", row.Kind);
        Assert.StartsWith("[Test]", row.Subject);
        Assert.NotNull(row.AcceptedBySmtpUtc);
        Assert.Null(row.FailedUtc);
        Assert.Contains("This is a test.", row.HtmlBody);

        // What went is what was recorded, so "Read it" shows the letter that arrived.
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(row.Subject, sent.Subject);
        Assert.Equal(row.HtmlBody, sent.HtmlBody);
    }

    [Fact]
    public async Task A_picked_letter_is_filled_in_with_made_up_details()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var sender = new FakeSender();

        var result = Ok(await Controller(sqlite, sender).SendTest(
            new MailTestRequest(To, MailKinds.ConfirmYourAddress.Key), default));

        Assert.True(result.Sent);
        Assert.False(result.UsedPublished);
        var row = await OnlyRowAsync(sqlite);
        Assert.Equal("test-confirm-your-address", row.Kind);
        Assert.Equal("[Test] Confirm your email", row.Subject);
        Assert.Contains("Marguerite Ashdown", row.HtmlBody);
        Assert.Contains("Confirm my email", row.HtmlBody);
        Assert.Contains("“Confirm your address” letter", row.HtmlBody);
        Assert.DoesNotContain("{", row.HtmlBody!.Replace("{{", ""));

        // One header, with the test box under it rather than above it.
        var html = row.HtmlBody!;
        Assert.Equal(html.IndexOf(MailHeader.IconFileName), html.LastIndexOf(MailHeader.IconFileName));
        Assert.True(html.IndexOf(MailHeader.IconFileName) < html.IndexOf("This is a test."));
    }

    [Fact]
    public async Task A_published_template_is_the_one_tested()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.EmailTemplates.Add(new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Kind = MailKinds.ResetYourPassword.Key,
                Subject = "Ben's reset for {AppUsers.FirstName}",
                BodyHtml = "<p>Ben wrote this.</p>{ResetButton}",
                PublishedUtc = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = Ok(await Controller(sqlite, new FakeSender()).SendTest(
            new MailTestRequest(To, MailKinds.ResetYourPassword.Key), default));

        Assert.True(result.UsedPublished);
        var row = await OnlyRowAsync(sqlite);
        Assert.Equal("[Test] Ben's reset for Marguerite", row.Subject);
        Assert.Contains("Ben wrote this.", row.HtmlBody);
    }

    [Fact]
    public async Task Every_letter_the_site_sends_can_be_tested()
    {
        foreach (var kind in MailKinds.All)
        {
            await using var sqlite = await SqliteTestDb.CreateAsync();
            var result = Ok(await Controller(sqlite, new FakeSender()).SendTest(
                new MailTestRequest(To, kind.Key), default));

            Assert.True(result.Sent, kind.Key);
            var row = await OnlyRowAsync(sqlite);
            Assert.True(row.Kind.Length <= 60, kind.Key);
            Assert.False(string.IsNullOrWhiteSpace(row.HtmlBody), kind.Key);
        }
    }

    [Fact]
    public async Task A_refusal_is_recorded_as_given_up_with_the_servers_words()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();

        var result = Ok(await Controller(sqlite, new FakeSender(refusal: "535 authentication failed"))
            .SendTest(new MailTestRequest(To), default));

        Assert.False(result.Sent);
        Assert.Equal("535 authentication failed", result.ServerSaid);
        var row = await OnlyRowAsync(sqlite);
        Assert.NotNull(row.FailedUtc);
        Assert.Null(row.AcceptedBySmtpUtc);
        Assert.Equal("535 authentication failed", row.LastError);
    }

    [Fact]
    public async Task With_mail_switched_off_the_test_is_still_recorded()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var sender = new FakeSender(configured: false);

        var result = Ok(await Controller(sqlite, sender).SendTest(new MailTestRequest(To), default));

        Assert.False(result.Sent);
        Assert.Empty(sender.Sent);
        Assert.NotNull((await OnlyRowAsync(sqlite)).FailedUtc);
    }

    [Fact]
    public async Task A_letter_the_site_does_not_send_is_refused_in_words()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();

        var answer = await Controller(sqlite, new FakeSender())
            .SendTest(new MailTestRequest(To, "no-such-letter"), default);

        Assert.IsType<BadRequestObjectResult>(answer.Result);
        await using var db = await sqlite.NewContextAsync();
        Assert.Empty(await db.OutboxEmails.ToListAsync());
    }
}
