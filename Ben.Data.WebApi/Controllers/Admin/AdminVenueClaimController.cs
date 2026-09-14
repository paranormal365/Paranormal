using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Where a person decides who runs a place, when a code could not (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>To decide:</b> claims with no address to prove them by, and claims somebody objected to.
/// <b>Standing:</b> proved claims inside their week for objections, shown so a reviewer who knows
/// something is wrong can step in before they take effect. <b>Confirmed:</b> every venue already
/// accepted, with a way to undo one — because the commonest false claims are a competitor and a
/// former manager, and the second is exactly the one that is discovered afterwards.</para>
///
/// <para>Undoing a confirmation stops the venue gating future events and takes its page down. It
/// does not touch any grant it gave or any event resting on one: those were given in good faith by
/// whoever answered for the building at the time, and unpicking them is a conversation, not a click.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/venue-claims")]
public sealed class AdminVenueClaimController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;

    public AdminVenueClaimController(IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages)
    { _dbFactory = dbFactory; _messages = messages; }

    [HttpGet]
    public async Task<ActionResult<AdminVenueClaimListRecord>> Get(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, null, ct));
    }

    [HttpPost("{claimId:guid}/approve")]
    public async Task<ActionResult<AdminVenueClaimListRecord>> Approve(
        Guid claimId, [FromBody] DecideVenueClaimRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var claim = await db.VenuePlaceClaims.Include(c => c.Place).Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null) return NotFound();
        if (!VenueClaims.Open.Contains(claim.State)) return Conflict("This claim is already settled.");

        var note = request.Note?.Trim() is { Length: > 0 } n ? n : null;
        if (await VenueClaims.ApproveAsync(db, claim, userId, note, DateTime.UtcNow, ct) is { } refusal)
            return Conflict(refusal);

        await db.SaveChangesAsync(ct);

        await _messages.SendAsync(
            $"{claim.Organization.Name} is confirmed as the venue at {claim.Place.Name}",
            $"<p>Your group is now confirmed as the venue at <strong>{VenueNotices.Safe(claim.Place.Name)}</strong>. "
            + "Other groups will ask you before publishing events there, and you can show your venue page.</p>"
            + (note is null ? "" : $"<p>The reviewer said: “{VenueNotices.Safe(note)}”</p>")
            + $"<p><a href=\"/organizations/{claim.OrganizationId}/venue\">Open your venue</a></p>",
            [claim.ClaimantAppUserId], userId, ct);

        return Ok(await ListAsync(db, $"{claim.Organization.Name} is now the venue at {claim.Place.Name}.", ct));
    }

    [HttpPost("{claimId:guid}/refuse")]
    public async Task<ActionResult<AdminVenueClaimListRecord>> Refuse(
        Guid claimId, [FromBody] DecideVenueClaimRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (request.Note?.Trim() is not { Length: > 0 } note)
            return BadRequest("Say why. The claimant reads it.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var claim = await db.VenuePlaceClaims.Include(c => c.Place).Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null) return NotFound();
        if (!VenueClaims.Open.Contains(claim.State)) return Conflict("This claim is already settled.");

        var now = DateTime.UtcNow;
        claim.State = VenueClaimState.Refused;
        claim.DecidedUtc = now;
        claim.DecidedByAppUserId = userId;
        claim.DecisionNote = note;
        claim.CodeHash = null;
        claim.DateUpdated = now;
        claim.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        await _messages.SendAsync(
            $"Your claim to run {claim.Place.Name} was not accepted",
            $"<p>Your group was not confirmed as the venue at {VenueNotices.Safe(claim.Place.Name)}.</p>"
            + $"<p>The reviewer said: “{VenueNotices.Safe(note)}”</p>",
            [claim.ClaimantAppUserId], userId, ct);

        return Ok(await ListAsync(db, "Refused. The claimant has been told why.", ct));
    }

    /// <summary>Undoes a confirmation that turned out to be wrong.</summary>
    [HttpPost("~/api/admin/venue-profiles/{profileId:guid}/unconfirm")]
    public async Task<ActionResult<AdminVenueClaimListRecord>> Unconfirm(
        Guid profileId, [FromBody] DecideVenueClaimRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (request.Note?.Trim() is not { Length: > 0 } note)
            return BadRequest("Say why. The group reads it.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var profile = await db.OrganizationVenueProfiles.Include(v => v.Place).Include(v => v.Organization)
            .FirstOrDefaultAsync(v => v.Id == profileId && v.VerifiedUtc != null, ct);
        if (profile is null) return NotFound();

        profile.VerifiedUtc = null;
        profile.IsPublished = false;
        profile.DateUpdated = DateTime.UtcNow;
        profile.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        await _messages.SendAsync(
            $"{profile.Organization.Name} is no longer confirmed as the venue at {profile.Place.Name}",
            $"<p>Your group is no longer confirmed as the venue at {VenueNotices.Safe(profile.Place.Name)}, and its venue page "
            + $"has been taken down. Yeses you already gave still stand.</p><p>The reviewer said: “{VenueNotices.Safe(note)}”</p>",
            await VenueNotices.PeopleWhoAnswerForAsync(db, profile.OrganizationId, ct), userId, ct);

        return Ok(await ListAsync(db, $"{profile.Organization.Name} is no longer the venue at {profile.Place.Name}.", ct));
    }

    private static async Task<AdminVenueClaimListRecord> ListAsync(BenDataContext db, string? note, CancellationToken ct)
    {
        // The reviewer sees the whole address, unmasked: whether it looks like the venue's own is
        // half the decision.
        var records = await db.VenuePlaceClaims.AsNoTracking()
            .OrderBy(c => c.DateCreated)
            .Select(c => new VenueClaimRecord(
                c.Id, c.PlaceId, c.Place.Name ?? "", c.OrganizationId, c.Organization.Name,
                c.ClaimantAppUser.DisplayName ?? c.ClaimantAppUser.Email ?? "",
                c.ClaimantRole, c.Evidence, c.State,
                c.PlaceContact != null ? c.PlaceContact.Value : null,
                c.DateCreated, c.ProvedUtc, c.ObjectionsCloseUtc,
                c.ObjectingOrganization != null ? c.ObjectingOrganization.Name : null,
                c.ObjectionText, c.DecidedUtc, c.DecisionNote, new List<Guid>()))
            .ToListAsync(ct);

        var confirmed = await db.OrganizationVenueProfiles.AsNoTracking()
            .Where(v => v.VerifiedUtc != null)
            .OrderBy(v => v.Place.Name)
            .Select(v => new ConfirmedVenueRecord(v.Id, v.PlaceId, v.Place.Name ?? "", v.OrganizationId, v.Organization.Name, v.VerifiedUtc!.Value))
            .ToListAsync(ct);

        return new(
            [.. records.Where(r => r.State is VenueClaimState.Contested
                                || (r.State == VenueClaimState.Pending && r.CodeSentTo is null))],
            [.. records.Where(r => r.State is VenueClaimState.Proved
                                || (r.State == VenueClaimState.Pending && r.CodeSentTo is not null))],
            [.. records.Where(r => r.State is VenueClaimState.Approved or VenueClaimState.Refused)
                .OrderByDescending(r => r.DecidedUtc).Take(50)],
            confirmed, note);
    }
}
