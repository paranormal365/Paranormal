using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The signed-out request wizard's one server call: the investigation request and the account,
/// together (site evaluation 2026-09-06, phase 1).
/// </summary>
/// <remarks>
/// <para><b>Why the request is the sign-up.</b> Nothing in an investigation request needs an
/// account until a group wants to talk back. Asking a stranger with something happening in their
/// house to create an account first — six fields including a permanent @name, an email to
/// confirm, a sign-in, an onboarding — put a wall in front of the product's revenue path. So the
/// wizard runs signed out and the account is made here, at Submit, from the request itself.</para>
///
/// <para><b>Three cases, one answer.</b></para>
/// <list type="bullet">
/// <item><b>No account holds the email:</b> the account is created (unconfirmed, with an @name
/// allocated from the given name), the request is created as Submitted with its organisation
/// applications, and the confirmation email goes out with a link that lands on the request.</item>
/// <item><b>An account holds the email:</b> nothing is created on that account and nothing is
/// sent to any group. The request is parked in <see cref="PendingClientRequest"/> and the account
/// holder is emailed a link that adopts it once they are signed in. An anonymous caller must not
/// be able to add requests to somebody else's account, and this endpoint must not become an
/// oracle for which addresses are registered — the same rule <c>AccountRegistrationController</c>
/// keeps. The stranger's answer is <b>identical</b> in both cases, down to the validation order:
/// every check that could fail runs before the account lookup, so a refusal never depends on
/// whether the address exists.</item>
/// <item><b>A signed-in person</b> never reaches this: the wizard uses the authenticated endpoints
/// exactly as before.</item>
/// </list>
///
/// <para>Anonymous, and therefore behind the auth rate limit: it creates accounts and sends
/// email.</para>
/// </remarks>
[ApiController]
[Route("api/public/client-requests")]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimiting.AuthPolicy)]
public sealed class PublicClientRequestController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly AccountCreationService _accounts;
    private readonly SiteIdentity _site;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PublicClientRequestController> _logger;

    public PublicClientRequestController(
        IDbContextFactory<BenDataContext> db,
        UserManager<AppUser> userManager,
        AccountCreationService accounts,
        IOptions<SiteIdentity> site,
        IConfiguration configuration,
        ILogger<PublicClientRequestController> logger)
    {
        _db            = db;
        _userManager   = userManager;
        _accounts      = accounts;
        _site          = site.Value;
        _configuration = configuration;
        _logger        = logger;
    }

    /// <summary>How long a parked request waits for its account holder before it is ignored.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(14);

    /// <summary>How many unclaimed requests one address may have waiting. See ParkForHolderAsync.</summary>
    public const int MaxPendingPerAddress = 3;

    /// <summary>The one sentence both outcomes say. See the class remarks for why it is one.</summary>
    public const string CheckYourEmail =
        "Check your email — we've sent you a link. Use it to confirm your address and follow your request.";

    [HttpPost("submit")]
    public async Task<ActionResult<AnonymousSubmitResponse>> Submit(
        [FromBody] AnonymousClientRequestSubmission request, CancellationToken ct)
    {
        // ── Everything that can be refused is refused here, before the address is looked up ──
        var email       = request.Email?.Trim() ?? string.Empty;
        var displayName = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new AnonymousSubmitResponse(false, "Enter an email address.", nameof(request.Email)));
        if (displayName.Length is < 2 or > 200)
            return BadRequest(new AnonymousSubmitResponse(false, "Enter your name.", nameof(request.Name)));
        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new AnonymousSubmitResponse(false, "Choose a password.", nameof(request.Password)));

        // Identity's password rules, applied to BOTH paths. Without this a weak password would be
        // refused for a new address and accepted for a registered one, and the difference would
        // tell a stranger which addresses have accounts.
        var passwordProblem = await PasswordProblemAsync(request.Password);
        if (passwordProblem is not null)
            return BadRequest(new AnonymousSubmitResponse(false, passwordProblem, nameof(request.Password)));

        if (string.IsNullOrWhiteSpace(request.StreetAddress1) || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.ZipCode))
            return BadRequest(new AnonymousSubmitResponse(false, "Please fill in the required address fields.", nameof(request.StreetAddress1)));

        List<Guid> orgIds = request.OrganizationIds is null ? [] : [.. request.OrganizationIds];
        var problem = ClientRequestRules.CheckSubmission(request.Latitude, request.Longitude, request.Description, orgIds);
        if (problem is not null)
            return BadRequest(new AnonymousSubmitResponse(false, problem, null));

        await using var db = await _db.CreateDbContextAsync(ct);
        problem = await ClientRequestRules.CheckOrganizationsExistAsync(db, orgIds, ct);
        if (problem is not null)
            return BadRequest(new AnonymousSubmitResponse(false, problem, null));

        // ── From here the answer is fixed. Which path runs is nobody's business but the holder's ──
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            await ParkForHolderAsync(db, existing, email, displayName, request, orgIds, ct);
            return Ok(new AnonymousSubmitResponse(true, CheckYourEmail, null));
        }

        var (first, last) = SplitName(displayName);
        var outcome = await _accounts.CreateUnconfirmedAsync(
            email, request.Password, displayName, first, last, handle: null, ct);
        if (!outcome.Succeeded)
        {
            // Only a race can land here: the password passed the same validators above, and the
            // handle was allocated free. Reported as the server's own sentence either way.
            return BadRequest(new AnonymousSubmitResponse(false, outcome.Error!, nameof(request.Password)));
        }
        var user = outcome.User!;

        var now = DateTime.UtcNow;
        var entity = new ClientRequest
        {
            Id                 = Guid.NewGuid(),
            AppUserId          = user.Id,
            Status             = ClientRequestStatus.Submitted,
            StreetAddress1     = request.StreetAddress1.Trim(),
            StreetAddress2     = request.StreetAddress2?.Trim(),
            City               = request.City.Trim(),
            State              = request.State.Trim(),
            ZipCode            = request.ZipCode.Trim(),
            Country            = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim(),
            Latitude           = request.Latitude,
            Longitude          = request.Longitude,
            Gender             = request.Gender,
            BirthYear          = request.BirthYear,
            Description        = request.Description?.Trim(),
            DateCreated        = now,
            CreatedByAppUserId = user.Id,
        };
        db.ClientRequests.Add(entity);
        foreach (var orgId in orgIds)
        {
            db.ClientRequestOrganizations.Add(new ClientRequestOrganization
            {
                Id                 = Guid.NewGuid(),
                ClientRequestId    = entity.Id,
                OrganizationId     = orgId,
                Status             = ClientOrgRequestStatus.Pending,
                DateApplied        = now,
                DateCreated        = now,
                CreatedByAppUserId = user.Id,
            });
        }
        await db.SaveChangesAsync(ct);

        // The person profile's gender too, so onboarding does not ask a second time (W-S4).
        if (request.Gender != ClientGender.NotProvided)
        {
            user.Gender = request.Gender;
            await _userManager.UpdateAsync(user);
        }

        await _accounts.SendConfirmationAsync(user, returnUrl: $"/my-requests/{entity.Id}", ct);

        _logger.LogInformation(
            "Investigation request {RequestId} created a new account {UserId} from the signed-out wizard.",
            entity.Id, user.Id);

        return Ok(new AnonymousSubmitResponse(true, CheckYourEmail, null));
    }

    /// <summary>
    /// Holds the request for the account holder and tells them where it is.
    /// </summary>
    /// <remarks>
    /// The secret in the link is the only credential; its hash is what the row keeps, so a read of
    /// the table cannot forge a link. The row carries the request verbatim — it is copied into a
    /// real <see cref="ClientRequest"/> at adoption and deleted.
    /// </remarks>
    private async Task ParkForHolderAsync(
        BenDataContext db, AppUser holder, string email, string displayName,
        AnonymousClientRequestSubmission request, IReadOnlyCollection<Guid> orgIds, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // A cap, because every parked row sends the holder an email and the address is the only
        // thing the sender needs to know. The rate limiter bounds how fast one caller can try;
        // this bounds how much mail any number of them can aim at one inbox. Over the cap the row
        // is not written and nothing is sent — and the caller is told exactly what everybody else
        // is told, because a different answer here would be the oracle all over again.
        var alreadyWaiting = await db.PendingClientRequests
            .CountAsync(p => p.NormalizedEmail == email.ToUpperInvariant() && p.DateExpires > now, ct);
        if (alreadyWaiting >= MaxPendingPerAddress)
        {
            _logger.LogInformation(
                "A signed-out request for an address with {Count} already waiting was not parked.",
                alreadyWaiting);
            return;
        }

        var secret = NewSecret();
        var row = new PendingClientRequest
        {
            Id                  = Guid.NewGuid(),
            NormalizedEmail     = email.ToUpperInvariant(),
            SecretHash          = HashSecret(secret),
            DisplayName         = displayName,
            StreetAddress1      = request.StreetAddress1.Trim(),
            StreetAddress2      = request.StreetAddress2?.Trim(),
            City                = request.City.Trim(),
            State               = request.State.Trim(),
            ZipCode             = request.ZipCode.Trim(),
            Country             = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim(),
            Latitude            = request.Latitude,
            Longitude           = request.Longitude,
            Gender              = request.Gender,
            BirthYear           = request.BirthYear,
            Description         = request.Description?.Trim(),
            OrganizationIdsJson = JsonSerializer.Serialize(orgIds),
            DateCreated         = now,
            DateExpires         = now + PendingLifetime,
        };
        db.PendingClientRequests.Add(row);
        await db.SaveChangesAsync(ct);

        var link = $"{BaseUrl}/my-requests/adopt/{row.Id}?key={secret}";
        await _accounts.TellHolderAboutPendingRequestAsync(holder, $"{row.StreetAddress1}, {row.City}", link, ct);

        _logger.LogInformation(
            "Investigation request from the signed-out wizard parked as {PendingId} for an existing account.",
            row.Id);
    }

    private async Task<string?> PasswordProblemAsync(string password)
    {
        var probe = new AppUser { Id = Guid.NewGuid(), UserName = "probe", Email = "probe@example.invalid" };
        var errors = new List<string>();
        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, probe, password);
            if (!result.Succeeded) errors.AddRange(result.Errors.Select(e => e.Description));
        }
        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    /// <summary>"Casey Evaluator" → ("Casey", "Evaluator"); one word → (word, null).</summary>
    private static (string First, string? Last) SplitName(string name)
    {
        var at = name.LastIndexOf(' ');
        return at < 0 ? (name, null) : (name[..at].Trim(), name[(at + 1)..].Trim());
    }

    private string BaseUrl =>
        (_site.BaseUrl?.TrimEnd('/') is { Length: > 0 } configured
            ? configured
            : _configuration["AppBaseUrl"]?.TrimEnd('/'))
        ?? string.Empty;

    // ── The secret, shared with the adopt endpoints ──────────────────────────

    public static string NewSecret()
        => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string HashSecret(string secret)
        => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
}

/// <summary>Everything the signed-out wizard collected, plus who is sending it.</summary>
public sealed record AnonymousClientRequestSubmission(
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude,
    ClientGender Gender,
    int? BirthYear,
    string? Description,
    IList<Guid> OrganizationIds,
    string Name,
    string Email,
    string Password);

/// <param name="Field">Which input to point at, when the server could say. Null for a general message.</param>
public sealed record AnonymousSubmitResponse(bool Succeeded, string Message, string? Field);
