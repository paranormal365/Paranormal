using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
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
        if (request.OrganizationIds.Count == 0)
            return BadRequest("At least one organization is required.");
        if (request.OrganizationIds.Count > 2)
            return BadRequest("You may apply to a maximum of 2 organizations.");
        if (request.OrganizationIds.Distinct().Count() != request.OrganizationIds.Count)
            return BadRequest("Duplicate organizations are not allowed.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ClientRequests
            .Include(r => r.OrganizationApplications)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.AppUserId != userId) return Forbid();
        if (entity.Status != ClientRequestStatus.Draft)
            return BadRequest("Only draft requests can be submitted.");
        if (!entity.Latitude.HasValue || !entity.Longitude.HasValue)
            return BadRequest("The address must be geocoded before submitting.");
        if (string.IsNullOrWhiteSpace(entity.Description))
            return BadRequest("A description of your experiences is required before submitting.");

        var now = DateTime.UtcNow;
        foreach (var orgId in request.OrganizationIds)
        {
            var orgExists = await db.Organizations.AnyAsync(o => o.Id == orgId, ct);
            if (!orgExists) return BadRequest($"Organization {orgId} not found.");

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

    /// <summary>Withdraws a submitted (or draft) request.</summary>
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

        entity.Status             = ClientRequestStatus.Withdrawn;
        entity.DateUpdated        = DateTime.UtcNow;
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
