using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// CRUD for client investigation requests. Each user manages their own requests.
/// SuperAdmin can view all. Org members can see requests submitted to their org.
/// </summary>
[ApiController]
[Route("api/client-requests")]
[Authorize]
public sealed class ClientRequestController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public ClientRequestController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Returns all requests belonging to the current user.</summary>
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<ClientRequestRecord>>> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var requests = await db.ClientRequests
            .AsNoTracking()
            .Where(r => r.AppUserId == userId)
            .OrderByDescending(r => r.DateCreated)
            .ToListAsync(ct);

        var ids = requests.Select(r => r.Id).ToList();
        var orgCounts = await db.ClientRequestOrganizations.AsNoTracking()
            .Where(o => ids.Contains(o.ClientRequestId))
            .GroupBy(o => o.ClientRequestId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return Ok(requests.Select(r =>
            _mapper.Map<ClientRequestRecord>(r) with { OrgCount = orgCounts.GetValueOrDefault(r.Id) }));
    }

    /// <summary>Returns a single request. Only the owner or SuperAdmin can access it.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientRequestRecord>> GetById(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var r = await db.ClientRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        if (r.AppUserId != userId && !User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin))
            return Forbid();
        return Ok(_mapper.Map<ClientRequestRecord>(r));
    }

    /// <summary>Returns the org applications for a request (owner or SuperAdmin).</summary>
    [HttpGet("{id:guid}/organizations")]
    public async Task<ActionResult<IEnumerable<ClientRequestOrganizationRecord>>> GetOrganizations(
        Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var r = await db.ClientRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        if (r.AppUserId != userId && !User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin))
            return Forbid();

        var apps = await db.ClientRequestOrganizations
            .AsNoTracking()
            .Include(a => a.Organization)
            .Where(a => a.ClientRequestId == id)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<ClientRequestOrganizationRecord>>(apps));
    }

    /// <summary>Creates a new draft request for the current user.</summary>
    [HttpPost]
    public async Task<ActionResult<ClientRequestRecord>> Create(
        [FromBody] UpsertClientRequestRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new ClientRequest
        {
            Id                 = Guid.NewGuid(),
            AppUserId          = userId,
            Status             = ClientRequestStatus.Draft,
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
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.ClientRequests.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id },
            _mapper.Map<ClientRequestRecord>(entity));
    }

    /// <summary>Updates a draft request. Only the owner may update, and only while in Draft status.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClientRequestRecord>> Update(
        Guid id, [FromBody] UpsertClientRequestRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ClientRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.AppUserId != userId) return Forbid();
        if (entity.Status != ClientRequestStatus.Draft)
            return BadRequest("Only draft requests can be edited.");

        entity.StreetAddress1     = request.StreetAddress1.Trim();
        entity.StreetAddress2     = request.StreetAddress2?.Trim();
        entity.City               = request.City.Trim();
        entity.State              = request.State.Trim();
        entity.ZipCode            = request.ZipCode.Trim();
        entity.Country            = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim();
        entity.Latitude           = request.Latitude;
        entity.Longitude          = request.Longitude;
        entity.Gender             = request.Gender;
        entity.BirthYear          = request.BirthYear;
        entity.Description        = request.Description?.Trim();
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ClientRequestRecord>(entity));
    }

    /// <summary>
    /// Submits the request to up to 2 organizations.
    /// Validates address is geocoded, description is present.
    /// Creates ClientRequestOrganization entries and changes status to Submitted.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult<ClientRequestRecord>> Submit(
        Guid id, [FromBody] SubmitClientRequestRequest request, CancellationToken ct)
    {
        // The org-count rules first, before the row is loaded: they need nothing from it.
        var orgIds = request.OrganizationIds.ToList();
        var countProblem = ClientRequestRules.CheckSubmission(0m, 0m, "x", orgIds);
        if (countProblem is not null) return BadRequest(countProblem);

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ClientRequests
            .Include(r => r.OrganizationApplications)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.AppUserId != userId) return Forbid();
        if (entity.Status != ClientRequestStatus.Draft)
            return BadRequest("Only draft requests can be submitted.");

        // The same rules the signed-out door applies — see ClientRequestRules.
        var problem = ClientRequestRules.CheckSubmission(entity.Latitude, entity.Longitude, entity.Description, orgIds)
                   ?? await ClientRequestRules.CheckOrganizationsExistAsync(db, orgIds, ct);
        if (problem is not null) return BadRequest(problem);

        var now = DateTime.UtcNow;
        foreach (var orgId in orgIds)
        {
            db.ClientRequestOrganizations.Add(new ClientRequestOrganization
            {
                Id                 = Guid.NewGuid(),
                ClientRequestId    = id,
                OrganizationId     = orgId,
                Status             = ClientOrgRequestStatus.Pending,
                DateApplied        = now,
                DateCreated        = now,
                CreatedByAppUserId = userId,
            });
        }

        entity.Status             = ClientRequestStatus.Submitted;
        entity.DateUpdated        = now;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ClientRequestRecord>(entity));
    }

    /// <summary>Withdraws a submitted (or draft) request, cancelling any still-open organization applications.</summary>
    [HttpPost("{id:guid}/withdraw")]
    public async Task<ActionResult<ClientRequestRecord>> Withdraw(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ClientRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.AppUserId != userId) return Forbid();
        if (entity.Status == ClientRequestStatus.Assigned)
            return BadRequest("An assigned case cannot be withdrawn without contacting the organization.");

        var now = DateTime.UtcNow;

        var openApps = await db.ClientRequestOrganizations
            .Where(a => a.ClientRequestId == id &&
                (a.Status == ClientOrgRequestStatus.Pending || a.Status == ClientOrgRequestStatus.Viewed ||
                 a.Status == ClientOrgRequestStatus.UnderReview))
            .ToListAsync(ct);
        foreach (var a in openApps) { a.Status = ClientOrgRequestStatus.Cancelled; a.DateResponded = now; }

        entity.Status             = ClientRequestStatus.Withdrawn;
        entity.DateUpdated        = now;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ClientRequestRecord>(entity));
    }

    /// <summary>
    /// Deletes a draft. Only a draft: anything a group has seen is withdrawn, not erased.
    /// </summary>
    /// <remarks>
    /// W-R5 in the 2026-09-06 evaluation: a draft the person abandoned was offered "Withdraw",
    /// which left a Withdrawn card on their list for a request nobody ever received. A draft that
    /// was never sent has nothing to withdraw from; it is simply removed, files and all.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ClientRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.AppUserId != userId) return Forbid();
        if (entity.Status != ClientRequestStatus.Draft)
            return BadRequest("Only a draft can be deleted. A request a group has received can be withdrawn instead.");

        db.ClientRequests.Remove(entity);   // applications and file links cascade; the files themselves stay the person's
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Parked requests (site evaluation 2026-09-06, phase 1) ────────────────
    //
    // A request submitted from the signed-out wizard under an email that already has an account
    // is parked in PendingClientRequests and the holder is emailed a link carrying a secret.
    // These three endpoints are what that link reaches. Every one of them answers 404 for a row
    // that is missing, expired, keyed wrongly, OR belongs to a different address — one answer,
    // so the link cannot be used to learn anything about a row that is not the caller's.

    /// <summary>The parked request, for the adopt page to show before asking "is this yours?".</summary>
    [HttpGet("pending/{id:guid}")]
    public async Task<ActionResult<PendingClientRequestRecord>> GetPending(
        Guid id, [FromQuery] string? key, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var row = await FindMyPendingAsync(db, id, key, ct);
        if (row is null) return NotFound();

        var orgIds = ParseOrgIds(row.OrganizationIdsJson);
        var names  = await db.Organizations.AsNoTracking()
            .Where(o => orgIds.Contains(o.Id))
            .Select(o => o.Name)
            .ToListAsync(ct);

        return Ok(new PendingClientRequestRecord(
            row.Id, row.DisplayName, row.StreetAddress1, row.StreetAddress2, row.City, row.State, row.ZipCode,
            names, row.DateCreated));
    }

    /// <summary>
    /// Makes the parked request the caller's own: a real Submitted request with its organisation
    /// applications, exactly as if they had been signed in when they typed it.
    /// </summary>
    /// <remarks>
    /// A group that was chosen and has since disappeared is skipped rather than failing the
    /// adoption; if none remain the request is still created, as a draft the person can send on.
    /// </remarks>
    [HttpPost("pending/{id:guid}/adopt")]
    public async Task<ActionResult<ClientRequestRecord>> AdoptPending(
        Guid id, [FromQuery] string? key, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var row = await FindMyPendingAsync(db, id, key, ct);
        if (row is null) return NotFound();

        var orgIds = ParseOrgIds(row.OrganizationIdsJson);
        var living = await db.Organizations.AsNoTracking()
            .Where(o => orgIds.Contains(o.Id))
            .Select(o => o.Id)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var entity = new ClientRequest
        {
            Id                 = Guid.NewGuid(),
            AppUserId          = userId,
            Status             = living.Count > 0 ? ClientRequestStatus.Submitted : ClientRequestStatus.Draft,
            StreetAddress1     = row.StreetAddress1,
            StreetAddress2     = row.StreetAddress2,
            City               = row.City,
            State              = row.State,
            ZipCode            = row.ZipCode,
            Country            = row.Country,
            Latitude           = row.Latitude,
            Longitude          = row.Longitude,
            Gender             = row.Gender,
            BirthYear          = row.BirthYear,
            Description        = row.Description,
            DateCreated        = now,
            CreatedByAppUserId = userId,
        };
        db.ClientRequests.Add(entity);
        foreach (var orgId in living)
        {
            db.ClientRequestOrganizations.Add(new ClientRequestOrganization
            {
                Id                 = Guid.NewGuid(),
                ClientRequestId    = entity.Id,
                OrganizationId     = orgId,
                Status             = ClientOrgRequestStatus.Pending,
                DateApplied        = now,
                DateCreated        = now,
                CreatedByAppUserId = userId,
            });
        }
        db.PendingClientRequests.Remove(row);
        await db.SaveChangesAsync(ct);

        return Ok(_mapper.Map<ClientRequestRecord>(entity));
    }

    /// <summary>"Not mine" — the parked request is discarded and nothing else happens.</summary>
    [HttpPost("pending/{id:guid}/discard")]
    public async Task<IActionResult> DiscardPending(Guid id, [FromQuery] string? key, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var row = await FindMyPendingAsync(db, id, key, ct);
        if (row is null) return NotFound();

        db.PendingClientRequests.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// The parked row, only if it is this caller's: same address as the signed-in account, the
    /// secret from the link, and not yet expired. Null for anything else.
    /// </summary>
    private async Task<PendingClientRequest?> FindMyPendingAsync(
        BenDataContext db, Guid id, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var userId = GetCurrentUserId();
        var myEmail = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.NormalizedEmail)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(myEmail)) return null;

        var row = await db.PendingClientRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return null;
        if (row.DateExpires < DateTime.UtcNow) return null;
        if (!string.Equals(row.NormalizedEmail, myEmail, StringComparison.Ordinal)) return null;

        var expected = Public.PublicClientRequestController.HashSecret(key);
        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(row.SecretHash);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b) ? row : null;
    }

    private static List<Guid> ParseOrgIds(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json) ?? []; }
        catch { return []; }
    }

    /// <summary>
    /// Adds one more organization to a Declined or Withdrawn request, re-opening it as Submitted.
    /// Subject to the same 2-organization cap as the initial submission.
    /// </summary>
    [HttpPost("{id:guid}/add-organization")]
    public async Task<ActionResult<ClientRequestRecord>> AddOrganization(
        Guid id, [FromBody] AddOrganizationRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ClientRequests
            .Include(r => r.OrganizationApplications)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.AppUserId != userId) return Forbid();
        if (entity.Status is not (ClientRequestStatus.Declined or ClientRequestStatus.Withdrawn))
            return BadRequest("Only a declined or withdrawn request can be sent to another organization.");
        if (entity.OrganizationApplications.Count >= 2)
            return BadRequest("You may apply to a maximum of 2 organizations.");
        if (entity.OrganizationApplications.Any(a => a.OrganizationId == request.OrganizationId))
            return BadRequest("This organization has already been applied to.");

        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, ct);
        if (org is null) return BadRequest("Organization not found.");
        if (!org.IsAcceptingClients) return BadRequest("This organization is not accepting new requests.");

        var now = DateTime.UtcNow;
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization
        {
            Id                 = Guid.NewGuid(),
            ClientRequestId    = id,
            OrganizationId     = request.OrganizationId,
            Status             = ClientOrgRequestStatus.Pending,
            DateApplied        = now,
            DateCreated        = now,
            CreatedByAppUserId = userId,
        });

        entity.Status             = ClientRequestStatus.Submitted;
        entity.DateUpdated        = now;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ClientRequestRecord>(entity));
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public sealed record UpsertClientRequestRequest(
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude,
    Ben.Data.Common.Enums.ClientGender Gender,
    int? BirthYear,
    string? Description);

public sealed record SubmitClientRequestRequest(IList<Guid> OrganizationIds);

public sealed record AddOrganizationRequest(Guid OrganizationId);
