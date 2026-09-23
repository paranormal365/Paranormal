using Ben.Data.Common;
using Ben.Data.Common.Mail;
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

    /// <summary>
    /// Tells somebody an account was made for them, and hands them the way to take it over.
    /// </summary>
    /// <param name="user">The account that now exists.</param>
    /// <param name="email">Where to write. The account's address, which nobody has proved yet.</param>
    /// <param name="setPasswordUrl">A reset link, which is how an owner chooses their own password.</param>
    /// <param name="madeBy">Who made it, as the reader would recognise them.</param>
    /// <returns>False when it did not leave this machine, which has been logged.</returns>
    Task<bool> TrySendAccountMadeForYouAsync(
        AppUser user, string email, string setPasswordUrl, string madeBy);
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

    /// <summary>
    /// The link and its button, named the way a template refers to them.
    /// </summary>
    /// <remarks>
    /// Both, always: an author who wants the site's own button uses <c>{ConfirmButton}</c> and one
    /// who wants their own wording wraps <c>{ConfirmUrl}</c> in whatever they like. Offering only
    /// the URL would mean everybody hand-rolls a button, and a hand-rolled one is exactly what
    /// renders differently in Outlook.
    /// </remarks>
    private static Dictionary<string, (string Value, bool IsHtml)> Links(
        string urlToken, string buttonToken, string buttonText, string url,
        params (string Name, string Value)[] extra)
    {
        var supplied = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            [urlToken] = (url, false),
            [buttonToken] = (BenEmailLayout.ActionButton(buttonText, url), true),
        };

        foreach (var (name, value) in extra) supplied[name] = (value, false);
        return supplied;
    }

    /// <summary>
    /// The reader's zone.
    /// </summary>
    /// <remarks>
    /// The site's, for now: a person has no time zone of their own yet (item 246 records that as
    /// still to do). Identity mail is the one place it matters least — a confirmation link does
    /// not depend on what time anybody thinks it is.
    /// </remarks>
    private static TimeZoneInfo SiteZone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        }
    }

    /// <summary>The confirmation send, with its outcome reported rather than swallowed.</summary>
    public async Task<bool> TrySendConfirmationAsync(AppUser user, string email, string confirmationLink)
    {
        if (!_email.IsConfigured)
        {
            // QUEUED ANYWAY, and still reported as not sent. The outbox takes a letter whether or
            // not SMTP is set up, so the day it is, this one goes; until then a site administrator
            // can read it — link and all — at /admin/mail, which is how a local sign-up is finished
            // and how the browser tests follow it. It used to be written to the log instead, and a
            // confirmation link is a credential: whoever holds it finishes somebody else's account
            // (NoCredentialsInLogsTests). The log line says what happened and never the link.
            //
            // False, because nothing left this machine: DateConfirmationSent stays empty and the
            // sign-up screen does not claim a letter is on its way.
            await SendConfirmationLinkAsync(user, email, confirmationLink);
            _logger.LogError(
                "No confirmation message was sent to {Recipient}: SMTP is not configured. It is "
              + "waiting in the outbox at /admin/mail, and goes when mail is set up.", email);
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
            linkKind: "confirmation",
            kind: MailKinds.ConfirmYourAddress.Key,
            supplied: Links("ConfirmUrl", "ConfirmButton", "Confirm my email", confirmationLink));

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink)
        => SendAsync(email, "Reset your password",
            BenEmailLayout.Wrap(_site, "Reset your password",
                "<p>Use the button below to choose a new password.</p>"
              + "<p>If you did not request this, ignore this message — your password will not "
              + "change.</p>",
                buttonText: "Reset password", buttonUrl: resetLink),
            linkKind: "password reset",
            kind: MailKinds.ResetYourPassword.Key,
            supplied: Links("ResetUrl", "ResetButton", "Reset password", resetLink));

    /// <summary>
    /// An account somebody else made, and the way for its owner to take it over.
    /// </summary>
    /// <remarks>
    /// <para><b>The gap this closes.</b> An account could be created for somebody — their address,
    /// a password a stranger typed — and nothing told them. They held an account on a live site
    /// they had never heard of, with a password they did not know and could not change, because
    /// changing it starts with knowing it exists. The letter was declared and sent by nothing for
    /// as long as the letter list has existed.</para>
    ///
    /// <para><b>It carries a reset link, not the password.</b> Mailing the password would make two
    /// people who know it rather than one, for ever, in a message that sits in an inbox. A reset
    /// link lets the owner choose their own, which is what takes the account out of the hands of
    /// whoever set it up.</para>
    ///
    /// <para><b>Sent whether or not the address is confirmed</b> — it is precisely the unconfirmed
    /// case that needs it, since a confirmed one belongs to somebody who has already been here.
    /// The address may not be theirs at all, which is why the letter never says what the password
    /// is and the link only ever grants the power to replace it.</para>
    /// </remarks>
    public async Task<bool> TrySendAccountMadeForYouAsync(
        AppUser user, string email, string setPasswordUrl, string madeBy)
    {
        var who = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(madeBy) ? "Somebody" : madeBy.Trim());

        await SendAsync(email, $"An account was made for you on {_site.Name}",
            BenEmailLayout.Wrap(_site, "An account was made for you",
                $"<p>{who} made an account on {WebUtility.HtmlEncode(_site.Name)} using this "
              + "address. You have not signed in to it.</p>"
              + "<p>Use the button below to choose your own password. Until you do, the only "
              + "password this account has is the one they typed.</p>"
              + "<p>If you were not expecting this, choosing a password is still the safest thing "
              + "to do — it takes the account out of anybody else's hands. Reply to this message "
              + "if you would rather it was removed.</p>",
                buttonText: "Choose my password", buttonUrl: setPasswordUrl),
            linkKind: "account handover",
            kind: MailKinds.AccountMadeForYou.Key,
            supplied: Links("SetPasswordUrl", "SetPasswordButton", "Choose my password", setPasswordUrl,
                            ("MadeBy", string.IsNullOrWhiteSpace(madeBy) ? "Somebody" : madeBy.Trim())),
            displayName: user.DisplayName);

        return _lastSendSucceeded;
    }

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
            linkKind: "password reset",
            kind: MailKinds.ResetYourPassword.Key,
            supplied: Links("ResetUrl", "ResetButton", "Reset password", resetUrl,
                            ("ResetCode", resetCode)));
    }

    private async Task SendAsync(string to, string subject, string htmlBody, string linkKind,
                                 string? kind = null,
                                 IReadOnlyDictionary<string, (string Value, bool IsHtml)>? supplied = null,
                                 string? displayName = null)
    {
        _lastSendSucceeded = false;
        try
        {
            // A written template replaces the words; the link and its button are handed in, so
            // whoever wrote it could put the button where they wanted it. A confirmation letter
            // with no link is refused when the template is saved, not discovered here.
            //
            // The composing itself happens ONCE, where every letter passes — see
            // OutboxEmailService.WithAnyTemplateAsync. This used to do it here as well, which was
            // fine while it was the only letter that did; doing both would now render the template
            // twice, and the second pass has no link to hand it.
            var payload = new MailPayload(
                Tables: new Dictionary<string, IReadOnlyDictionary<string, object?>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["AppUsers"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Email"] = to,
                        ["DisplayName"] = displayName,
                        ["UserName"] = displayName,
                    },
                },
                Supplied: supplied?.ToDictionary(
                    pair => pair.Key,
                    pair => new MailSuppliedValue(pair.Value.Value, pair.Value.IsHtml),
                    StringComparer.OrdinalIgnoreCase));

            await _email.SendAsync(new EmailMessage(to, subject, htmlBody, Kind: kind, Payload: payload));
            _lastSendSucceeded = true;
        }
        catch (Exception ex)
        {
            // One log line, and it carries NO link. Error is the level the database sink
            // keeps, so this is the line that still exists tomorrow when somebody asks "did that
            // message ever go out". It deliberately omits the link: a confirmation link is a
            // credential, and the whole point of this line is that it gets STORED.
            _logger.LogError(ex,
                "Could not send the {LinkKind} message to {Recipient}. The account exists but its "
              + "owner has not been told how to complete it.", linkKind, to);

            // Identity treats a throwing sender as a failed request, which would report the
            // registration as failed after the account had already been created. So the send is
            // attempted unconditionally and a failure is logged rather than raised.
            //
            // There used to be a second line here, at Warning, carrying the link "so the flow
            // stays completable without a mail provider". It is gone: the link is a credential,
            // and the fallback it provided now lives in the outbox, which takes every letter with
            // or without SMTP and shows it at /admin/mail (NoCredentialsInLogsTests). Reaching
            // this catch means the outbox itself refused, and then there is no letter to point to.
        }
    }
}
