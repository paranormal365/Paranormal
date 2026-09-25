using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Tours;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The meeting point: a guide scanning guests onto a walk (item 247).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-20: <i>"qr codes for ghost tour tickets for the staff to scan when they
/// arrive for the tour."</i> A hosted event has a door screen built for staff at a venue; a walk
/// had nothing, which is the part of this that had no equivalent at all.</para>
///
/// <para><b>Every answer is a sentence.</b> The reader is somebody standing on a dark street with
/// a queue behind them, so a refused scan says what to do next — check the email, look them up by
/// name, that pass is for Friday — rather than returning a status code for a page to interpret.
/// A scan is never an error response for the same reason: "not recognised" is an ANSWER, and a
/// 404 would make the page show its own failure text instead of the guide's.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/tour-door")]
[Authorize]
public sealed class TourDoorController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;

    public TourDoorController(IDbContextFactory<BenDataContext> db, IOrganizationSecurityService security)
    { _db = db; _security = security; }

    /// <summary>
    /// Whether this person may work the door of this walk.
    /// </summary>
    /// <remarks>
    /// The same permission that runs the walk. A guide who may change the event's attendees is a
    /// guide who may let somebody onto it, and anybody else has no business scanning passes.
    /// </remarks>
    private async Task<bool> MayWorkTheDoorAsync(Guid userId, Guid orgId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(userId, orgId,
               Ben.Data.Common.Enums.OrganizationSecurityTable.OrgCalendar,
               Ben.Data.Common.Enums.OrganizationSecurityAction.Update, ct);

    /// <summary>Scans one pass and says what the guide should do.</summary>
    [HttpPost("scan")]
    public async Task<ActionResult<TourScanResult>> Scan(
        Guid orgId, Guid eventId, [FromBody] TourScanRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();
        if (!await MayWorkTheDoorAsync(userId.Value, orgId, ct)) return Forbid();

        var token = request.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return Ok(TourScanResult.Refused("That code could not be read. Try the scan again."));

        await using var db = await _db.CreateDbContextAsync(ct);

        var seat = await TourPasses.ByTokenAsync(db, token, ct);

        // Named specifically, because a guest holding a valid pass for next Friday is the most
        // common honest mistake at a meeting point.
        var otherWalk = seat is not null && seat.OrgCalendarEventId != eventId
            ? await db.OrgCalendarEvents.AsNoTracking()
                .Where(e => e.Id == seat.OrgCalendarEventId)
                .Select(e => e.Title)
                .FirstOrDefaultAsync(ct)
            : null;

        if (TourPasses.WhyThisScanIsRefused(seat, eventId, otherWalk) is { } refusal)
            return Ok(TourScanResult.Refused(refusal));

        var (alreadyIn, at) = TourPasses.CheckIn(seat!, userId.Value);
        await db.SaveChangesAsync(ct);

        var who = seat!.AppUser?.DisplayName ?? "This guest";
        var seats = Math.Max(1, seat.Seats);

        return Ok(new TourScanResult
        {
            Admitted = true,
            AlreadyIn = alreadyIn,
            GuestName = who,
            Seats = seats,
            // Said, not implied. A guide needs "already here" out loud so they can tell a
            // queue-jumper from somebody whose friend scanned their code a minute ago.
            Says = alreadyIn
                ? $"{who} is already in — scanned at {at.ToLocalTime():h:mm tt}."
                : seats > 1
                    ? $"{who} — {seats} places. Let them in."
                    : $"{who}. Let them in.",
        });
    }
}

/// <summary>One scanned code.</summary>
public sealed record TourScanRequest(string? Token);
