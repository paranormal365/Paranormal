using Ben.Data.Common;
using Ben.Data.Common.Helpers;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Text;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Creates a password account that must confirm its email, and sends the confirmation.
/// </summary>
/// <remarks>
/// <para>Extracted from <c>AccountRegistrationController</c> (site evaluation 2026-09-06, phase 1)
/// because a second caller arrived: the investigation-request wizard, which creates the account
/// from the request itself. Both must produce exactly the same account — same handle rules, same
/// confirmation token type, same landing route, same <c>DateConfirmationSent</c> stamp — so the
/// steps live in one place and the two controllers only differ in what they say back.</para>
///
/// <para><b>The link may carry a return target.</b> Somebody who signed up by asking for help
/// should land on their request after confirming, not on the home page; a local path is threaded
/// through the confirmation link as <c>returnUrl</c> and the website's confirm page hands it to
/// sign-in. Only a local path is ever accepted, on both ends.</para>
///
/// <para><b>The existing-account note</b> is here too, for the same reason: the two anonymous
/// entry points both refuse to reveal whether an address is registered, and both owe the real
/// account holder a word about it. The wording differs by what the stranger was doing.</para>
/// </remarks>
public sealed class AccountCreationService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly UserHandleService _handles;
    private readonly IConfirmationMailer _mailer;
    private readonly IEmailService _email;
    private readonly SiteIdentity _site;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountCreationService> _logger;

    public AccountCreationService(
        UserManager<AppUser> userManager,
        UserHandleService handles,
        IConfirmationMailer mailer,
        IEmailService email,
        IOptions<SiteIdentity> site,
        IConfiguration configuration,
        ILogger<AccountCreationService> logger)
    {
        _userManager   = userManager;
        _handles       = handles;
        _mailer        = mailer;
        _email         = email;
        _site          = site.Value;
        _configuration = configuration;
        _logger        = logger;
    }

    /// <summary>What a caller must know about a creation attempt.</summary>
    /// <param name="User">The account, when one was made.</param>
    /// <param name="Error">Why not, in a sentence for the person, when it was not.</param>
    /// <param name="HandleClash">True when the refusal is the chosen @name being taken.</param>
    public sealed record Outcome(AppUser? User, string? Error, bool HandleClash = false)
    {
        public bool Succeeded => User is not null;
    }

    /// <summary>
    /// Creates an unconfirmed account. Does not send anything — see <see cref="SendConfirmationAsync"/>.
    /// </summary>
    /// <remarks>
    /// The caller has already checked that no account holds <paramref name="email"/>; this does
    /// not re-check, so the unique index is the guard against a race and reports through
    /// <see cref="Outcome.Error"/>. <paramref name="handle"/> null means "allocate one from the
    /// name" — the path for people who never chose an @name, which is every request-wizard
    /// sign-up. A chosen handle is normalised and its validity is the caller's business to have
    /// reported precisely before getting here.
    /// </remarks>
    public async Task<Outcome> CreateUnconfirmedAsync(
        string email, string password, string displayName,
        string? firstName, string? lastName, string? handle, CancellationToken ct)
    {
        var chosen = handle is null
            ? await _handles.AllocateAsync(displayName, email, ct)
            : UserHandle.Normalize(handle);

        var user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Email              = email,
            UserName           = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName        = displayName,
            FirstName          = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim(),
            LastName           = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim(),
            Handle             = chosen,
            EmailConfirmed     = false,
            DateCreated        = DateTime.UtcNow,
        };

        var created = await _userManager.CreateAsync(user, password);
        if (created.Succeeded) return new Outcome(user, null);

        // Identity's password rules produce the messages here. A duplicate handle can also land
        // here if somebody took it between the caller's check and this line — the unique index
        // is the real guard, and this is where it reports.
        var isHandleClash = created.Errors.Any(e =>
            e.Description.Contains("Handle", StringComparison.OrdinalIgnoreCase));

        return new Outcome(null,
            isHandleClash
                ? "That name was taken a moment ago. Try another."
                : string.Join(" ", created.Errors.Select(e => e.Description)),
            isHandleClash);
    }

    /// <summary>
    /// Builds and sends the confirmation link; stamps <c>DateConfirmationSent</c> on a real send.
    /// </summary>
    /// <remarks>
    /// <para>The link points at the <b>website</b>, not at this API. Identity's own
    /// <c>/confirmEmail</c> returns a line of text, which is a poor thing to land on after clicking
    /// a link in an email; the website's page calls that endpoint and then offers a way to sign in.
    /// </para>
    ///
    /// <para>The token is base64url-encoded because it goes in a query string and Identity's raw
    /// tokens are not URL-safe. This is the same encoding <c>MapIdentityApi</c> uses, so the same
    /// confirmation endpoint accepts both.</para>
    ///
    /// <para>Returns whether the message left this machine. When it did not, the sender has
    /// already logged the link at Warning so a local sign-up can still be finished.</para>
    /// </remarks>
    public async Task<bool> SendConfirmationAsync(AppUser user, string? returnUrl, CancellationToken ct)
    {
        string link;
        bool sent;
        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var code  = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            link = $"{BaseUrl}/confirm-email?userId={user.Id}&code={code}";
            if (IsLocalPath(returnUrl))
                link += $"&returnUrl={Uri.EscapeDataString(returnUrl!)}";

            sent = await _mailer.TrySendConfirmationAsync(user, user.Email!, link);
        }
        catch (Exception ex)
        {
            // The account already exists by the time this runs, and so may the request that
            // created it. Letting a token-provider or mail fault escape would report the whole
            // thing as failed to somebody whose account was in fact made — and would leave them
            // with no account they could reach and no way to try again, because the address is
            // now taken. Recorded at Error, where the diagnostics page reads it.
            _logger.LogError(ex,
                "Could not send the confirmation message to {Recipient}. The account exists but "
              + "its owner has not been told how to complete it.", user.Email);
            return false;
        }

        if (sent)
        {
            // Stamped only on a real send. An account with no DateConfirmationSent has never been
            // told how to complete itself, and that is now a question the database can answer.
            user.DateConfirmationSent = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }

        return sent;
    }

    /// <summary>
    /// Tells the real account holder that somebody tried to register their address.
    /// </summary>
    /// <remarks>
    /// <para>The only person entitled to know that the address is registered is the person who
    /// registered it. This is also genuinely useful to them: it is either their own forgotten
    /// account, or somebody probing it.</para>
    ///
    /// <para>Sent through <c>IEmailService</c> with its own wording rather than through Identity's
    /// sender: the three messages Identity knows how to send are confirmation, password reset and
    /// email change, and this is none of them. Reusing the confirmation template would have
    /// delivered "confirm your email" to somebody who confirmed theirs months ago.</para>
    /// </remarks>
    public async Task WarnExistingAccountHolderAsync(AppUser existing, CancellationToken ct)
    {
        var name = System.Net.WebUtility.HtmlEncode(_site.Name);
        await TrySendAsync(existing,
            $"Someone tried to sign up with your email on {_site.Name}",
            BenEmailLayout.Wrap(_site,
                "You already have an account",
                $"<p>Somebody tried to create an account on {name} using this email address, and one "
                + "already exists.</p>"
                + "<p>If that was you, you already have an account — sign in below, or reset your "
                + "password if you have forgotten it.</p>"
                + "<p>If it was not you, there is nothing to do. Your account has not changed and "
                + "nobody has been given access to it.</p>",
                buttonText: "Sign in", buttonUrl: $"{BaseUrl}/login"),
            "their address was used in a sign-up attempt", ct);
    }

    /// <summary>
    /// Tells the account holder that an investigation request was made under their address, and
    /// gives them the one link that can claim it.
    /// </summary>
    /// <remarks>
    /// The street address is in the message deliberately: it is what lets the holder tell "I did
    /// this from the kitchen on my phone" from "somebody is using my email". The link carries the
    /// secret; the page it lands on requires signing in as this address before it shows anything.
    /// </remarks>
    public async Task TellHolderAboutPendingRequestAsync(
        AppUser existing, string streetAddress, string adoptLink, CancellationToken ct)
    {
        var name = System.Net.WebUtility.HtmlEncode(_site.Name);
        await TrySendAsync(existing,
            $"An investigation request was made using your {_site.Name} email",
            BenEmailLayout.Wrap(_site,
                "Was this you?",
                $"<p>Somebody asked for an investigation at "
                + $"<strong>{System.Net.WebUtility.HtmlEncode(streetAddress)}</strong> on {name} "
                + "using this email address, which already has an account.</p>"
                + "<p>If that was you, sign in to finish it — the button below opens the request "
                + "so you can add it to your account. Nothing has been sent to any group yet.</p>"
                + "<p>If it was not you, there is nothing to do. Your account has not changed, and "
                + "the request will be discarded on its own.</p>",
                buttonText: "Sign in to finish it", buttonUrl: adoptLink),
            "an investigation request was made under their address", ct);
    }

    private async Task TrySendAsync(AppUser to, string subject, string body, string what, CancellationToken ct)
    {
        try
        {
            await _email.SendAsync(to.Email!, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not tell {UserId} that {What}.", to.Id, what);
        }
    }

    private string BaseUrl =>
        (_site.BaseUrl?.TrimEnd('/') is { Length: > 0 } configured
            ? configured
            : _configuration["AppBaseUrl"]?.TrimEnd('/'))
        ?? string.Empty;

    /// <summary>A path on this site and nothing else — never an absolute or protocol-relative URL.</summary>
    public static bool IsLocalPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && !path.StartsWith("//");
}
