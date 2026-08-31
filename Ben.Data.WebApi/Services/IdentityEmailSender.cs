using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Net;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Delivers the confirmation, password-reset and change-email messages that ASP.NET Core Identity's
/// mapped endpoints send.
/// </summary>
/// <remarks>
/// <para>Identity ships a no-op <c>IEmailSender&lt;TUser&gt;</c> and registers it silently when
/// nothing else is provided. That is harmless while accounts are usable immediately, and becomes a
/// lockout the moment sign-in requires a confirmed address: the account is created, the
/// confirmation link is generated, and then discarded — with no error anywhere. Requiring
/// confirmation and registering a real sender therefore have to happen together.</para>
///
/// <para>The send is always attempted, including while SMTP is unconfigured — there is no
/// "is it worth trying" check in front of it. When it fails, the link is written to the log so the
/// flow can still be completed locally, mirroring what the invite and contact-verification flows
/// already do: fall back to a link a human can copy rather than a send-and-hope.</para>
/// </remarks>
/// <summary>
/// Reports whether a confirmation message actually went out.
/// </summary>
/// <remarks>
/// <see cref="IEmailSender{T}"/> returns a bare <c>Task</c>, so a caller cannot tell a send from a
/// silent failure — and this sender deliberately swallows exceptions, which made the caller's own
/// try/catch DEAD CODE: it could never run. That is why a failed sign-up confirmation left no trace
/// at all. This interface exists so the outcome is a value the caller can act on rather than an
/// exception nobody receives.
/// </remarks>
public interface IConfirmationMailer
{
    /// <summary>Sends the confirmation link. False means it did not leave this machine.</summary>
    Task<bool> TrySendConfirmationAsync(AppUser user, string email, string confirmationLink);
}

public sealed class IdentityEmailSender : IEmailSender<AppUser>, IConfirmationMailer
{
    private readonly IEmailService _email;
    private readonly ILogger<IdentityEmailSender> _logger;

    private readonly SiteIdentity _site;

    public IdentityEmailSender(
        IEmailService email, ILogger<IdentityEmailSender> logger, IOptions<SiteIdentity> site)
    {
        _email  = email;
        _logger = logger;
        _site   = site.Value;
    }

    /// <summary>The confirmation send, with its outcome reported rather than swallowed.</summary>
    public async Task<bool> TrySendConfirmationAsync(AppUser user, string email, string confirmationLink)
    {
        if (!_email.IsConfigured)
        {
            // Said plainly and at Error, because on a deployed site this is a broken sign-up: the
            // account exists and nobody will ever be told how to finish it. Locally it is expected
            // and harmless — the environment names itself in the log either way.
            _logger.LogError(
                "No confirmation message was sent to {Recipient}: SMTP is not configured, so the "
              + "account cannot be completed by its owner.", email);
            return false;
        }

        await SendConfirmationLinkAsync(user, email, confirmationLink);
        return _lastSendSucceeded;
    }

    /// <summary>Set by <c>SendAsync</c>; read immediately, on the same call, by the method above.</summary>
    private bool _lastSendSucceeded;

    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink)
        => SendAsync(email, "Confirm your email",
            BenEmailLayout.Wrap(_site, "Confirm your email",
                "<p>You're one click from finishing your account. Confirm this address and you "
              + "can sign in.</p>"
              + "<p>If you did not create this account, ignore this message and nothing happens.</p>",
                buttonText: "Confirm my email", buttonUrl: confirmationLink),
            linkKind: "confirmation", link: confirmationLink);

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink)
        => SendAsync(email, "Reset your password",
            BenEmailLayout.Wrap(_site, "Reset your password",
                "<p>Use the button below to choose a new password.</p>"
              + "<p>If you did not request this, ignore this message — your password will not "
              + "change.</p>",
                buttonText: "Reset password", buttonUrl: resetLink),
            linkKind: "password reset", link: resetLink);

    /// <summary>
    /// The reset email carries a finished link, not a bare code.
    /// </summary>
    /// <remarks>
    /// This used to send the code alone — with no reset page in the product, there was nowhere to
    /// paste it, which made the whole flow decorative (item 142's sixth write-only feature). The
    /// code still appears as text for anyone whose mail client mangles links, and the link
    /// degrades to a relative path when no public origin is configured.
    /// </remarks>
    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
    {
        var resetUrl = _site.AbsoluteUrl(
            $"/reset-password?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(resetCode)}");

        return SendAsync(email, "Reset your password",
            BenEmailLayout.Wrap(_site, "Reset your password",
                $"""
                 <p>Use the button below to choose a new password{(user.PasswordHash is null
                     ? ", or to add a password to an account that signs in with Microsoft" : "")}.</p>
                 <p>If the button does not work, go to the reset page and enter this code:
                    <strong>{WebUtility.HtmlEncode(resetCode)}</strong></p>
                 <p>If you did not request this, ignore this message — your password will not change.</p>
                 """,
                buttonText: "Reset password", buttonUrl: resetUrl),
            linkKind: "password reset", link: resetUrl);
    }

    private async Task SendAsync(string to, string subject, string htmlBody, string linkKind, string link)
    {
        _lastSendSucceeded = false;
        try
        {
            await _email.SendAsync(to, subject, htmlBody);
            _lastSendSucceeded = true;
        }
        catch (Exception ex)
        {
            // TWO log lines, deliberately, because they want different audiences and different
            // durability.
            //
            // The first is an ERROR and carries NO link. Error is the level the database sink
            // keeps, so this is the line that still exists tomorrow when somebody asks "did that
            // message ever go out". It deliberately omits the link: a confirmation link is a
            // credential, and the whole point of this line is that it gets STORED.
            _logger.LogError(ex,
                "Could not send the {LinkKind} message to {Recipient}. The account exists but its "
              + "owner has not been told how to complete it.", linkKind, to);

            // Identity treats a throwing sender as a failed request, which would report the
            // registration as failed after the account had already been created. So the send is
            // attempted unconditionally and a failure is logged rather than raised — including
            // while SMTP is unconfigured, which throws from SmtpEmailService by design.
            //
            // The link goes into the log so the flow stays completable without a mail provider.
            // That makes it exactly as private as the log, which is the reason this is a warning
            // and not something to leave switched on once mail is actually configured.
            // The second is a WARNING and carries the link, for completing the flow locally where
            // no mail server exists. Warning is below the database sink's threshold, so the token
            // stays in console output and never lands in a table.
            _logger.LogWarning(
                "Could not send the {LinkKind} message to {Recipient}. Use this instead: {Link}",
                linkKind, to, link);
        }
    }
}
