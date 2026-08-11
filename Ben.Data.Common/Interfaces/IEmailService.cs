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
}
