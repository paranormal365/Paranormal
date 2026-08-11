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
    public string FromName { get; set; } = "IsHaunted.com";
    public bool UseSsl { get; set; } = true;
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

    public SmtpEmailService(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SmtpEmailService.SendAsync called while unconfigured — callers must check IsConfigured first.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        var secureSocketOptions = _options.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_options.Host!, _options.Port, secureSocketOptions, ct); // non-null: IsConfigured already checked above
        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password ?? string.Empty, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
