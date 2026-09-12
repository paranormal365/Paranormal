using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Sends what the outbox holds, retries what did not go, and clears out old bodies (item 239).
/// </summary>
/// <remarks>
/// <para>The other half of <see cref="OutboxEmailService"/>: callers write rows, this posts them.
/// Ben, 2026-09-12: <i>"if it doesn't send on the first try it will try to send it on the next
/// try."</i></para>
///
/// <para><b>Claim, send, mark — in that order, on purpose.</b> A crash in the window between the
/// mail server accepting a letter and this row being marked will send it twice. That is the right
/// way round to fail: a duplicate is an annoyance a person forgives, and a letter nobody sent is
/// the bug this table exists to end.</para>
///
/// <para><b>A dead mailbox and a dropped connection are not the same failure.</b> Retrying a 5xx
/// six times fills the queue with addresses that will never work, and a queue full of those is one
/// nobody reads. A permanent refusal gives up at once, keeping the server's own words; everything
/// else backs off.</para>
/// </remarks>
public sealed class MailSenderJob : IScheduledJob
{
    public string Name => "mail-sender";

    /// <summary>
    /// How many letters one pass will try.
    /// </summary>
    /// <remarks>
    /// A cap rather than "everything due", because a digest job can queue hundreds at once and a
    /// relay that is asked for hundreds in a burst throttles or blacklists. Twenty-five every five
    /// minutes is three hundred an hour, which is far more than this site writes and gentle enough
    /// that no provider notices.
    /// </remarks>
    public const int BatchSize = 25;

    /// <summary>
    /// How long before a claim is taken back.
    /// </summary>
    /// <remarks>
    /// A process that died mid-send leaves a row claimed for ever otherwise. Ten minutes is longer
    /// than any send can take and shorter than anybody would wait for a confirmation.
    /// </remarks>
    public static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(10);

    /// <summary>How long a body is kept after the letter went.</summary>
    /// <remarks>
    /// A body is personal data, and for a hosted event it carries the pass QR — a working door
    /// credential. The metadata beside it stays for ever, so "did it go" is answerable a year later
    /// and "what did it say" for a month.
    /// </remarks>
    public static readonly TimeSpan BodyLife = TimeSpan.FromDays(30);

    /// <summary>How often the scrub actually runs; the sender itself runs every pass.</summary>
    /// <remarks>
    /// Static, like <c>MediaRetentionJob</c>'s: a job is resolved fresh from a scope every pass, and
    /// losing this on restart costs one extra sweep of a query that finds nothing.
    /// </remarks>
    public static readonly TimeSpan MinimumSweepAge = TimeSpan.FromHours(6);
    private static DateTime _lastSweptUtc = DateTime.MinValue;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly SmtpEmailService _smtp;
    private readonly ILogger<MailSenderJob> _log;
    private readonly string _claimant = $"{Environment.MachineName}:{Environment.ProcessId}";

    public MailSenderJob(
        IDbContextFactory<BenDataContext> db, SmtpEmailService smtp, ILogger<MailSenderJob> log)
    { _db = db; _smtp = smtp; _log = log; }

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Nothing is claimed on a machine that cannot post, so a deployment with no relay quietly
        // accumulates a queue and flushes it the moment one is configured — which is the whole
        // reason an unconfigured deployment still writes rows.
        if (_smtp.IsConfigured) await SendDueAsync(now, ct);
        await ScrubAsync(now, ct);
    }

    private async Task SendDueAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var takeBack = now - StaleClaim;
        var due = await db.OutboxEmails
            .Where(e => e.AcceptedBySmtpUtc == null
                     && e.FailedUtc == null
                     && e.NextAttemptUtc <= now
                     && (e.ClaimedUtc == null || e.ClaimedUtc < takeBack))
            .OrderBy(e => e.NextAttemptUtc)
            .Select(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var id in due)
        {
            if (ct.IsCancellationRequested) return;

            // The claim IS the lock, and it is one statement so two senders cannot both win it.
            // The same predicate as the query above, re-checked, because the row may have been
            // taken between reading the ids and getting here.
            var claimed = await db.OutboxEmails
                .Where(e => e.Id == id
                         && e.AcceptedBySmtpUtc == null
                         && e.FailedUtc == null
                         && (e.ClaimedUtc == null || e.ClaimedUtc < takeBack))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(e => e.ClaimedUtc, DateTime.UtcNow)
                    .SetProperty(e => e.ClaimedBy, _claimant), ct);
            if (claimed != 1) continue;

            var row = await db.OutboxEmails
                .Include(e => e.Attachments)
                .FirstOrDefaultAsync(e => e.Id == id, ct);
            if (row is null) continue;

            row.Attempts++;

            try
            {
                await _smtp.SendAsync(new EmailMessage(
                    row.To, row.Subject, row.HtmlBody ?? string.Empty,
                    row.Attachments
                        .Where(a => a.Content is not null)
                        .Select(a => new EmailAttachment(a.FileName, a.ContentType, a.Content!))
                        .ToList(),
                    row.ReplyTo), ct);

                row.AcceptedBySmtpUtc = DateTime.UtcNow;
                row.LastError = null;
                row.ClaimedUtc = null;
                row.ClaimedBy = null;
            }
            catch (Exception ex)
            {
                Fail(row, ex, DateTime.UtcNow);
                _log.LogWarning(ex,
                    "The {Kind} letter to {To} did not go on attempt {Attempt}. {Outcome}",
                    row.Kind, row.To, row.Attempts,
                    row.FailedUtc is null ? $"Next try {row.NextAttemptUtc:u}." : "Given up on.");
            }

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Records a failed attempt: gives up, or sets when to try again.
    /// </summary>
    /// <remarks>Internal and static so the rules can be tested without a relay or a database.</remarks>
    internal static void Fail(Ben.Data.Source.Entities.OutboxEmail row, Exception ex, DateTime nowUtc)
    {
        row.ClaimedUtc = null;
        row.ClaimedBy = null;
        row.LastError = Clip(ex.Message, 1000);

        if (IsPermanent(ex) || Backoff(row.Attempts) is not { } wait)
        {
            row.FailedUtc = nowUtc;
            return;
        }

        row.NextAttemptUtc = nowUtc + wait;
    }

    /// <summary>
    /// Whether this failure will still be a failure in an hour.
    /// </summary>
    /// <remarks>
    /// <para>A 5xx from the relay is about the message or the mailbox — no such user, refused for
    /// its content — and asking again changes nothing.</para>
    ///
    /// <para><b>Everything else is transient, including a failure to authenticate.</b> MailKit
    /// raises that as an <c>AuthenticationException</c> rather than an SMTP command, so it does not
    /// look like a 5xx here, and that is the behaviour worth having: a misconfigured password is
    /// fixed on the server, and the queue should flush when it is rather than have given up.</para>
    /// </remarks>
    internal static bool IsPermanent(Exception ex)
        => ex is SmtpCommandException smtp && (int)smtp.StatusCode is >= 500 and < 600;

    /// <summary>
    /// How long to wait after <paramref name="attempts"/> failures, or null to give up.
    /// </summary>
    /// <remarks>
    /// One minute, five, fifteen, an hour, six hours, a day — about thirty-one hours of trying
    /// across six attempts. Long enough to ride out a relay that is down for a working day, short
    /// enough at the start that a blip costs a guest a minute rather than an evening.
    /// </remarks>
    internal static TimeSpan? Backoff(int attempts) => attempts switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromHours(1),
        5 => TimeSpan.FromHours(6),
        6 => TimeSpan.FromHours(24),
        _ => null,
    };

    /// <summary>
    /// Clears the words out of letters that went a month ago, keeping the fact that they went.
    /// </summary>
    private async Task ScrubAsync(DateTime now, CancellationToken ct)
    {
        if (now - _lastSweptUtc < MinimumSweepAge) return;
        _lastSweptUtc = now;

        await using var db = await _db.CreateDbContextAsync(ct);
        var before = now - BodyLife;

        // Attachments first: their rows hang off the letter, and clearing the letter's own body is
        // what marks the pair as done.
        var scrubbed = await db.OutboxEmailAttachments
            .Where(a => a.Content != null
                     && a.OutboxEmail.AcceptedBySmtpUtc != null
                     && a.OutboxEmail.AcceptedBySmtpUtc < before)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Content, (byte[]?)null), ct);

        var cleared = await db.OutboxEmails
            .Where(e => e.BodyScrubbedUtc == null
                     && e.AcceptedBySmtpUtc != null
                     && e.AcceptedBySmtpUtc < before)
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.HtmlBody, (string?)null)
                .SetProperty(e => e.BodyScrubbedUtc, now), ct);

        if (cleared > 0 || scrubbed > 0)
            _log.LogInformation(
                "Cleared the words out of {Letters} sent letter(s) and {Files} attachment(s) older "
              + "than {Days} days. What was sent, to whom and when is kept.",
                cleared, scrubbed, BodyLife.TotalDays);
    }

    private static string Clip(string value, int max)
        => value.Length <= max ? value : value[..max];
}
