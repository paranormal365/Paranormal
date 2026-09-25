using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The confirmation send when no mail server is configured.
/// </summary>
/// <remarks>
/// <para>A local sign-up has to be finishable without mail (2026-09-04 walkthrough gap #1, seen
/// again on 2026-09-06). That used to be done by logging the link. It is now done by queueing the
/// letter anyway — the outbox takes one whether or not SMTP is set up, and a site administrator reads
/// its link at /admin/mail — because the link is a credential and a log is no place for one
/// (NoCredentialsInLogsTests).</para>
/// </remarks>
public class IdentityEmailSenderTests
{
    /// <summary>
    /// What IdentityEmailSender is really handed: the outbox, on a machine with no SMTP host. It
    /// reports that nothing can be sent, and takes the letter anyway.
    /// </summary>
    /// <remarks>
    /// This used to throw, like the raw SMTP service does. Nothing in production gives this sender
    /// the raw service — Program.cs registers the outbox as IEmailService — so a fake that throws was
    /// testing a wiring that does not exist.
    /// </remarks>
    private sealed class OutboxWithNoMailServer : IEmailService
    {
        public readonly List<EmailMessage> Queued = [];
        public bool IsConfigured => false;

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => SendAsync(new EmailMessage(to, subject, htmlBody), ct);

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Queued.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<IdentityEmailSender>
    {
        public readonly List<(LogLevel Level, string Message)> Lines = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task With_no_mail_server_the_letter_is_queued_and_no_log_line_carries_the_link()
    {
        var log    = new CapturingLogger();
        var outbox = new OutboxWithNoMailServer();
        // No composer here any more. Applying a written template moved to the one place every
        // letter passes through — OutboxEmailService — so this sender hands its rows over on the
        // message and nothing about templates happens in it. The story this test tells, about a
        // missing MAIL server, is unchanged.
        var sender = new IdentityEmailSender(outbox, log, Options.Create(new SiteIdentity()));
        var user   = new AppUser { Id = Guid.NewGuid(), Email = "new@example.com", UserName = "new@example.com" };
        const string link = "http://localhost:5078/confirm-email?userId=1&code=abc";

        var sent = await sender.TrySendConfirmationAsync(user, user.Email, link);

        // Not sent — nothing left the machine, so the sign-up screen must not say a letter is coming…
        Assert.False(sent);

        // …but queued, with its link, so the flow can still be finished from /admin/mail.
        var letter = Assert.Single(outbox.Queued);
        Assert.Equal("new@example.com", letter.To);
        // Parts, not the whole link: the layout may write the & in the query as &amp;, which is the
        // same link to a mail client and a different string to Contains.
        Assert.Contains("/confirm-email?userId=1", letter.HtmlBody);
        Assert.Contains("code=abc", letter.HtmlBody);

        // And no line at ANY level carries it: the one line there is says where the letter waits.
        Assert.DoesNotContain(log.Lines, l => l.Message.Contains("code=abc", StringComparison.Ordinal));
        var error = Assert.Single(log.Lines, l => l.Level == LogLevel.Error);
        Assert.Contains("/admin/mail", error.Message);
    }

    /// <summary>No database, so the composer always falls back — see the note at its use.</summary>
    private sealed class NoDatabase
        : Microsoft.EntityFrameworkCore.IDbContextFactory<Ben.Data.Source.Context.BenDataContext>
    {
        public Ben.Data.Source.Context.BenDataContext CreateDbContext()
            => throw new InvalidOperationException("No database in this test.");

        public Task<Ben.Data.Source.Context.BenDataContext> CreateDbContextAsync(
            CancellationToken ct = default)
            => throw new InvalidOperationException("No database in this test.");
    }
}
