using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Scheduling;
using MailKit.Net.Smtp;
using System.Net.Sockets;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The outbox's rules: what a letter becomes on the way in, and what a failure does to it
/// (item 239).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"basically in order to verify all e-mails generated get sent and if it
/// doesn't send on the first try it will try to send it on the next try."</i></para>
///
/// <para>No database and no relay: every rule here is a decision about one row, and the decisions
/// are the part worth being certain about. The table itself is exercised by the guard tests
/// alongside these and by the running site.</para>
/// </remarks>
public sealed class MailOutboxTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc);

    private static EmailMessage Letter(
        string subject = "Your place at Halloween Lock-In is confirmed",
        string body = "<p>Hello</p>",
        IReadOnlyList<EmailAttachment>? attachments = null,
        string? replyTo = null)
        => new("guest@example.com", subject, body, attachments, replyTo);

    // ── what a letter becomes on the way in ──────────────────────────────────

    [Fact]
    public void A_letter_is_written_down_whole_and_is_due_at_once()
    {
        var row = OutboxEmailService.Row(Letter(replyTo: "stay@thomashouse.example"), Now);

        Assert.Equal("guest@example.com", row.To);
        Assert.Equal("<p>Hello</p>", row.HtmlBody);
        Assert.Equal("stay@thomashouse.example", row.ReplyTo);
        Assert.Equal(Now, row.CreatedUtc);
        // Due now: the delay a letter has gained is the sender's next pass, not a scheduled wait.
        Assert.Equal(Now, row.NextAttemptUtc);
        Assert.Equal(0, row.Attempts);
        Assert.True(row.IsWaiting);
        Assert.Null(row.AcceptedBySmtpUtc);
    }

    [Fact]
    public void Attachments_travel_with_it_and_remember_their_size()
    {
        var row = OutboxEmailService.Row(
            Letter(attachments: [new EmailAttachment("event.ics", "text/calendar; method=PUBLISH", [1, 2, 3])]),
            Now);

        var file = Assert.Single(row.Attachments);
        Assert.Equal("event.ics", file.FileName);
        // The full type, parameters included — that is what makes a mail client offer "add to
        // calendar" rather than "download this file".
        Assert.Equal("text/calendar; method=PUBLISH", file.ContentType);
        Assert.Equal(3, file.ByteCount);
        Assert.Equal([1, 2, 3], file.Content);
    }

    [Fact]
    public void A_body_too_long_to_keep_is_stored_truncated_and_says_so()
    {
        // The metadata — that this letter was meant, to whom, and whether it went — is worth more
        // than the words, so an oversized body never costs us the row.
        var row = OutboxEmailService.Row(
            Letter(body: new string('x', OutboxEmailService.MaximumBodyBytes + 500)), Now);

        Assert.NotNull(row.HtmlBody);
        Assert.Contains("truncated", row.HtmlBody!);
        Assert.Contains("truncated", row.LastError);
        Assert.True(row.IsWaiting);
    }

    [Fact]
    public void Attachments_too_large_to_keep_are_dropped_and_the_words_still_go()
    {
        // The interface's own default already drops attachments for an implementation that cannot
        // carry them, so a letter without its calendar file is a shape this product understands.
        // One nobody can send is not.
        var huge = new byte[OutboxEmailService.MaximumAttachmentBytes + 1];
        var row = OutboxEmailService.Row(
            Letter(attachments: [new EmailAttachment("video.mp4", "video/mp4", huge)]), Now);

        Assert.Empty(row.Attachments);
        Assert.Contains("too large to queue", row.LastError);
        Assert.Equal("<p>Hello</p>", row.HtmlBody);
        Assert.True(row.IsWaiting);
    }

    [Theory]
    [InlineData("Your place at Halloween Lock-In is confirmed", "your-place-at-halloween")]
    [InlineData("Your place at The October Weekend is confirmed", "your-place-at-the")]
    [InlineData("Confirm your email address", "confirm-your-email-address")]
    [InlineData("   ", "unknown")]
    [InlineData(null, "unknown")]
    public void The_kind_is_stable_across_events_because_the_variable_part_comes_last(
        string? subject, string expected)
        // Grouping only has to be good enough to answer "are the confirmations going out". Every
        // subject the site writes puts the event's name at the end, so the first four words are
        // the same letter every time.
        => Assert.Equal(expected, OutboxEmailService.Kind(subject));

    // ── what a failure does to it ────────────────────────────────────────────

    [Fact]
    public void A_dropped_connection_is_tried_again_and_the_waits_get_longer()
    {
        var row = OutboxEmailService.Row(Letter(), Now);
        var seen = new List<TimeSpan>();

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            row.Attempts = attempt;
            MailSenderJob.Fail(row, new IOException("the connection was reset"), Now);
            Assert.Null(row.FailedUtc);
            seen.Add(row.NextAttemptUtc - Now);
        }

        Assert.Equal(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
             TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24)],
            seen);

        // Six attempts is about thirty-one hours of trying; the seventh gives up.
        row.Attempts = 7;
        MailSenderJob.Fail(row, new IOException("the connection was reset"), Now);
        Assert.Equal(Now, row.FailedUtc);
    }

    [Fact]
    public void A_mailbox_that_does_not_exist_is_given_up_on_at_once()
    {
        // Retrying a 5xx six times fills the queue with addresses that will never work, and a
        // queue full of those is one nobody reads.
        var row = OutboxEmailService.Row(Letter(), Now);
        row.Attempts = 1;

        MailSenderJob.Fail(row,
            new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted,
                                     SmtpStatusCode.MailboxUnavailable, "No such user here"),
            Now);

        Assert.Equal(Now, row.FailedUtc);
        Assert.Contains("No such user", row.LastError);
        Assert.False(row.IsWaiting);
    }

    [Fact]
    public void A_failure_always_lets_go_of_its_claim()
    {
        // Otherwise a row that failed stays claimed and the stale-claim window is the only thing
        // that ever frees it — turning a one-minute backoff into a ten-minute one.
        var row = OutboxEmailService.Row(Letter(), Now);
        row.Attempts = 1;
        row.ClaimedUtc = Now;
        row.ClaimedBy = "somewhere:1";

        MailSenderJob.Fail(row, new IOException("nope"), Now);

        Assert.Null(row.ClaimedUtc);
        Assert.Null(row.ClaimedBy);
    }

    [Fact]
    public void A_password_the_server_refuses_is_transient_because_somebody_will_fix_it()
    {
        // MailKit raises an authentication failure as its own exception rather than an SMTP
        // command, so it never looks like a 5xx here — and that is the behaviour worth having: a
        // misconfigured password is fixed on the server, and the queue should flush when it is.
        Assert.False(MailSenderJob.IsPermanent(
            new System.Security.Authentication.AuthenticationException("5.7.8 authentication failed")));
        Assert.False(MailSenderJob.IsPermanent(new SocketException()));
        Assert.False(MailSenderJob.IsPermanent(new TimeoutException()));
    }

    [Fact]
    public void A_four_hundred_from_the_relay_is_transient_and_a_five_hundred_is_not()
    {
        Assert.False(MailSenderJob.IsPermanent(new SmtpCommandException(
            SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ServiceNotAvailable, "try later")));
        Assert.True(MailSenderJob.IsPermanent(new SmtpCommandException(
            SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxNameNotAllowed, "no")));
    }
}
