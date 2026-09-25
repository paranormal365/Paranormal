namespace Ben.Data.Common.Interfaces;

/// <summary>
/// Abstracts email sending so the app can run with no email provider configured (dev — every
/// caller falls back to a copyable link) and swap in a real SMTP provider via config without
/// touching any calling code. Mirrors <see cref="IFileStorageService"/>'s split: interface here
/// in Common, implementation in the WebApi project.
/// </summary>
public interface IEmailService
{
    /// <summary>True when a real send target (SMTP host, etc.) is configured. Callers should
    /// check this before promising the recipient an email is on its way — when false, lean on
    /// a copyable link instead of a send-and-hope.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends an HTML email. Implementations should let exceptions propagate — callers that want
    /// "best effort" (e.g. an invite that should still succeed if the send fails) are responsible
    /// for catching and logging, matching this app's existing fire-and-forget audit-log convention.
    /// </summary>
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>
    /// Sends an HTML email that carries files, or asks for replies somewhere other than the
    /// site's own address.
    /// </summary>
    /// <remarks>
    /// <para>Added for item 233: a tour's sign-up mail carries the walk as a calendar file, so the
    /// guest's phone reminds them on the night. Nothing in this product needed an attachment
    /// before, which is why the interface had none.</para>
    ///
    /// <para><b>A default implementation, deliberately.</b> It drops the attachments and forwards
    /// to the plain send, so an implementation that has not been taught about them still delivers
    /// the words — including the three test doubles in this solution, which would otherwise all
    /// have had to change to add a feature none of them exercise. A real provider overrides it.</para>
    /// </remarks>
    Task SendAsync(EmailMessage message, CancellationToken ct = default)
        => SendAsync(message.To, message.Subject, message.HtmlBody, ct);
}

/// <summary>One email, with everything a caller may want to say about it.</summary>
/// <param name="ReplyTo">
/// Where a reply should go, when that is not the site. A tour guest hitting reply means to reach
/// the business that is walking them around a city at night, not our support address.
/// </param>
/// <param name="Kind">
/// Which of <see cref="Mail.MailKinds"/> this is, when the caller knows (item 246).
/// <para>Trailing and optional, so the twenty callers item 239a deliberately avoided editing stay
/// unedited until each is given its name. Where it is null the outbox still guesses from the
/// subject, which is what it has always done — a letter without a declared kind is grouped, but it
/// cannot carry a template, because a template keyed to a guess stops applying the day somebody
/// rewords a subject line.</para>
/// </param>
/// <param name="TimesShownInZone">
/// The clock the times in this letter are written on — "UTC", "CDT" — when that is not the reader's
/// own. The outbox turns it into a line in the footer (Ben, 2026-09-20).
/// <para>It exists because of a real hazard, not a tidiness wish. An event with no timezone of its
/// own is written out in UTC, and the formatter prints it with <b>no label at all</b>, so the
/// reader sees a bare time and reads it as local — which is how somebody arrives hours late. Unlike
/// a browser, a mail client tells us nothing about where the reader is, so the only honest thing
/// the letter can do is say which clock it means.</para>
/// <para>Trailing and optional. Null for the letters with no times in them, which is most of them.</para>
/// </param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    string? ReplyTo = null,
    string? Kind = null,
    string? TimesShownInZone = null,
    MailPayload? Payload = null);

/// <summary>
/// The rows a letter is carrying, for a template to read.
/// </summary>
/// <remarks>
/// <para><b>Why this travels on the message rather than living in each mailer.</b> A letter gains a
/// written template by consulting <c>MailComposer</c> before it sends, and for a long time exactly
/// three of thirty-five letters did — because doing it meant editing each mailer, and each edit
/// broke the tests that mock the sender. Carrying the rows here instead means the composing happens
/// in ONE place, where every letter already passes, and a mailer opts in by saying what it is
/// holding rather than by learning how templates work.</para>
///
/// <para>Both halves are optional. A letter that supplies nothing still gets a template — one
/// written with only <c>{SiteName}</c>, <c>{Date}</c> or plain words needs no rows at all.</para>
/// </remarks>
/// <param name="Tables">
/// Rows by table name, as a template addresses them: <c>{AppUsers.FirstName}</c> reads
/// <c>Tables["AppUsers"]["FirstName"]</c>.
/// </param>
/// <param name="Supplied">
/// Values only the sending code can know — a confirmation link, a ready-made button — keyed by the
/// token name the letter's kind declares.
/// </param>
public sealed record MailPayload(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? Tables = null,
    IReadOnlyDictionary<string, MailSuppliedValue>? Supplied = null);

/// <summary>One supplied value, and whether it is markup rather than words.</summary>
/// <remarks>
/// A named record rather than a tuple, because this crosses an assembly boundary and appears in a
/// public signature: <c>(string, bool)</c> tells a reader nothing at the call site.
/// </remarks>
public sealed record MailSuppliedValue(string Value, bool IsHtml = false);

/// <summary>One file travelling with an email.</summary>
/// <param name="ContentType">
/// The full type, parameters included — <c>text/calendar; method=PUBLISH</c> is what tells a mail
/// client to offer "add to calendar" rather than "download this file".
/// </param>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
