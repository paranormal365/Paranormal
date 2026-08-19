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

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SmtpEmailService.SendAsync called while unconfigured — callers must check IsConfigured first.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName ?? _site.Name, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

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
            await client.AuthenticateAsync(_options.User, _options.Password ?? string.Empty, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
