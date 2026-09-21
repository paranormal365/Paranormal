using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Investigations;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The guest's end of the guide's code (item 248).
/// </summary>
/// <remarks>
/// <para><b>Two doors, and only one of them is open to strangers.</b> Looking a code up says what
/// it is for, so somebody who has just scanned a sheet can see they are in the right place before
/// being asked to sign in for anything. Redeeming it needs an account, because a credential has to
/// belong to somebody — and because every rule the site already has about who may sign in, who is
/// blocked and whose account is closed then applies without being restated here.</para>
///
/// <para><b>Why the guest signs in at all</b>, when the whole point is somebody at a gate in the
/// dark: the alternative is minting shadow accounts from a name typed on a pavement, and those
/// accounts cannot be signed back into, cannot be recovered, and would strand the evening's work
/// the moment the phone was closed. What the scan removes is the ASKING — nobody has to be found
/// in a member list, emailed, approved or added to anything. Somebody with the app already signed
/// in is one tap from working; somebody without gets the ordinary sign-up with the code carried
/// through it.</para>
///
/// <para><b>The look-up is not an oracle.</b> It answers the same shape whatever is wrong —
/// unrecognised, expired, withdrawn — and never says whether an investigation exists, who is on
/// it, whose house it is at, or what case it belongs to. What it names is the group and what the
/// visit is called, which is exactly what was already printed on a sheet held up to a crowd.</para>
/// </remarks>
[ApiController]
[Route("api/public/investigation-codes")]
// Authorize at the class and open ONE door, rather than the other way round. AllowAnonymous
// anywhere above a method wins outright — a class-level one silently cancelled the [Authorize] on
// redeeming, which the compiler said out loud (ASP0026) and which would otherwise have shipped a
// door that mints credentials for callers who never signed in.
[Authorize]
public sealed class PublicInvestigationCodeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly ILogger<PublicInvestigationCodeController> _log;

    public PublicInvestigationCodeController(
        IDbContextFactory<BenDataContext> db, ILogger<PublicInvestigationCodeController> log)
    { _db = db; _log = log; }

    /// <summary>What this code is for, before anybody signs in.</summary>
    /// <remarks>
    /// A scanned token and a typed code both arrive here, because the page behind the QR and the
    /// "have a code?" box in the app are the same screen asking the same question.
    /// </remarks>
    [HttpGet("{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<InvestigationCodeInvitation>> Look(string code, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var found = await GuestCodes.FindAsync(db, code, ct);
        if (GuestCodes.WhyThisCodeIsRefused(found) is { } no)
            return Ok(new InvestigationCodeInvitation(false, no, null, null, null, null, null));

        var investigation = found!.Investigation!;
        var orgName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == found.OrganizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);

        return Ok(new InvestigationCodeInvitation(
            true, null, investigation.Id, investigation.Title, orgName,
            investigation.ScheduledDateTime, found.ExpiresUtc));
    }

    /// <summary>
    /// Joins tonight's work with this code.
    /// </summary>
    /// <remarks>
    /// Idempotent: a guest who scans twice, or comes back after closing the app, holds the one
    /// credential they already had rather than collecting rows a guide would then have to revoke
    /// one at a time.
    /// </remarks>
    [HttpPost("redeem")]
    public async Task<ActionResult<InvestigationCodeRedemption>> Redeem(
        [FromBody] RedeemInvestigationCodeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var found = await GuestCodes.FindAsync(db, request?.Code, ct);
        var (pass, refused) = found is null
            ? (null, GuestCodes.WhyThisCodeIsRefused(null))
            : await GuestCodes.RedeemAsync(db, found, userId.Value, request?.DisplayName, ct);

        if (pass is null)
            return Ok(InvestigationCodeRedemption.Refused(refused ?? "That code can't be used."));

        await db.SaveChangesAsync(ct);

        var investigation = found!.Investigation!;
        var orgName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == found.OrganizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);

        _log.LogInformation(
            "Guest {UserId} joined investigation {InvestigationId} with a join code.",
            userId, investigation.Id);

        return Ok(new InvestigationCodeRedemption(
            true,
            $"You're on {investigation.Title}. Anything you record tonight can go straight to the group.",
            investigation.Id, investigation.Title, orgName, found.ExpiresUtc));
    }

    /// <summary>The visits this account may contribute to as a guest right now.</summary>
    /// <remarks>
    /// <para>So the app has somewhere to send a recording. A guest is not an attendee and not a
    /// member, so nothing else on the site would ever list tonight's investigation for them — and
    /// a credential that admits somebody to a place they cannot find is the write-only shape this
    /// site has shipped eight times already.</para>
    ///
    /// <para>It names the visit and the group, and stops there. The same thin answer the look-up
    /// gives, for the same reason.</para>
    /// </remarks>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<InvestigationCodeInvitation>>> Mine(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var mine = await db.InvestigationGuestPasses.AsNoTracking()
            .Where(p => p.AppUserId == userId.Value
                     && p.RevokedUtc == null
                     && p.InvestigationJoinCode!.RevokedUtc == null
                     && p.InvestigationJoinCode.ExpiresUtc > now)
            .OrderBy(p => p.InvestigationJoinCode!.ExpiresUtc)
            .Select(p => new InvestigationCodeInvitation(
                true, null,
                p.InvestigationId,
                p.InvestigationJoinCode!.Investigation!.Title,
                db.Organizations.Where(o => o.Id == p.InvestigationJoinCode.OrganizationId)
                    .Select(o => o.Name).FirstOrDefault(),
                p.InvestigationJoinCode.Investigation.ScheduledDateTime,
                p.InvestigationJoinCode.ExpiresUtc))
            .ToListAsync(ct);

        return Ok(mine);
    }
}

/// <summary>What a guest sends to join.</summary>
/// <param name="Code">The scanned token or the short code off the sheet; either is understood.</param>
/// <param name="DisplayName">
/// What they want the guide to see them as, when it is not their account's own name. Optional.
/// </param>
public sealed record RedeemInvestigationCodeRequest(string? Code, string? DisplayName);
