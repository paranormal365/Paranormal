using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Investigations;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The sheet a guide holds up, and the list of who is working tonight because of it (item 248).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-20: <i>"something we let them generate and print or generate and let others
/// scan off their tablet, computer or phone."</i> So the code is a thing staff LOOK at — the
/// screen shows it, prints it, and answers "who has one" — rather than something mailed out.</para>
///
/// <para><b>Whoever may run the visit may issue its code.</b> The same gate as changing the
/// investigation, because a code admits people to the investigation's evidence and nothing
/// narrower would mean anything: somebody who cannot run the night has no business deciding who
/// records it.</para>
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/investigations/{investigationId:guid}/join-code")]
[Authorize]
public sealed class InvestigationJoinCodeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly Ben.Data.Common.SiteIdentity _site;

    public InvestigationJoinCodeController(
        IDbContextFactory<BenDataContext> db,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site)
    { _db = db; _site = site.Value; }

    /// <summary>The live code for this visit, or nothing when there isn't one.</summary>
    [HttpGet]
    public async Task<ActionResult<InvestigationJoinCodeRecord?>> Current(
        Guid orgId, Guid investigationId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, investigationId, ct) is { } refusal) return refusal;

        var live = (await GuestCodes.LiveForAsync(db, investigationId, ct)).FirstOrDefault();
        if (live is null) return Ok((InvestigationJoinCodeRecord?)null);

        return Ok(await ToRecordAsync(db, live, ct));
    }

    /// <summary>
    /// Makes a fresh code, retiring whatever this visit had.
    /// </summary>
    /// <remarks>
    /// <para>This is also how a code is taken back: a sheet left on a pub table is answered with a
    /// new sheet. People who already scanned the old one keep working — see
    /// <see cref="GuestCodes.IssueAsync"/> for why deleting their evening is not the remedy.</para>
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<InvestigationJoinCodeRecord>> Issue(
        Guid orgId, Guid investigationId, [FromBody] IssueJoinCodeRequest? request,
        CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, investigationId, ct) is { } refusal) return refusal;

        var investigation = await db.Investigations
            .FirstOrDefaultAsync(i => i.Id == investigationId && i.OrganizationId == orgId, ct);
        if (investigation is null) return NotFound();

        // Asked before anything is made. A code for a visit to somebody's home would admit
        // strangers to a private client's case, which is the one thing this feature must never do.
        if (await GuestCodes.WhyACodeMayNotBeIssuedAsync(db, investigation, ct) is { } refused)
            return BadRequest(refused);

        // Twelve hours by default, which covers a visit that runs past midnight; the ceiling is
        // GuestCodes.LongestLife and the caller cannot talk its way past it.
        var expires = request?.ExpiresUtc ?? DateTime.UtcNow.AddHours(12);

        var code = await GuestCodes.IssueAsync(db, investigation, GetCurrentUserId(), expires, ct);
        await db.SaveChangesAsync(ct);

        return Ok(await ToRecordAsync(db, code, ct));
    }

    /// <summary>Takes the live code out of use without issuing another.</summary>
    /// <remarks>
    /// The end of the night. Everybody's pass stops working with it, because a pass is only live
    /// while the code that minted it is — which is what makes "we're done" one button rather than
    /// a list of people to go through.
    /// </remarks>
    [HttpDelete]
    public async Task<ActionResult<bool>> Revoke(Guid orgId, Guid investigationId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, investigationId, ct) is { } refusal) return refusal;

        var live = await GuestCodes.LiveForAsync(db, investigationId, ct);
        foreach (var code in live) GuestCodes.Revoke(code, GetCurrentUserId());
        await db.SaveChangesAsync(ct);

        return Ok(live.Count > 0);
    }

    /// <summary>Who is working tonight on the strength of this code.</summary>
    [HttpGet("holders")]
    public async Task<ActionResult<IEnumerable<InvestigationGuestPassRecord>>> Holders(
        Guid orgId, Guid investigationId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, investigationId, ct) is { } refusal) return refusal;

        var holders = await db.InvestigationGuestPasses.AsNoTracking()
            .Where(p => p.InvestigationId == investigationId)
            .OrderBy(p => p.IssuedUtc)
            .Select(p => new InvestigationGuestPassRecord(
                p.Id,
                p.AppUserId,
                p.DisplayName ?? p.AppUser!.DisplayName ?? "A guest",
                p.IssuedUtc,
                p.RevokedUtc,
                db.FieldSessionUploads.Count(s => s.InvestigationId == investigationId
                                               && s.SubmittedByAppUserId == p.AppUserId)))
            .ToListAsync(ct);

        return Ok(holders);
    }

    /// <summary>
    /// Takes one person's credential away, leaving everybody else's alone.
    /// </summary>
    /// <remarks>
    /// <para>The reason the pass is its own row. A guide who needs to stop one phone should not
    /// have to reprint the sheet and re-admit twenty people to do it.</para>
    ///
    /// <para>What they already sent up stays. A revoked pass stops somebody adding more; it is
    /// not a way to delete the evening's record of what happened, which is a separate decision
    /// with its own screen.</para>
    /// </remarks>
    [HttpDelete("holders/{passId:guid}")]
    public async Task<ActionResult<bool>> RevokeHolder(
        Guid orgId, Guid investigationId, Guid passId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, investigationId, ct) is { } refusal) return refusal;

        var pass = await db.InvestigationGuestPasses
            .FirstOrDefaultAsync(p => p.Id == passId && p.InvestigationId == investigationId, ct);
        if (pass is null) return NotFound();

        GuestCodes.RevokePass(pass, GetCurrentUserId());
        await db.SaveChangesAsync(ct);
        return Ok(true);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>The one gate: whoever may run this visit. Null when they may.</summary>
    private async Task<ActionResult?> GateAsync(
        BenDataContext db, Guid orgId, Guid investigationId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        var belongs = await db.Investigations.AsNoTracking()
            .AnyAsync(i => i.Id == investigationId && i.OrganizationId == orgId, ct);
        if (!belongs) return NotFound();

        var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);
        return await InvestigationAccess.CanManageAsync(db, investigationId, userId.Value, isSuperAdmin, ct)
            ? null
            : Forbid();
    }

    /// <summary>
    /// Where the sheet sends somebody, as an address a camera and a person can both use.
    /// </summary>
    /// <remarks>
    /// <para><b>The site's own address, not the browser's.</b> This is printed and read off paper
    /// by a stranger on their own phone; whatever host the guide's tablet happens to be using —
    /// a LAN address, a machine name — is not somewhere that phone can go.</para>
    ///
    /// <para>The configured origin first, exactly as every emailed link on the site resolves.
    /// Falling back to this request's own origin is right for the deployment this site actually
    /// runs — the API and the website share one origin behind a reverse proxy, which is what the
    /// empty CORS allow-list means — and it keeps a code scannable on a host where nobody has set
    /// the origin, instead of putting a relative path in a QR that no camera can follow.</para>
    /// </remarks>
    private string JoinUrl(string path)
    {
        var configured = _site.AbsoluteUrl(path);
        if (Uri.IsWellFormedUriString(configured, UriKind.Absolute)) return configured;

        return $"{Request.Scheme}://{Request.Host}{path}";
    }

    /// <summary>The printed address, without its scheme.</summary>
    private static string WithoutScheme(string absolute)
        => Uri.TryCreate(absolute, UriKind.Absolute, out var uri)
            ? uri.Authority + uri.AbsolutePath
            : absolute;

    private async Task<InvestigationJoinCodeRecord> ToRecordAsync(
        BenDataContext db, Source.Entities.InvestigationJoinCode code, CancellationToken ct)
        => new(code.Id, code.InvestigationId, code.Token, code.TypedCode,
               // What the QR carries is the ADDRESS, not the bare token: a guest points a phone
               // camera at it, and a camera that reads a random hex string can do nothing with it.
               // The address opens the join page, which is what knows how to ask them to sign in.
               // /tonight rather than /join because /join is the group-invitation link and a code
               // for one evening is not an invitation to a group — the two must not share a door.
               //
               // Drawn by the same renderer a tour pass and a hosted event's pass use, so "why
               // does my code not scan" has one place to look.
               Services.Events.EventPasses.DataUri(JoinUrl($"/tonight/{code.Token}"), 6),
               // Host and path only: the sheet is read aloud and typed, so every character that
               // is not needed to reach the page is a character somebody gets wrong in the dark.
               WithoutScheme(JoinUrl("/tonight")),
               code.ExpiresUtc, code.RevokedUtc,
               await db.InvestigationGuestPasses
                   .CountAsync(p => p.InvestigationJoinCodeId == code.Id && p.RevokedUtc == null, ct));
}

/// <summary>What a guide asks for when they make a code.</summary>
/// <param name="ExpiresUtc">
/// When it should stop working. Capped at <see cref="GuestCodes.LongestLife"/>, and twelve hours
/// when it is not given.
/// </param>
public sealed record IssueJoinCodeRequest(DateTime? ExpiresUtc);
