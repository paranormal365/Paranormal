using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
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
    private readonly AccountCreationService _accounts;
    private readonly IDbContextFactory<Ben.Data.Source.Context.BenDataContext> _db;

    public AccountRegistrationController(
        UserManager<AppUser> userManager,
        UserHandleService handles,
        AccountCreationService accounts,
        IDbContextFactory<Ben.Data.Source.Context.BenDataContext> db)
    {
        _userManager = userManager;
        _handles     = handles;
        _accounts    = accounts;
        _db          = db;
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
            await _accounts.WarnExistingAccountHolderAsync(existing, ct);
            return Ok(new RegisterResponse(true, CheckYourEmail, null));
        }

        var outcome = await _accounts.CreateUnconfirmedAsync(
            email, request.Password, displayName, request.FirstName, request.LastName, request.Handle, ct);

        if (!outcome.Succeeded)
            return BadRequest(new RegisterResponse(false, outcome.Error!,
                outcome.HandleClash ? nameof(RegisterRequest.Handle) : nameof(RegisterRequest.Password)));

        await _accounts.SendConfirmationAsync(outcome.User!, returnUrl: null, ct);

        return Ok(new RegisterResponse(true, CheckYourEmail, null));
    }

    private const string CheckYourEmail =
        "Check your email — we've sent a link to confirm the address. You'll be able to sign in once you've used it.";

    /// <summary>
    /// Sends the confirmation link again, on request from the sign-in page.
    /// </summary>
    /// <remarks>
    /// <para><b>The answer is identical in every case</b> — account absent, already confirmed,
    /// throttled, sent, or send failed. This endpoint is anonymous and takes an email address, so
    /// any distinction in its reply is an oracle for which addresses hold accounts; the register
    /// endpoint above pays the same discipline for the same reason. The truthful phrasing that
    /// covers all of it: if the address has an unconfirmed account, a link is on its way.</para>
    ///
    /// <para><b>Throttled by <c>DateConfirmationSent</c></b> — the column added when Ben's own
    /// sign-up produced no email — at the same 60 seconds the contact-info resend uses. The
    /// throttle window also absorbs a stale read: the stamp is written before any distinct caller
    /// could retry.</para>
    ///
    /// <para>A send that succeeds re-stamps the column, so Admin → Users shows the LATEST attempt
    /// rather than the first — "Sent 09/01" on a row whose owner asked again today would send an
    /// administrator investigating yesterday's mail problem.</para>
    /// </remarks>
    [HttpPost("resend-confirmation")]
    public async Task<ActionResult<ResendConfirmationResponse>> ResendConfirmation(
        [FromBody] ResendConfirmationRequest request, CancellationToken ct)
    {
        var neutral = new ResendConfirmationResponse(
            "If that address has an unconfirmed account, a new link is on its way. "
          + "Check your spam folder too.");

        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Ok(neutral);

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed)
            return Ok(neutral);

        if (user.DateConfirmationSent is { } last && DateTime.UtcNow - last < ResendCooldown)
            return Ok(neutral);

        await _accounts.SendConfirmationAsync(user, returnUrl: null, ct);
        return Ok(neutral);
    }

    /// <summary>Matches the contact-info resend, deliberately — one number for one idea.</summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

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

        if (user.EmailConfirmed)
            return Ok(new ConfirmEmailResponse(true, "Your email is already confirmed.",
                user.Handle, await WhatIsWaitingAsync(user.Id, ct)));

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
        if (result.Succeeded)
        {
            // Identity records confirmation as a bare bool. The time is what makes it readable
            // next to DateConfirmationSent — "sent Monday, confirmed Monday" against "sent Monday,
            // still nothing" is the whole diagnostic.
            user.DateEmailConfirmed = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }

        return Ok(result.Succeeded
            ? new ConfirmEmailResponse(true, "Your email is confirmed. You can sign in now.",
                user.Handle, await WhatIsWaitingAsync(user.Id, ct))
            : new ConfirmEmailResponse(false, "That link has expired or has already been used."));
    }

    /// <summary>
    /// A sentence about the investigation request this person made before they could sign in.
    /// </summary>
    /// <remarks>
    /// Somebody who signed up by asking for help has been unable to sign in since, so nothing the
    /// site did in the meantime — a group looking, a group accepting — has reached them. The
    /// confirmation page is the first screen they see, so it says where things stand. Null for
    /// everyone else; the page then says nothing.
    /// </remarks>
    private async Task<string?> WhatIsWaitingAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var latest = await db.ClientRequests.AsNoTracking()
            .Where(r => r.AppUserId == userId
                     && (r.Status == ClientRequestStatus.Submitted || r.Status == ClientRequestStatus.Assigned))
            .OrderByDescending(r => r.DateCreated)
            .Select(r => new
            {
                r.Status,
                Groups = r.OrganizationApplications
                    .Where(a => a.Status != ClientOrgRequestStatus.Cancelled)
                    .Select(a => new { a.Organization.Name, a.Status })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (latest is null || latest.Groups.Count == 0) return null;

        var accepted = latest.Groups.FirstOrDefault(g => g.Status == ClientOrgRequestStatus.Accepted);
        if (latest.Status == ClientRequestStatus.Assigned && accepted is not null)
            return $"{accepted.Name} has accepted your request. Sign in to see your case and talk to them.";

        var names = latest.Groups.Select(g => g.Name).ToList();
        var who = names.Count == 1 ? names[0] : string.Join(" and ", names);
        return $"Your request is with {who}, waiting for their review. Sign in to follow it.";
    }
}

/// <summary>A sign-up. <c>Handle</c> is normalised and checked server-side regardless of what the browser did.</summary>
public sealed record RegisterRequest(
    string Email, string Password, string DisplayName, string Handle,
    string? FirstName = null, string? LastName = null);

/// <summary>The result of a sign-up. <c>Field</c> names the input to point at, or null for a general message.</summary>
public sealed record RegisterResponse(bool Succeeded, string Message, string? Field);

public sealed record HandleAvailabilityResponse(string Handle, bool Available, string? Reason);

public sealed record ResendConfirmationRequest(string? Email);

/// <summary>One field, always the same sentence. See ResendConfirmation for why.</summary>
public sealed record ResendConfirmationResponse(string Message);

public sealed record ConfirmEmailRequest(Guid UserId, string Code);

/// <param name="Handle">The @name the account carries, shown once on the landing page — the
/// request wizard allocates it without asking, so this is the first the person hears of it.</param>
/// <param name="Waiting">Where the request they made before they could sign in stands, or null.</param>
public sealed record ConfirmEmailResponse(bool Succeeded, string Message, string? Handle = null, string? Waiting = null);
