using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// How to reach a place — its website, phone numbers, email addresses — public or private
/// (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>Public details are everybody's to read; private ones are the recording group's.</b> A
/// hotel's front-desk number is on its sign. The events coordinator's mobile that a society was
/// given for scheduling is not, and nobody but that society sees it.</para>
///
/// <para><b>Before a place has a confirmed venue, any group may record its public details</b>, and
/// each says who added it. <b>After</b>, the public details are the venue's: it vouches for what
/// others added or removes it, and other groups may add private notes only. Ben, 2026-09-13: the
/// details are "completed by validation or verification of who is in charge of venue, or a rep of
/// the venue".</para>
/// </remarks>
[ApiController]
[Authorize]
public sealed class PlaceContactController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IOrganizationSecurityService _security;

    public PlaceContactController(IDbContextFactory<BenDataContext> dbFactory, IOrganizationSecurityService security)
    { _dbFactory = dbFactory; _security = security; }

    /// <summary>The public details, for anybody.</summary>
    [AllowAnonymous]
    [HttpGet("api/public/places/{placeId:guid}/contacts")]
    public async Task<ActionResult<PlaceContactListRecord>> GetPublic(Guid placeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, placeId, viewerId: null, ct));
    }

    /// <summary>The public details and the viewer's own groups' private ones.</summary>
    [HttpGet("api/places/{placeId:guid}/contacts")]
    public async Task<ActionResult<PlaceContactListRecord>> Get(Guid placeId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, placeId, userId, ct));
    }

    [HttpPost("api/places/{placeId:guid}/contacts")]
    public async Task<ActionResult<PlaceContactListRecord>> Add(
        Guid placeId, [FromBody] AddPlaceContactRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await db.Places.AnyAsync(p => p.Id == placeId, ct)) return NotFound("We can't find that place.");
        if (!IsSuperAdmin && !await IsMemberAsync(db, request.OrganizationId, userId, ct))
            return Forbid();

        if (Enum.IsDefined(request.Kind) == false) return BadRequest("Choose a website, a phone number or an email address.");
        var (value, refusal) = Normalise(request.Kind, request.Value);
        if (refusal is not null) return BadRequest(refusal);

        var venue = await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct);
        if (request.IsPublic && venue is not null && venue.OrganizationId != request.OrganizationId && !IsSuperAdmin)
            return Conflict($"{venue.Organization.Name} runs this place, so its public details are theirs to keep. "
                          + "Add it as a private note for your group instead.");

        if (await db.PlaceContacts.AnyAsync(c => c.PlaceId == placeId && c.Kind == request.Kind && c.Value == value
                                              && (c.IsPublic || c.OrganizationId == request.OrganizationId), ct))
            return Conflict("That's already on the record for this place.");

        var now = DateTime.UtcNow;
        db.PlaceContacts.Add(new PlaceContact
        {
            Id = Guid.NewGuid(), PlaceId = placeId, Kind = request.Kind, Value = value!,
            Label = request.Label?.Trim() is { Length: > 0 } l ? (l.Length > 120 ? l[..120] : l) : null,
            IsPublic = request.IsPublic, OrganizationId = request.OrganizationId,
            // The venue adding its own public detail is vouching for it in the same breath.
            ConfirmedByVenueUtc = request.IsPublic && venue?.OrganizationId == request.OrganizationId ? now : null,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, placeId, userId, ct));
    }

    [HttpDelete("api/places/{placeId:guid}/contacts/{contactId:guid}")]
    public async Task<ActionResult<PlaceContactListRecord>> Remove(Guid placeId, Guid contactId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var contact = await db.PlaceContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.PlaceId == placeId, ct);
        if (contact is null) return NotFound();

        var venue = await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct);
        if (!await MayRemoveAsync(db, contact, venue, userId, ct))
            return NotFound("That isn't yours to remove.");

        // A claim that was proved by this address keeps its record of where the code went.
        await db.VenuePlaceClaims.Where(c => c.PlaceContactId == contactId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.PlaceContactId, (Guid?)null), ct);

        db.PlaceContacts.Remove(contact);
        await db.SaveChangesAsync(ct);
        return Ok(await ListAsync(db, placeId, userId, ct));
    }

    /// <summary>The confirmed venue vouches for a public detail somebody else added.</summary>
    [HttpPost("api/places/{placeId:guid}/contacts/{contactId:guid}/confirm")]
    public async Task<ActionResult<PlaceContactListRecord>> Confirm(Guid placeId, Guid contactId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var contact = await db.PlaceContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.PlaceId == placeId, ct);
        if (contact is null || !contact.IsPublic) return NotFound();

        var venue = await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct);
        if (venue is null || !await AnswersForAsync(userId, venue.OrganizationId, ct))
            return Conflict("Only the confirmed venue can vouch for a place's public details.");

        contact.ConfirmedByVenueUtc = DateTime.UtcNow;
        contact.DateUpdated = DateTime.UtcNow;
        contact.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(await ListAsync(db, placeId, userId, ct));
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private bool IsSuperAdmin => User.IsInRole(RoleNames.SuperAdmin);

    private static Task<bool> IsMemberAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => db.OrganizationUserMemberships.AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

    private async Task<bool> AnswersForAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsSuperAdmin || await _security.HasAccessAsync(userId, orgId,
            OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private async Task<bool> MayRemoveAsync(
        BenDataContext db, PlaceContact contact, OrganizationVenueProfile? venue, Guid userId, CancellationToken ct)
    {
        if (IsSuperAdmin) return true;

        // The venue keeps the public details once there is one.
        if (contact.IsPublic && venue is not null)
            return await AnswersForAsync(userId, venue.OrganizationId, ct);

        return contact.OrganizationId is Guid org && await IsMemberAsync(db, org, userId, ct);
    }

    /// <summary>Trimmed, and checked for the obvious mistakes — not validated to the letter.</summary>
    internal static (string? Value, string? Refusal) Normalise(PlaceContactKind kind, string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return (null, "Say what it is.");
        if (value.Length > 320) return (null, "That is too long to be one address or number.");

        switch (kind)
        {
            case PlaceContactKind.Email:
                if (!System.Net.Mail.MailAddress.TryCreate(value, out var mail) || mail.Address != value)
                    return (null, "That email address doesn't look right.");
                return (value.ToLowerInvariant(), null);

            case PlaceContactKind.Website:
                if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    value = "https://" + value;
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Host.Contains('.'))
                    return (null, "That web address doesn't look right.");
                return (uri.GetLeftPart(UriPartial.Path).TrimEnd('/'), null);

            default:
                if (value.Count(char.IsDigit) < 7)
                    return (null, "That phone number looks too short.");
                return (value, null);
        }
    }

    private async Task<PlaceContactListRecord> ListAsync(BenDataContext db, Guid placeId, Guid? viewerId, CancellationToken ct)
    {
        var myOrgs = viewerId is Guid me
            ? await db.OrganizationUserMemberships.AsNoTracking()
                .Where(m => m.AppUserId == me && m.IsActive).Select(m => m.OrganizationId).ToListAsync(ct)
            : [];

        var venue = await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct);
        var viewerAnswersForVenue = viewerId is Guid v && venue is not null && await AnswersForAsync(v, venue.OrganizationId, ct);

        var rows = await db.PlaceContacts.AsNoTracking()
            .Where(c => c.PlaceId == placeId
                     && (c.IsPublic || (c.OrganizationId != null && myOrgs.Contains(c.OrganizationId.Value))))
            .OrderBy(c => !c.IsPublic).ThenBy(c => c.Kind).ThenBy(c => c.DateCreated)
            .Select(c => new { Contact = c, OrgName = c.Organization != null ? c.Organization.Name : null })
            .ToListAsync(ct);

        var records = new List<PlaceContactRecord>();
        foreach (var r in rows)
        {
            var c = r.Contact;
            var canRemove = viewerId is Guid who && await MayRemoveAsync(db, c, venue, who, ct);
            records.Add(new PlaceContactRecord(
                c.Id, c.Kind, c.Value, c.Label, c.IsPublic,
                r.OrgName, c.OrganizationId,
                IsProvisional: c.IsPublic && c.ConfirmedByVenueUtc is null,
                CanRemove: canRemove,
                CanConfirm: c.IsPublic && c.ConfirmedByVenueUtc is null && viewerAnswersForVenue));
        }

        string? whyNoPublic = venue is not null && !viewerAnswersForVenue && !IsSuperAdminSafe()
            ? $"{venue.Organization.Name} runs this place, so its public details are theirs to keep. "
              + "Your group can add private notes."
            : null;

        return new(records, venue?.Organization.Name, whyNoPublic);
    }

    private bool IsSuperAdminSafe() => User?.Identity?.IsAuthenticated == true && IsSuperAdmin;
}
