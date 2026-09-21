using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The confirmation send when no mail server is configured. The configuration comment has
/// always promised that the link is logged so a local sign-up can be finished; the sender's
/// early return skipped that, and a fresh account could never be completed by its owner
/// (2026-09-04 walkthrough gap #1, seen again on 2026-09-06).
/// </summary>
public class IdentityEmailSenderTests
{
    private sealed class NoMailServer : IEmailService
    {
        public bool IsConfigured => false;
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP is not configured.");
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
    public async Task With_no_mail_server_the_link_is_logged_so_the_flow_can_still_be_finished()
    {
        var log    = new CapturingLogger();
        // No composer here any more. Applying a written template moved to the one place every
        // letter passes through — OutboxEmailService — so this sender hands its rows over on the
        // message and nothing about templates happens in it. The story this test tells, about a
        // missing MAIL server, is unchanged.
        var sender = new IdentityEmailSender(new NoMailServer(), log, Options.Create(new SiteIdentity()));
        var user   = new AppUser { Id = Guid.NewGuid(), Email = "new@example.com", UserName = "new@example.com" };
        const string link = "http://localhost:5078/confirm-email?userId=1&code=abc";

        var sent = await sender.TrySendConfirmationAsync(user, user.Email, link);

        Assert.False(sent);
        // The durable line (Error, kept by the database sink) never carries the link…
        var error = Assert.Single(log.Lines, l => l.Level == LogLevel.Error);
        Assert.DoesNotContain(link, error.Message);
        // …and the console line (Warning, below that sink) carries it, so the flow is completable.
        var warning = Assert.Single(log.Lines, l => l.Level == LogLevel.Warning);
        Assert.Contains("Use this instead", warning.Message);
        Assert.Contains(link, warning.Message);
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
