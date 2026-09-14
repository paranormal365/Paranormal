using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Writes a letter down instead of sending it, so that a sender can try until it goes (item 239).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"Should we create an email db table and a task to send them so we can
/// timestamp when they are created and when they are sent … basically in order to verify all
/// e-mails generated get sent and if it doesn't send on the first try it will try to send it on
/// the next try."</i></para>
///
/// <para><b>A decorator, not a rewrite.</b> <see cref="IEmailService"/> was already the right seam:
/// one interface, one real implementation, one method that matters. Registering this as the
/// interface and leaving <see cref="SmtpEmailService"/> registered as itself covered all twenty
/// callers — Identity's password mail, the tour and event mailers, the reminder job, every invite
/// door — without one of them being edited.</para>
///
/// <para><b>It never sends, and it never throws.</b> A caller that reaches this has already decided
/// a letter should exist; failing its request because a database write hiccupped would take the
/// caller's own operation down with it, and every one of them is wrapped in a swallow-and-log that
/// would hide it anyway. A write that fails is logged at Error, which is above the database sink's
/// threshold — the exact bar the 2026-08-31 failure fell under.</para>
///
/// <para><b>An unconfigured deployment queues anyway.</b> The old contract said callers must check
/// <see cref="IsConfigured"/> before promising anyone a letter, and that stays true for what a
/// screen says. But refusing to write the row would throw the letter away, and the far better
/// property is this: the day SMTP is switched on, everything queued goes out. So
/// <see cref="IsConfigured"/> still reports the truth about the machine, and the queue fills
/// regardless.</para>
/// </remarks>
public sealed class OutboxEmailService : IEmailService
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IEmailService _sender;
    private readonly ILogger<OutboxEmailService> _log;

    /// <summary>
    /// How much rendered HTML is kept.
    /// </summary>
    /// <remarks>
    /// Generous: a confirmation with its pass drawn in as a data URI is about eight kilobytes, and
    /// the biggest letter the site writes is nowhere near this. A body over the cap is stored
    /// truncated with a line saying so rather than refused, because the metadata — that this
    /// letter was meant, to whom, and whether it went — is worth more than the words.
    /// </remarks>
    public const int MaximumBodyBytes = 256 * 1024;

    /// <summary>
    /// The most attachment bytes one letter may carry into the table.
    /// </summary>
    /// <remarks>
    /// The files here are small by nature — a calendar entry is about a kilobyte, a pass image
    /// two. Anything past this is a mistake somewhere upstream, and storing it would put a row of
    /// that size in every backup, so the letter goes with its words and the reason the files did
    /// not is written on the row where somebody will see it.
    /// </remarks>
    public const int MaximumAttachmentBytes = 8 * 1024 * 1024;

    public OutboxEmailService(
        IDbContextFactory<BenDataContext> db,
        SmtpEmailService sender,
        ILogger<OutboxEmailService> log)
    { _db = db; _sender = sender; _log = log; }

    /// <summary>
    /// Whether this machine could actually send. Unchanged in meaning, and still worth asking.
    /// </summary>
    /// <remarks>
    /// A screen that says "we have emailed you" when nothing can send is lying, and several of them
    /// correctly offer a copyable link instead. What has changed is that the letter is queued
    /// either way, so the honest sentence is now "queued" rather than "not sent".
    /// </remarks>
    public bool IsConfigured => _sender.IsConfigured;

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => SendAsync(new EmailMessage(to, subject, htmlBody), ct);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _db.CreateDbContextAsync(ct);
            db.OutboxEmails.Add(Row(message, DateTime.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Error, not Warning. A Warning is what hid the failure this whole table exists to
            // stop hiding: the database sink only keeps Error and above, so a Warning here would
            // leave the same silence behind.
            _log.LogError(ex,
                "Could not queue the {Kind} letter to {To}; it will not be sent.",
                Kind(message.Subject), message.To);
        }
    }

    /// <summary>
    /// One letter as a row, with the two size rules applied.
    /// </summary>
    /// <remarks>Internal so a test can read what would be stored without a database.</remarks>
    internal static OutboxEmail Row(EmailMessage message, DateTime nowUtc)
    {
        var body = message.HtmlBody ?? string.Empty;
        var truncated = body.Length > MaximumBodyBytes;

        var row = new OutboxEmail
        {
            Id = Guid.NewGuid(),
            To = Clip(message.To, 320),
            Subject = Clip(message.Subject, 400),
            ReplyTo = message.ReplyTo is { Length: > 0 } r ? Clip(r, 320) : null,
            Kind = Kind(message.Subject),
            HtmlBody = truncated
                ? body[..MaximumBodyBytes] + "\n<!-- truncated: the letter was longer than the outbox keeps -->"
                : body,
            CreatedUtc = nowUtc,
            // Due at once. The sender's next pass is within five minutes, which is the delay a
            // letter now has and did not before — worth it for one that arrives at all.
            NextAttemptUtc = nowUtc,
        };

        if (truncated)
            row.LastError = "The body was longer than the outbox keeps and was stored truncated.";

        var attachments = message.Attachments ?? [];
        var total = attachments.Sum(a => (long)(a.Content?.Length ?? 0));

        if (total > MaximumAttachmentBytes)
        {
            // The words still go. The interface's own default already drops attachments for an
            // implementation that cannot carry them, so a letter without its calendar file is a
            // shape this product already understands — and one nobody can send is not.
            row.LastError =
                $"{attachments.Count} attachment(s) totalling {total / 1024} KB were too large to queue "
              + "and were dropped; the letter itself was kept.";
            return row;
        }

        foreach (var a in attachments)
        {
            row.Attachments.Add(new OutboxEmailAttachment
            {
                Id = Guid.NewGuid(),
                OutboxEmailId = row.Id,
                FileName = Clip(a.FileName, 260),
                ContentType = Clip(a.ContentType, 200),
                Content = a.Content,
                ByteCount = a.Content?.Length ?? 0,
            });
        }

        return row;
    }

    /// <summary>
    /// A short slug for what sort of letter this is, guessed from its subject.
    /// </summary>
    /// <remarks>
    /// <para>Guessed, because the alternative was adding a parameter to <see cref="IEmailService"/>
    /// and editing all twenty callers — which is the cost the decorator exists to avoid. A slug
    /// from the subject groups a screen well enough to answer "are the confirmations going out",
    /// and a caller that wants to be exact can say so later by widening the interface once, with
    /// this as the fallback.</para>
    ///
    /// <para>Deliberately coarse: lower-cased, punctuation dropped, first four words, hyphenated.
    /// "Your place at Halloween Lock-In is confirmed" becomes <c>your-place-at-halloween</c>,
    /// which is stable across events because the variable part is at the end of every subject the
    /// site writes.</para>
    /// </remarks>
    internal static string Kind(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "unknown";

        var words = new string(subject.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(4);

        var slug = string.Join('-', words);
        return slug.Length == 0 ? "unknown" : Clip(slug, 60);
    }

    private static string Clip(string value, int max)
        => value.Length <= max ? value : value[..max];
}
