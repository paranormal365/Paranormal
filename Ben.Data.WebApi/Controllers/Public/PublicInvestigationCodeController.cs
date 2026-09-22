using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Investigations;
using Ben.Data.WebApi.Services.Redaction;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>
    /// An investigation's title as somebody who is not on the team may be told it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a title needs redacting at all (C7).</b> A case-bound visit is titled from its
    /// case, and a case is titled for its place — which for a private engagement is a client's own
    /// name or address. The public place page has run this through <c>CaseProseRedactor</c> since
    /// item 184 for exactly that reason; these three answers were handing it over raw, to a caller
    /// who has not even signed in.</para>
    ///
    /// <para>Belt and braces beside the issue door. Nothing can mint a code on a private
    /// engagement any more, but codes issued before that rule existed still resolve, and a rule
    /// enforced only where rows are WRITTEN is one release away from being no rule at all.</para>
    ///
    /// <para>A visit with no case redacts to itself: <c>RedactFor</c> returns the text unchanged
    /// when the case has no roster, and a case that is not a private engagement has none.</para>
    /// </remarks>
    private static async Task<string> TitleForAStrangerAsync(
        BenDataContext db, Guid? caseId, string title, CancellationToken ct)
    {
        if (caseId is not { } id) return title;

        var roster = await CaseRedactionRoster.ForCaseAsync(db, id, ct);
        return (roster is null ? title : CaseProseRedactor.Redact(title, roster)) ?? title;
    }

    /// <summary>What this code is for, before anybody signs in.</summary>
    /// <remarks>
    /// A scanned token and a typed code both arrive here, because the page behind the QR and the
    /// "have a code?" box in the app are the same screen asking the same question.
    /// </remarks>
    [HttpGet("{code}")]
    [AllowAnonymous]
    // Rate limited like the handover door beside it. Guessing a typed code is not a practical way
    // in — thirty to the eighth, and they expire — but an anonymous lookup anybody can call in a
    // loop should cost what signing in costs, and the two doors should not differ by accident.
    [EnableRateLimiting(Ben.Data.WebApi.Services.RateLimiting.AuthPolicy)]
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
            true, null, investigation.Id,
            await TitleForAStrangerAsync(db, investigation.CaseId, investigation.Title, ct),
            orgName, investigation.ScheduledDateTime, found.ExpiresUtc));
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

        var title = await TitleForAStrangerAsync(db, investigation.CaseId, investigation.Title, ct);

        return Ok(new InvestigationCodeRedemption(
            true,
            $"You're on {title}. Anything you record tonight can go straight to the group.",
            investigation.Id, title, orgName, found.ExpiresUtc));
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

        // Read first, redact second. The titles cannot be redacted inside the projection — the
        // roster is two queries of its own — so the rows are materialised and then passed through
        // the same rule Look and Redeem apply, in one batch.
        var rows = await db.InvestigationGuestPasses.AsNoTracking()
            .Where(p => p.AppUserId == userId.Value
                     && p.RevokedUtc == null
                     && p.InvestigationJoinCode!.RevokedUtc == null
                     && p.InvestigationJoinCode.ExpiresUtc > now)
            .OrderBy(p => p.InvestigationJoinCode!.ExpiresUtc)
            .Select(p => new
            {
                p.InvestigationId,
                p.InvestigationJoinCode!.Investigation!.Title,
                p.InvestigationJoinCode.Investigation.CaseId,
                p.InvestigationJoinCode.Investigation.ScheduledDateTime,
                p.InvestigationJoinCode.ExpiresUtc,
                OrganizationName = db.Organizations
                    .Where(o => o.Id == p.InvestigationJoinCode.OrganizationId)
                    .Select(o => o.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var rosters = await CaseRedactionRoster.ForCasesAsync(
            db, rows.Where(r => r.CaseId != null).Select(r => r.CaseId!.Value).Distinct().ToList(), ct);

        var mine = rows.Select(r => new InvestigationCodeInvitation(
                true, null,
                r.InvestigationId,
                r.CaseId is { } caseId
                    ? CaseProseRedactor.RedactFor(rosters, caseId, r.Title)
                    : r.Title,
                r.OrganizationName,
                r.ScheduledDateTime,
                r.ExpiresUtc))
            .ToList();

        return Ok(mine);
    }
}

/// <summary>What a guest sends to join.</summary>
/// <param name="Code">The scanned token or the short code off the sheet; either is understood.</param>
/// <param name="DisplayName">
/// What they want the guide to see them as, when it is not their account's own name. Optional.
/// </param>
public sealed record RedeemInvestigationCodeRequest(string? Code, string? DisplayName);
