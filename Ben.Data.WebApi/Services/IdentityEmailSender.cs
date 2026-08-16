using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
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
public sealed class IdentityEmailSender : IEmailSender<AppUser>
{
    private readonly IEmailService _email;
    private readonly ILogger<IdentityEmailSender> _logger;

    public IdentityEmailSender(IEmailService email, ILogger<IdentityEmailSender> logger)
    {
        _email  = email;
        _logger = logger;
    }

    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink)
        => SendAsync(email, "Confirm your email",
            $"""
             <p>Confirm your email address to finish setting up your account.</p>
             <p><a href="{WebUtility.HtmlEncode(confirmationLink)}">Confirm email</a></p>
             <p>If you did not create this account, you can ignore this message.</p>
             """,
            linkKind: "confirmation", link: confirmationLink);

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink)
        => SendAsync(email, "Reset your password",
            $"""
             <p>Use the link below to choose a new password.</p>
             <p><a href="{WebUtility.HtmlEncode(resetLink)}">Reset password</a></p>
             <p>If you did not request this, you can ignore this message — your password will not change.</p>
             """,
            linkKind: "password reset", link: resetLink);

    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
        => SendAsync(email, "Your password reset code",
            $"""
             <p>Your password reset code is <strong>{WebUtility.HtmlEncode(resetCode)}</strong>.</p>
             <p>If you did not request this, you can ignore this message.</p>
             """,
            linkKind: "password reset code", link: resetCode);

    private async Task SendAsync(string to, string subject, string htmlBody, string linkKind, string link)
    {
        try
        {
            await _email.SendAsync(to, subject, htmlBody);
        }
        catch (Exception ex)
        {
            // Identity treats a throwing sender as a failed request, which would report the
            // registration as failed after the account had already been created. So the send is
            // attempted unconditionally and a failure is logged rather than raised — including
            // while SMTP is unconfigured, which throws from SmtpEmailService by design.
            //
            // The link goes into the log so the flow stays completable without a mail provider.
            // That makes it exactly as private as the log, which is the reason this is a warning
            // and not something to leave switched on once mail is actually configured.
            _logger.LogWarning(ex,
                "Could not send the {LinkKind} message to {Recipient}. Use this instead: {Link}",
                linkKind, to, link);
        }
    }
}
