using Ben.Data.Common.Mail;
using Ben.Data.Common;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A group claiming to run a place, proving it, and the groups who know the place objecting
/// (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para>See <see cref="VenueClaims"/> for the rules. What this adds is who may do what: a claim is
/// made and proved by somebody who answers for the claiming group, an objection by somebody who
/// answers for a group that knows the place, and nothing here can move an event.</para>
///
/// <para><b>A claim never touches an event.</b> It decides who must say yes to FUTURE events at the
/// address. There is deliberately no write to <c>HostedEvents</c> anywhere in this file, and
/// <c>VenuePlaceClaimTests</c> holds it to that.</para>
/// </remarks>
[ApiController]
[Authorize]
public sealed class VenueClaimController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IOrganizationSecurityService _security;
    private readonly PlatformMessageService _messages;
    private readonly IEmailService _email;
    private readonly SiteIdentity _site;
    private readonly UserManager<AppUser>? _users;

    public VenueClaimController(
        IDbContextFactory<BenDataContext> dbFactory, IOrganizationSecurityService security,
        PlatformMessageService messages, IEmailService email, IOptions<SiteIdentity> site,
        UserManager<AppUser>? users = null)
    {
        _dbFactory = dbFactory; _security = security; _messages = messages;
        _email = email; _site = site.Value; _users = users;
    }

    // ── the claimant ─────────────────────────────────────────────────────────

    /// <summary>What this group would be claiming at a place, and how it could prove it.</summary>
    [HttpGet("api/organizations/{orgId:guid}/venue-claims/start")]
    public async Task<ActionResult<VenueClaimStartRecord>> Start(Guid orgId, [FromQuery] Guid place, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await AnswersForAsync(userId, orgId, ct)) return Forbid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var placeRow = await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == place, ct);
        if (placeRow is null) return NotFound("We can't find that place.");

        var venue = await VenueGrants.VerifiedVenueAtAsync(db, place, ct);
        var proving = await VenueClaims.ProvingContactsAsync(db, place, orgId, DateTime.UtcNow, ct);

        var open = await db.VenuePlaceClaims.AsNoTracking()
            .Where(c => c.PlaceId == place && c.OrganizationId == orgId && VenueClaims.Open.Contains(c.State))
            .Select(c => c.Id).FirstOrDefaultAsync(ct);

        return Ok(new VenueClaimStartRecord(
            place, placeRow.Name ?? placeRow.StreetAddress1 ?? "This place",
            venue?.Organization.Name,
            [.. proving.Select(c => new ProvingContactRecord(c.Id, VenueClaims.Mask(c.Value), c.Label))],
            open == Guid.Empty ? null : await RecordAsync(db, open, userId, ct)));
    }

    /// <summary>Claims to run a place: a code to a proving address, or a review by a person.</summary>
    [HttpPost("api/organizations/{orgId:guid}/venue-claims")]
    public async Task<ActionResult<VenueClaimRecord>> Claim(
        Guid orgId, [FromBody] StartVenueClaimRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await AnswersForAsync(userId, orgId, ct)) return Forbid();
        if (!Enum.IsDefined(request.Role)) return BadRequest("Say whether you own it, manage it, or act for it.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var place = await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PlaceId, ct);
        if (place is null) return NotFound("We can't find that place.");

        if (await VenueGrants.VerifiedVenueAtAsync(db, request.PlaceId, ct) is { } venue)
            return Conflict(venue.OrganizationId == orgId
                ? "Your group is already confirmed as the venue there."
                : $"{venue.Organization.Name} is already confirmed as the venue there. If that is wrong, "
                  + "contact support rather than claiming it again.");

        if (await db.VenuePlaceClaims.AnyAsync(c => c.PlaceId == request.PlaceId && c.OrganizationId == orgId
                                                 && VenueClaims.Open.Contains(c.State), ct))
            return Conflict("Your group already has a claim open for this place.");

        var evidence = request.Evidence?.Trim() is { Length: > 0 } e ? (e.Length > 4000 ? e[..4000] : e) : null;
        var now = DateTime.UtcNow;

        PlaceContact? proving = null;
        if (request.PlaceContactId is Guid contactId)
        {
            proving = (await VenueClaims.ProvingContactsAsync(db, request.PlaceId, orgId, now, ct))
                .FirstOrDefault(c => c.Id == contactId);
            if (proving is null)
                return BadRequest("That address can't prove this claim. Choose one from the list, or ask for a review.");
            // No refusal when mail is not set up: the code is queued all the same and waits in the
            // outbox until it is, readable by a site administrator at /admin/mail meanwhile.
        }
        else if (evidence is null)
        {
            return BadRequest("Without an address to send a code to, a person reviews the claim. "
                            + "Tell them what shows you run the place — a licence, a listing, your role there.");
        }

        var claim = new VenuePlaceClaim
        {
            Id = Guid.NewGuid(), PlaceId = request.PlaceId, OrganizationId = orgId,
            ClaimantAppUserId = userId, ClaimantRole = request.Role, Evidence = evidence,
            PlaceContactId = proving?.Id,
            DateCreated = now, CreatedByAppUserId = userId,
        };
        db.VenuePlaceClaims.Add(claim);

        string? code = null;
        if (proving is not null)
        {
            (code, claim.CodeHash) = VenueClaims.NewCode();
            claim.CodeSentUtc = now;
        }

        await db.SaveChangesAsync(ct);

        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Name).FirstAsync(ct);
        var placeName = place.Name ?? "a place";

        if (proving is not null)
        {
            await SendCodeAsync(proving.Value, orgName, placeName, code!, ct);
        }
        else
        {
            await TellReviewersAsync(userId,
                $"{orgName} claims to run {placeName}",
                $"<p><strong>{VenueNotices.Safe(orgName)}</strong> claims to be the venue at "
                + $"<strong>{VenueNotices.Safe(placeName)}</strong> and has asked for a review.</p>"
                + $"<p><a href=\"/admin/venue-claims\">Review it</a></p>", ct);
        }

        return Ok(await RecordAsync(db, claim.Id, userId, ct));
    }

    /// <summary>The code from the venue's inbox.</summary>
    [HttpPost("api/organizations/{orgId:guid}/venue-claims/{claimId:guid}/code")]
    public async Task<ActionResult<VenueClaimRecord>> Code(
        Guid orgId, Guid claimId, [FromBody] VenueClaimCodeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await AnswersForAsync(userId, orgId, ct)) return Forbid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var claim = await db.VenuePlaceClaims.Include(c => c.Place)
            .FirstOrDefaultAsync(c => c.Id == claimId && c.OrganizationId == orgId, ct);
        if (claim is null) return NotFound();

        var now = DateTime.UtcNow;
        var refusal = VenueClaims.Check(claim, request.Code, now);
        if (refusal is not null)
        {
            claim.DateUpdated = now;
            await db.SaveChangesAsync(ct);
            return BadRequest(refusal);
        }

        claim.State = VenueClaimState.Proved;
        claim.ProvedUtc = now;
        claim.ObjectionsCloseUtc = now + VenueClaims.ObjectionWindow;
        claim.CodeHash = null;
        claim.DateUpdated = now;
        claim.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        // Everybody who knows the place hears, and has the week to say it is wrong.
        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Name).FirstAsync(ct);
        var placeName = claim.Place.Name ?? "a place";
        var recipients = new HashSet<Guid> { claim.Place.CreatedByAppUserId };
        foreach (var group in await VenueClaims.InterestedGroupsAsync(db, claim.PlaceId, orgId, ct))
            recipients.UnionWith(await VenueNotices.PeopleWhoAnswerForAsync(db, group, ct));
        recipients.Remove(userId);

        await _messages.SendAsync(
            $"{orgName} says it runs {placeName}",
            $"<p><strong>{VenueNotices.Safe(orgName)}</strong> has proved it reads the venue's own email "
            + $"and says it runs <strong>{VenueNotices.Safe(placeName)}</strong>.</p>"
            + $"<p>Unless somebody objects, from {claim.ObjectionsCloseUtc:MM/dd/yyyy} other groups will need "
            + "its yes to publish events there. Nothing already booked changes.</p>"
            + $"<p><a href=\"/venue-claims/{claim.Id}\">If this is wrong, say so</a></p>",
            [.. recipients], userId, ct);

        return Ok(await RecordAsync(db, claim.Id, userId, ct));
    }

    /// <summary>A new code, when the first expired or was lost.</summary>
    [HttpPost("api/organizations/{orgId:guid}/venue-claims/{claimId:guid}/resend")]
    public async Task<ActionResult<VenueClaimRecord>> Resend(Guid orgId, Guid claimId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await AnswersForAsync(userId, orgId, ct)) return Forbid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var claim = await db.VenuePlaceClaims.Include(c => c.Place).Include(c => c.PlaceContact).Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.Id == claimId && c.OrganizationId == orgId, ct);
        if (claim is null) return NotFound();
        if (claim.State != VenueClaimState.Pending || claim.PlaceContact is null)
            return Conflict("There is no code to send for this claim.");

        var now = DateTime.UtcNow;
        if (claim.CodeSentUtc is { } last && now - last < TimeSpan.FromMinutes(2))
            return Conflict("A code went a moment ago. Give it a couple of minutes to arrive.");

        string code;
        (code, claim.CodeHash) = VenueClaims.NewCode();
        claim.CodeSentUtc = now;
        claim.CodeAttempts = 0;
        claim.DateUpdated = now;
        claim.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        await SendCodeAsync(claim.PlaceContact.Value, claim.Organization.Name, claim.Place.Name ?? "a place", code, ct);
        return Ok(await RecordAsync(db, claim.Id, userId, ct));
    }

    [HttpPost("api/organizations/{orgId:guid}/venue-claims/{claimId:guid}/withdraw")]
    public async Task<ActionResult<VenueClaimRecord>> Withdraw(Guid orgId, Guid claimId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await AnswersForAsync(userId, orgId, ct)) return Forbid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var claim = await db.VenuePlaceClaims.FirstOrDefaultAsync(c => c.Id == claimId && c.OrganizationId == orgId, ct);
        if (claim is null) return NotFound();
        if (!VenueClaims.Open.Contains(claim.State)) return Conflict("This claim is already settled.");

        claim.State = VenueClaimState.Withdrawn;
        claim.CodeHash = null;
        claim.DateUpdated = DateTime.UtcNow;
        claim.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(await RecordAsync(db, claim.Id, userId, ct));
    }

    // ── anybody who knows the place ──────────────────────────────────────────

    /// <summary>A claim, for the claimant, a group that may object, or a reviewer.</summary>
    [HttpGet("api/venue-claims/{claimId:guid}")]
    public async Task<ActionResult<VenueClaimRecord>> Get(Guid claimId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var record = await RecordAsync(db, claimId, userId, ct);
        if (record is null) return NotFound();

        var claimantSide = await AnswersForAsync(userId, record.OrganizationId, ct);
        if (!claimantSide && record.MayObjectFor.Count == 0 && !IsSuperAdmin) return NotFound();

        return Ok(record);
    }

    /// <summary>Says the claim is wrong. A person decides from here.</summary>
    [HttpPost("api/venue-claims/{claimId:guid}/object")]
    public async Task<ActionResult<VenueClaimRecord>> Object(
        Guid claimId, [FromBody] ObjectToVenueClaimRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (request.Reason?.Trim() is not { Length: > 0 } reason)
            return BadRequest("Say why it is wrong. The person deciding reads this.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var claim = await db.VenuePlaceClaims.Include(c => c.Place).Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null) return NotFound();

        var record = await RecordAsync(db, claimId, userId, ct);
        if (record is null || !record.MayObjectFor.Contains(request.OrganizationId)) return NotFound();

        if (claim.State is not (VenueClaimState.Pending or VenueClaimState.Proved))
            return Conflict(claim.State == VenueClaimState.Contested
                ? "Somebody has already objected. A person is deciding."
                : "This claim is already settled.");

        var now = DateTime.UtcNow;
        claim.State = VenueClaimState.Contested;
        claim.ObjectedUtc = now;
        claim.ObjectedByAppUserId = userId;
        claim.ObjectingOrganizationId = request.OrganizationId;
        claim.ObjectionText = reason.Length > 4000 ? reason[..4000] : reason;
        claim.DateUpdated = now;
        claim.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        var objector = await db.Organizations.AsNoTracking().Where(o => o.Id == request.OrganizationId).Select(o => o.Name).FirstAsync(ct);
        await TellReviewersAsync(userId,
            $"{objector} objects to {claim.Organization.Name} running {claim.Place.Name}",
            $"<p><strong>{VenueNotices.Safe(objector)}</strong> objects to <strong>{VenueNotices.Safe(claim.Organization.Name)}</strong> "
            + $"being confirmed as the venue at {VenueNotices.Safe(claim.Place.Name)}.</p>"
            + $"<p>They said: “{VenueNotices.Safe(claim.ObjectionText)}”</p><p><a href=\"/admin/venue-claims\">Decide it</a></p>", ct);

        await _messages.SendAsync(
            $"Your claim to run {claim.Place.Name} has been questioned",
            $"<p>{VenueNotices.Safe(objector)} says your group does not run {VenueNotices.Safe(claim.Place.Name)}. "
            + "A person will look at both sides and decide; you may be contacted.</p>",
            [claim.ClaimantAppUserId], userId, ct);

        return Ok(await RecordAsync(db, claimId, userId, ct));
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private bool IsSuperAdmin => User.IsInRole(RoleNames.SuperAdmin);

    private async Task<bool> AnswersForAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsSuperAdmin || await _security.HasAccessAsync(userId, orgId,
            OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private async Task SendCodeAsync(string to, string orgName, string placeName, string code, CancellationToken ct)
    {
        var body = $"<p>Somebody from <strong>{VenueNotices.Safe(orgName)}</strong> says they run "
                 + $"<strong>{VenueNotices.Safe(placeName)}</strong> and wants to be confirmed as its venue on "
                 + $"{VenueNotices.Safe(_site.Name)}.</p>"
                 + $"<p>If that is you, or somebody you asked to do it, give them this code:</p>"
                 + $"<p style=\"font-size:24px;letter-spacing:4px\"><strong>{code}</strong></p>"
                 + "<p>It works for 24 hours. If you don't know who this is, don't share it — nothing "
                 + "happens without the code.</p>";

        static Services.Mail.MailRows.Manual Named(string table, string name)
            => new(table, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Name"] = name });

        await _email.SendAsync(new EmailMessage(to, $"A code to confirm who runs {placeName}", body,
            Kind: MailKinds.VenueClaimCode.Key,
            // The code is the letter, so a template of this kind is REQUIRED to carry {ClaimCode};
            // until 2026-09-23 nothing declared or supplied it.
            Payload: Services.Mail.MailRows.For(MailKinds.VenueClaimCode,
                new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase) { ["ClaimCode"] = new(code) },
                Services.Mail.MailRows.Person(to, null), Named("Places", placeName), Named("Organizations", orgName))), ct);
    }

    private async Task TellReviewersAsync(Guid senderId, string subject, string body, CancellationToken ct)
    {
        if (_users is null) return;
        var reviewers = (await _users.GetUsersInRoleAsync(RoleNames.SuperAdmin)).Select(u => u.Id).ToList();
        await _messages.SendAsync(subject, body, reviewers, senderId, ct);
    }

    private async Task<VenueClaimRecord?> RecordAsync(BenDataContext db, Guid claimId, Guid viewerId, CancellationToken ct)
    {
        var row = await db.VenuePlaceClaims.AsNoTracking()
            .Where(c => c.Id == claimId)
            .Select(c => new
            {
                Claim = c, PlaceName = c.Place.Name, OrgName = c.Organization.Name,
                Claimant = c.ClaimantAppUser.DisplayName,
                Contact = c.PlaceContact != null ? c.PlaceContact.Value : null,
                Objector = c.ObjectingOrganization != null ? c.ObjectingOrganization.Name : null,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var c = row.Claim;
        var interested = await VenueClaims.InterestedGroupsAsync(db, c.PlaceId, c.OrganizationId, ct);
        var mayObjectFor = new List<Guid>();
        if (VenueClaims.Open.Contains(c.State))
            foreach (var group in interested)
                if (await AnswersForAsync(viewerId, group, ct) && (!IsSuperAdmin
                    || await db.OrganizationUserMemberships.AnyAsync(m => m.OrganizationId == group && m.AppUserId == viewerId && m.IsActive, ct)))
                    mayObjectFor.Add(group);

        return new VenueClaimRecord(
            c.Id, c.PlaceId, row.PlaceName ?? "", c.OrganizationId, row.OrgName, row.Claimant ?? "",
            c.ClaimantRole, c.Evidence, c.State,
            row.Contact is null ? null : VenueClaims.Mask(row.Contact),
            c.DateCreated, c.ProvedUtc, c.ObjectionsCloseUtc, row.Objector, c.ObjectionText,
            c.DecidedUtc, c.DecisionNote, mayObjectFor);
    }
}
