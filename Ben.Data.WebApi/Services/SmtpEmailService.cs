using Ben.Data.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Ben.Data.WebApi.Services;

/// <summary>SMTP settings bound from the "Smtp" config section.</summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@example.com";
    /// <summary>
    /// Falls back to <see cref="Ben.Data.Common.SiteIdentity.Name"/> when unset, so the site's name
    /// is configured once rather than repeated here.
    /// </summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Kept for existing configuration: true means "use TLS", and <see cref="Security"/> decides
    /// which kind. False disables it entirely, which is only ever right for a local test relay.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// How TLS is established: <c>StartTls</c> upgrades a plain connection (ports 587 and 3325),
    /// <c>SslOnConnect</c> negotiates TLS before anything else (port 465).
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="UseSsl"/> alone could only ever mean StartTls, so a server
    /// that offers implicit TLS on 465 — as No-IP's does — could not be configured at all: the
    /// client would send EHLO in the clear and the server would drop it.
    /// </remarks>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
}

/// <summary>How the SMTP connection establishes TLS.</summary>
public enum SmtpSecurity
{
    /// <summary>Connect in the clear, then upgrade with STARTTLS. Ports 587, 3325.</summary>
    StartTls = 0,

    /// <summary>Negotiate TLS immediately on connect. Port 465.</summary>
    SslOnConnect = 1,
}

/// <summary>
/// <see cref="IEmailService"/> over SMTP via MailKit. <see cref="IsConfigured"/> is false whenever
/// no <c>Host</c> is set — the case in every environment today, since no SMTP credentials exist
/// yet (see the "Smtp" section commented out in appsettings). Callers must check
/// <see cref="IsConfigured"/> before relying on a send actually reaching anyone.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly SmtpOptions _options;
    private readonly Ben.Data.Common.SiteIdentity _site;

    public SmtpEmailService(IOptions<SmtpOptions> options, IOptions<Ben.Data.Common.SiteIdentity> site)
    {
        _options = options.Value;
        _site    = site.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => SendAsync(new EmailMessage(to, subject, htmlBody), ct);

    /// <summary>
    /// Builds the MIME message, attachments and all, and sends it.
    /// </summary>
    /// <remarks>
    /// A <see cref="BodyBuilder"/> rather than a bare <c>TextPart</c>: with no attachments it
    /// produces the same single HTML part this method has always sent, and with them it produces
    /// the multipart a calendar invitation needs. One path, so a mail with an attachment is not a
    /// second, less-travelled way of sending mail.
    /// </remarks>
    public async Task SendAsync(EmailMessage email, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SmtpEmailService.SendAsync called while unconfigured — callers must check IsConfigured first.");

        var message = BuildMessage(email);

        using var client = new SmtpClient();
        var secureSocketOptions = _options.UseSsl
            ? _options.Security switch
            {
                SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
                _                         => SecureSocketOptions.StartTls,
            }
            : SecureSocketOptions.None;
        await client.ConnectAsync(_options.Host!, _options.Port, secureSocketOptions, ct); // non-null: IsConfigured already checked above
        if (!string.IsNullOrEmpty(_options.User))
        {
            // Pin the SASL mechanism. The relay advertises PLAIN and LOGIN; left to itself MailKit
            // picks the strongest thing on offer, and on 2026-08-31 that choice was refused with
            // "5.7.8 authentication failed" for hours while a hand-written client pinned to PLAIN
            // went straight through. Nothing this service talks to needs anything else.
            KeepOnlyPlainAndLogin(client.AuthenticationMechanisms);
            await client.AuthenticateAsync(_options.User, _options.Password ?? string.Empty, ct);
        }
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>
    /// Leaves only PLAIN and LOGIN in the set of mechanisms MailKit may try. The set is the one
    /// the server advertised, so removing an entry is the only lever — there is no "prefer".
    /// </summary>
    /// <summary>The MIME message this service would send. Split out so a test can read it.</summary>
    internal MimeMessage BuildMessage(EmailMessage email)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName ?? _site.Name, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;

        // A reply-to the sender never claims to BE: the From stays the site's own address, which
        // is what the relay is authorised to send as, and only replies are steered elsewhere.
        // A malformed address is dropped rather than thrown — a guest should still get the mail.
        if (!string.IsNullOrWhiteSpace(email.ReplyTo)
            && MailboxAddress.TryParse(email.ReplyTo, out var replyTo))
            message.ReplyTo.Add(replyTo);

        var builder = new BodyBuilder { HtmlBody = email.HtmlBody };
        foreach (var attachment in email.Attachments ?? [])
        {
            builder.Attachments.Add(
                attachment.FileName, attachment.Content,
                ContentType.Parse(attachment.ContentType));
        }
        message.Body = builder.ToMessageBody();

        return message;
    }

    public static void KeepOnlyPlainAndLogin(ISet<string> advertised)
    {
        foreach (var mechanism in advertised.Where(m => m is not ("PLAIN" or "LOGIN")).ToList())
            advertised.Remove(mechanism);
    }
}
