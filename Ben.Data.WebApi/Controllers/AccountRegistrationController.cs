using Ben.Data.Common;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Text;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Signing up: creating an account with a display name and an <c>@name</c>, and confirming the
/// email address it was created against.
/// </summary>
/// <remarks>
/// <para><b>Why not <c>MapIdentityApi</c>'s <c>/register</c>.</b> That endpoint takes an email and
/// a password and nothing else, so an account created through it has no display name and no
/// <c>@name</c> — and a handle cannot be added afterwards without letting people change it, which
/// Ben has decided against. Registration therefore has to be one step that either produces a
/// complete account or produces none, which means our own endpoint.</para>
///
/// <para><b>Email confirmation is not new here.</b> Identity is already configured with
/// <c>RequireConfirmedAccount</c>, and <c>IdentityEmailSender</c> is registered so the confirmation
/// mail actually goes out (and falls back to logging the link when SMTP is unconfigured). This
/// endpoint generates the same token type and points at the same confirmation route, so an account
/// made here confirms exactly like any other.</para>
///
/// <para>Anonymous, and therefore rate-limited: it creates accounts, and it can be used to find out
/// which addresses are registered. See <see cref="AttemptRegisterAsync"/> on how the second is
/// handled.</para>
/// </remarks>
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimiting.AuthPolicy)]
public sealed class AccountRegistrationController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly UserHandleService _handles;
    private readonly IEmailSender<AppUser> _emailSender;
    private readonly Ben.Data.Common.Interfaces.IEmailService _email;
    private readonly SiteIdentity _site;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountRegistrationController> _logger;

    public AccountRegistrationController(
        UserManager<AppUser> userManager,
        UserHandleService handles,
        IEmailSender<AppUser> emailSender,
        Ben.Data.Common.Interfaces.IEmailService email,
        IOptions<SiteIdentity> site,
        IConfiguration configuration,
        ILogger<AccountRegistrationController> logger)
    {
        _userManager = userManager;
        _handles = handles;
        _emailSender = emailSender;
        _email = email;
        _site = site.Value;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Whether an <c>@name</c> is legal and free.
    /// </summary>
    /// <remarks>
    /// <para>Called as somebody types, so the answer has to be cheap and the message has to be
    /// usable. It is advisory: the unique index decides, and two people can both be told "free" a
    /// millisecond apart. Registration reports the collision if that happens.</para>
    ///
    /// <para>This does leak which handles exist — but handles are published on every post their
    /// owner writes, so there is nothing here that reading the feed would not tell you. That is
    /// exactly not true of email addresses, which is why <see cref="AttemptRegisterAsync"/> refuses
    /// to say anything about those.</para>
    /// </remarks>
    [HttpGet("handle-available")]
    public async Task<ActionResult<HandleAvailabilityResponse>> HandleAvailable(
        [FromQuery] string? handle, CancellationToken ct)
    {
        var (available, reason) = await _handles.IsAvailableAsync(handle, ct);
        return Ok(new HandleAvailabilityResponse(UserHandle.Normalize(handle), available, reason));
    }

    /// <summary>
    /// Creates an account and sends its confirmation email.
    /// </summary>
    /// <remarks>
    /// <para><b>The answer is the same whether or not the address is already registered.</b> An
    /// endpoint that says "that email is taken" is a way of testing whether somebody has an account
    /// here, which for a site about people's homes is worth more care than the small convenience of
    /// a precise error. When the address exists, nothing is created and the caller is told to check
    /// their email — and the real account holder gets a note saying somebody tried, which is the
    /// only party entitled to know.</para>
    ///
    /// <para>The <c>@name</c> is different and is reported precisely: it is public by nature, so
    /// "that name is taken" tells an attacker nothing the feed does not.</para>
    /// </remarks>
    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> AttemptRegisterAsync(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new RegisterResponse(false, "Enter an email address.", null));

        if (displayName.Length is < 2 or > 200)
            return BadRequest(new RegisterResponse(false, "Enter the name you want people to see.", null));

        // The handle is checked first and reported precisely, so somebody does not fill the form in
        // twice to discover their chosen name was never available.
        var (handleFree, handleReason) = await _handles.IsAvailableAsync(request.Handle, ct);
        if (!handleFree)
            return BadRequest(new RegisterResponse(false, handleReason ?? "Choose another name.", nameof(RegisterRequest.Handle)));

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new RegisterResponse(false, "Choose a password.", nameof(RegisterRequest.Password)));

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Deliberately indistinguishable from success. See the remarks.
            await WarnExistingAccountHolderAsync(existing, ct);
            return Ok(new RegisterResponse(true, CheckYourEmail, null));
        }

        var user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Email              = email,
            UserName           = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName        = displayName,
            FirstName          = request.FirstName?.Trim(),
            LastName           = request.LastName?.Trim(),
            Handle             = UserHandle.Normalize(request.Handle),
            EmailConfirmed     = false,
            DateCreated        = DateTime.UtcNow,
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Identity's password rules produce the messages here. A duplicate handle can also
            // land here if somebody took it between the check above and this line — the unique
            // index is the real guard, and this is where it reports.
            var isHandleClash = created.Errors.Any(e =>
                e.Description.Contains("Handle", StringComparison.OrdinalIgnoreCase));

            return BadRequest(new RegisterResponse(
                false,
                isHandleClash
                    ? "That name was taken a moment ago. Try another."
                    : string.Join(" ", created.Errors.Select(e => e.Description)),
                isHandleClash ? nameof(RegisterRequest.Handle) : nameof(RegisterRequest.Password)));
        }

        await SendConfirmationAsync(user, ct);

        return Ok(new RegisterResponse(true, CheckYourEmail, null));
    }

    private const string CheckYourEmail =
        "Check your email — we've sent a link to confirm the address. You'll be able to sign in once you've used it.";

    /// <summary>
    /// Builds and sends the confirmation link.
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
    /// </remarks>
    private async Task SendConfirmationAsync(AppUser user, CancellationToken ct)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var baseUrl = (_site.BaseUrl?.TrimEnd('/') is { Length: > 0 } configured
                ? configured
                : _configuration["AppBaseUrl"]?.TrimEnd('/'))
            ?? string.Empty;

        var link = $"{baseUrl}/confirm-email?userId={user.Id}&code={code}";

        try
        {
            await _emailSender.SendConfirmationLinkAsync(user, user.Email!, link);
        }
        catch (Exception ex)
        {
            // The account exists either way. IdentityEmailSender already logs the link on failure
            // so a local flow can still be completed; this is the layer above saying so too.
            _logger.LogError(ex, "Could not send the confirmation email for {UserId}.", user.Id);
        }
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
    private async Task WarnExistingAccountHolderAsync(AppUser existing, CancellationToken ct)
    {
        try
        {
            var baseUrl = (_site.BaseUrl?.TrimEnd('/') is { Length: > 0 } configured
                    ? configured
                    : _configuration["AppBaseUrl"]?.TrimEnd('/'))
                ?? string.Empty;

            var name = System.Net.WebUtility.HtmlEncode(_site.Name);
            var body =
                $"<p>Somebody tried to create an account on {name} using this email address, and one "
                + "already exists.</p>"
                + "<p>If that was you, you already have an account — "
                + $"<a href=\"{baseUrl}/login\">sign in here</a>, or reset your password if you have "
                + "forgotten it.</p>"
                + "<p>If it was not you, there is nothing to do. Your account has not changed and "
                + "nobody has been given access to it.</p>";

            await _email.SendAsync(existing.Email!, $"Someone tried to sign up with your email on {_site.Name}", body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not warn {UserId} that their address was used in a sign-up attempt.", existing.Id);
        }
    }

    /// <summary>
    /// Confirms an email address from the link.
    /// </summary>
    /// <remarks>
    /// A POST rather than a GET, and the website's page calls it on a button press rather than on
    /// load. Mail scanners and link previewers fetch URLs found in messages; a confirmation that
    /// happens on GET is a confirmation a scanner can perform on somebody's behalf.
    /// </remarks>
    [HttpPost("confirm-email")]
    public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null) return Ok(new ConfirmEmailResponse(false, "That link is not valid."));

        if (user.EmailConfirmed) return Ok(new ConfirmEmailResponse(true, "Your email is already confirmed."));

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
        }
        catch (FormatException)
        {
            return Ok(new ConfirmEmailResponse(false, "That link is not valid."));
        }

        var result = await _userManager.ConfirmEmailAsync(user, token);

        return Ok(result.Succeeded
            ? new ConfirmEmailResponse(true, "Your email is confirmed. You can sign in now.")
            : new ConfirmEmailResponse(false, "That link has expired or has already been used."));
    }
}

/// <summary>A sign-up. <c>Handle</c> is normalised and checked server-side regardless of what the browser did.</summary>
public sealed record RegisterRequest(
    string Email, string Password, string DisplayName, string Handle,
    string? FirstName = null, string? LastName = null);

/// <summary>The result of a sign-up. <c>Field</c> names the input to point at, or null for a general message.</summary>
public sealed record RegisterResponse(bool Succeeded, string Message, string? Field);

public sealed record HandleAvailabilityResponse(string Handle, bool Available, string? Reason);

public sealed record ConfirmEmailRequest(Guid UserId, string Code);

public sealed record ConfirmEmailResponse(bool Succeeded, string Message);
