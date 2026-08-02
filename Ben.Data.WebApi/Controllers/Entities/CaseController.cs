using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Manages cases for an organization. Requires org Owner/Admin or SuperAdmin
/// for most write operations. Case managers can update their assigned cases.
/// </summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/cases")]
[Authorize]
public sealed class CaseController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public CaseController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseRecord>>> GetAll(Guid orgId, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var cases = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == orgId)
            .OrderByDescending(c => c.DateCaseOpened)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<CaseRecord>>(cases));
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<CaseRecord>> GetById(Guid orgId, Guid caseId, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var c = await db.Cases.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == orgId, ct);
        return c is null ? NotFound() : Ok(_mapper.Map<CaseRecord>(c));
    }

    // ── Create (internally proposed) ──────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<CaseRecord>> Create(
        Guid orgId, [FromBody] CreateCaseRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var entity = new Case
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            Status             = CaseStatus.Proposed,
            Title              = request.Title.Trim(),
            Description        = request.Description?.Trim(),
            StreetAddress1     = request.StreetAddress1.Trim(),
            StreetAddress2     = request.StreetAddress2?.Trim(),
            City               = request.City.Trim(),
            State              = request.State.Trim(),
            ZipCode            = request.ZipCode.Trim(),
            Country            = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim(),
            Latitude           = request.Latitude,
            Longitude          = request.Longitude,
            DateCaseOpened     = DateTime.UtcNow,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        var (yr, num) = await AssignCaseNumberAsync(db, orgId, entity.DateCaseOpened, ct);
        entity.CaseYear     = yr;
        entity.OrgCaseNumber = num;
        db.Cases.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { orgId, caseId = entity.Id },
            _mapper.Map<CaseRecord>(entity));
    }

    /// <summary>
    /// Returns all pending client-request applications submitted to this organization.
    /// Anonymized — exact address not included until accepted.
    /// </summary>
    [HttpGet("pending-requests")]
    public async Task<ActionResult<IEnumerable<OrgPendingRequestRecord>>> GetPendingRequests(
        Guid orgId, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var apps = await db.ClientRequestOrganizations
            .AsNoTracking()
            .Include(a => a.ClientRequest)
            .Where(a => a.OrganizationId == orgId &&
                (a.Status == ClientOrgRequestStatus.Pending ||
                 a.Status == ClientOrgRequestStatus.Viewed ||
                 a.Status == ClientOrgRequestStatus.UnderReview))
            .OrderByDescending(a => a.DateApplied)
            .ToListAsync(ct);

        var records = apps.Select(a => new OrgPendingRequestRecord
        {
            ClientRequestId = a.ClientRequestId,
            DateApplied     = a.DateApplied,
            DateSubmitted   = a.ClientRequest!.DateCreated,
            City            = a.ClientRequest.City,
            State           = a.ClientRequest.State,
            ZipCode         = a.ClientRequest.ZipCode,
            Description     = a.ClientRequest.Description,
            Latitude        = a.ClientRequest.Latitude,
            Longitude       = a.ClientRequest.Longitude,
            Status          = a.Status,
        });
        return Ok(records);
    }

    /// <summary>Updates a pending request's status to Viewed or UnderReview.</summary>
    [HttpPut("request-status/{clientRequestId:guid}")]
    public async Task<IActionResult> UpdateRequestStatus(
        Guid orgId, Guid clientRequestId, [FromBody] UpdateRequestStatusRequest request, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();
        if (request.Status is not ClientOrgRequestStatus.Viewed and not ClientOrgRequestStatus.UnderReview)
            return BadRequest("Only Viewed and UnderReview statuses may be set via this endpoint.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var application = await db.ClientRequestOrganizations
            .FirstOrDefaultAsync(a => a.ClientRequestId == clientRequestId && a.OrganizationId == orgId, ct);
        if (application is null) return NotFound();

        // Only advance the status — never move backward
        if ((int)request.Status > (int)application.Status)
            application.Status = request.Status;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Declines a pending (or viewed/under-review) client-request application for this organization.</summary>
    [HttpPost("decline-request/{clientRequestId:guid}")]
    public async Task<ActionResult> DeclineClientRequest(Guid orgId, Guid clientRequestId, CancellationToken ct)
    {
        if (!await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var application = await db.ClientRequestOrganizations
            .FirstOrDefaultAsync(a => a.ClientRequestId == clientRequestId && a.OrganizationId == orgId, ct);
        if (application is null) return NotFound();
        if (application.Status is ClientOrgRequestStatus.Accepted or ClientOrgRequestStatus.Rejected or ClientOrgRequestStatus.Cancelled)
            return BadRequest("This application has already been responded to.");

        application.Status               = ClientOrgRequestStatus.Rejected;
        application.DateResponded        = DateTime.UtcNow;
        application.RespondedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Accepts a pending <see cref="ClientRequest"/> and promotes it to a Case.
    /// Auto-generates 4 standard CMS pages linked to the case.
    /// </summary>
    [HttpPost("accept-client-request/{clientRequestId:guid}")]
    public async Task<ActionResult<CaseRecord>> AcceptClientRequest(
        Guid orgId, Guid clientRequestId, [FromBody] AcceptClientRequestAsCaseRequest request,
        CancellationToken ct)
    {
        if (!await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        // Validate the org application exists and is pending
        var application = await db.ClientRequestOrganizations
            .Include(a => a.ClientRequest)
            .FirstOrDefaultAsync(a => a.ClientRequestId == clientRequestId
                                   && a.OrganizationId == orgId, ct);
        if (application is null) return NotFound("Client request application not found for this organization.");
        if (application.Status is ClientOrgRequestStatus.Accepted or ClientOrgRequestStatus.Cancelled)
            return BadRequest("This application has already been responded to.");
        if (application.ClientRequest is null) return NotFound("Client request not found.");

        var clientReq = application.ClientRequest;
        var now       = DateTime.UtcNow;

        // Accept the application
        application.Status               = ClientOrgRequestStatus.Accepted;
        application.DateResponded        = now;
        application.RespondedByAppUserId = userId == Guid.Empty ? null : userId;

        // Cancel all other pending applications for this request
        var otherApps = await db.ClientRequestOrganizations
            .Where(a => a.ClientRequestId == clientRequestId
                     && a.OrganizationId != orgId
                     && a.Status == ClientOrgRequestStatus.Pending)
            .ToListAsync(ct);
        foreach (var a in otherApps) { a.Status = ClientOrgRequestStatus.Cancelled; a.DateResponded = now; }

        clientReq.Status = ClientRequestStatus.Assigned;

        // Derive title: "{Surname}, {City} {State}" — case manager can rename later
        var clientName = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == clientReq.AppUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);
        var surname    = ExtractSurname(clientName);
        var caseTitle  = string.IsNullOrWhiteSpace(request.Title)
            ? $"{surname}, {clientReq.City} {clientReq.State}"
            : request.Title.Trim();

        var newCase = new Case
        {
            Id                    = Guid.NewGuid(),
            OrganizationId        = orgId,
            ClientRequestId       = clientRequestId,
            CaseManagerAppUserId  = request.CaseManagerAppUserId,
            Status                = CaseStatus.Accepted,
            Title                 = caseTitle,
            StreetAddress1        = clientReq.StreetAddress1,
            StreetAddress2        = clientReq.StreetAddress2,
            City                  = clientReq.City,
            State                 = clientReq.State,
            ZipCode               = clientReq.ZipCode,
            Country               = clientReq.Country,
            Latitude              = clientReq.Latitude,
            Longitude             = clientReq.Longitude,
            DateCaseOpened        = now,
            DateCreated           = now,
            CreatedByAppUserId    = userId,
        };
        var (yr, num) = await AssignCaseNumberAsync(db, orgId, now, ct);
        newCase.CaseYear      = yr;
        newCase.OrgCaseNumber = num;
        db.Cases.Add(newCase);
        await db.SaveChangesAsync(ct);

        // Auto-generate standard CMS pages
        await AutoGenerateCmsPagesAsync(db, orgId, newCase.Id, caseTitle, newCase.CaseYear, newCase.OrgCaseNumber, userId, ct);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { orgId, caseId = newCase.Id },
            _mapper.Map<CaseRecord>(newCase));
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [HttpPut("{caseId:guid}")]
    public async Task<ActionResult<CaseRecord>> Update(
        Guid orgId, Guid caseId, [FromBody] UpdateCaseRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.Cases.FirstOrDefaultAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        // Case manager can update their own case; org admin/super can update any
        bool isCaseManager = entity.CaseManagerAppUserId == userId;
        if (!isCaseManager && !await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();

        entity.Title                = request.Title?.Trim() ?? entity.Title;
        entity.Description          = request.Description?.Trim();
        entity.Status               = request.Status;
        entity.PublicPseudonym      = request.PublicPseudonym?.Trim();
        entity.IsPublic             = request.IsPublic;
        entity.CaseManagerAppUserId = request.CaseManagerAppUserId;
        if (request.Status is CaseStatus.Closed or CaseStatus.Haunted or CaseStatus.Public && entity.DateCaseClosed is null)
            entity.DateCaseClosed = DateTime.UtcNow;
        entity.DateUpdated          = DateTime.UtcNow;
        entity.UpdatedByAppUserId   = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<CaseRecord>(entity));
    }

    // ── Timeline entries ──────────────────────────────────────────────────────

    [HttpGet("{caseId:guid}/timeline")]
    public async Task<ActionResult<IEnumerable<CaseTimelineEntryRecord>>> GetTimeline(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var exists = await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct);
        if (!exists) return NotFound();

        var entries = await db.CaseTimelineEntries
            .AsNoTracking()
            .Include(e => e.AuthorAppUser)
            .Include(e => e.ExperienceTypes)
            .Where(e => e.CaseId == caseId)
            .OrderBy(e => e.EventDateTime ?? e.DateCreated)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<CaseTimelineEntryRecord>>(entries));
    }

    [HttpPost("{caseId:guid}/timeline")]
    public async Task<ActionResult<CaseTimelineEntryRecord>> AddTimelineEntry(
        Guid orgId, Guid caseId, [FromBody] UpsertTimelineEntryRequest request, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct))
            return NotFound();

        var entry = new CaseTimelineEntry
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            AuthorAppUserId    = userId,
            EntryType          = request.EntryType,
            EventDateTime      = request.EventDateTime,
            Title              = request.Title?.Trim(),
            Body               = request.Body?.Trim(),
            IsPublic           = request.IsPublic,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.CaseTimelineEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        // Add experience type tags
        foreach (var typeId in request.ExperienceTypeIds.Distinct())
        {
            db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
            {
                CaseTimelineEntryId = entry.Id,
                ExperienceTypeId    = typeId,
            });
        }
        if (request.ExperienceTypeIds.Any()) await db.SaveChangesAsync(ct);

        var loaded = await db.CaseTimelineEntries
            .AsNoTracking()
            .Include(e => e.AuthorAppUser)
            .Include(e => e.ExperienceTypes)
            .FirstAsync(e => e.Id == entry.Id, ct);
        return CreatedAtAction(nameof(GetTimeline), new { orgId, caseId },
            _mapper.Map<CaseTimelineEntryRecord>(loaded));
    }

    [HttpPut("{caseId:guid}/timeline/{entryId:guid}")]
    public async Task<ActionResult<CaseTimelineEntryRecord>> UpdateTimelineEntry(
        Guid orgId, Guid caseId, Guid entryId, [FromBody] UpsertTimelineEntryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entry = await db.CaseTimelineEntries
            .Include(e => e.ExperienceTypes)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId, ct);
        if (entry is null) return NotFound();

        // Author or org admin can edit
        if (entry.AuthorAppUserId != userId && !await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();

        entry.EntryType          = request.EntryType;
        entry.EventDateTime      = request.EventDateTime;
        entry.Title              = request.Title?.Trim();
        entry.Body               = request.Body?.Trim();
        entry.IsPublic           = request.IsPublic;
        entry.DateUpdated        = DateTime.UtcNow;
        entry.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;

        // Replace experience type tags
        db.CaseTimelineEntryExperienceTypes.RemoveRange(entry.ExperienceTypes);
        foreach (var typeId in request.ExperienceTypeIds.Distinct())
        {
            db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
            {
                CaseTimelineEntryId = entry.Id,
                ExperienceTypeId    = typeId,
            });
        }
        await db.SaveChangesAsync(ct);

        var loaded = await db.CaseTimelineEntries
            .AsNoTracking()
            .Include(e => e.AuthorAppUser)
            .Include(e => e.ExperienceTypes)
            .FirstAsync(e => e.Id == entry.Id, ct);
        return Ok(_mapper.Map<CaseTimelineEntryRecord>(loaded));
    }

    [HttpDelete("{caseId:guid}/timeline/{entryId:guid}")]
    public async Task<IActionResult> DeleteTimelineEntry(
        Guid orgId, Guid caseId, Guid entryId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entry = await db.CaseTimelineEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId, ct);
        if (entry is null) return NotFound();
        if (entry.AuthorAppUserId != userId && !await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        db.CaseTimelineEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Auto-assign case number ───────────────────────────────────────────────

    private static async Task<(int year, int number)> AssignCaseNumberAsync(
        BenDataContext db, Guid orgId, DateTime openedAt, CancellationToken ct)
    {
        int year = openedAt.Year;
        int max  = await db.Cases
            .Where(c => c.OrganizationId == orgId && c.CaseYear == year)
            .MaxAsync(c => (int?)c.OrgCaseNumber, ct) ?? 0;
        return (year, max + 1);
    }

    /// <summary>
    /// Extracts a display-friendly surname from a DisplayName.
    /// "John Smith" → "Smith", "AverageBen" → "AverageBen"
    /// </summary>
    private static string ExtractSurname(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Unknown";
        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1] : parts[0];
    }

    // ── Auto-generate CMS pages ───────────────────────────────────────────────

    private static async Task AutoGenerateCmsPagesAsync(
        BenDataContext db, Guid orgId, Guid caseId, string caseTitle, int caseYear, int caseNumber,
        Guid userId, CancellationToken ct)
    {
        var caseRef = $"#{caseYear}-{caseNumber:D3}";
        var pages = new[]
        {
            (Title: $"{caseRef} — Summary",                 Slug: $"cases/{caseId}/summary",  Sort: 1),
            (Title: $"{caseRef} — Investigation Findings",  Slug: $"cases/{caseId}/findings", Sort: 2),
            (Title: $"{caseRef} — Research & History",      Slug: $"cases/{caseId}/research", Sort: 3),
            (Title: $"{caseRef} — Timeline",                Slug: $"cases/{caseId}/timeline", Sort: 4),
        };

        foreach (var (title, slug, sort) in pages)
        {
            db.OrganizationPages.Add(new OrganizationPage
            {
                Id                 = Guid.NewGuid(),
                OrganizationId     = orgId,
                CaseId             = caseId,
                PageTitle          = title,
                UrlName            = slug,
                PageHtml           = "",
                IsPublished        = false,
                IsPublic           = false,
                SortOrder          = sort,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
        }
        await Task.CompletedTask;
    }

    // ── Auth helpers ──────────────────────────────────────────────────────────

    private async Task<bool> CanReadAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;
        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.OrganizationUserMemberships.AnyAsync(
            m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
    }

    private async Task<bool> IsOrgAdminOrSuperAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;
        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.OrganizationUserMemberships.AnyAsync(
            m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
              && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator), ct);
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public sealed record CreateCaseRequest(
    string Title,
    string? Description,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude);

public sealed record AcceptClientRequestAsCaseRequest(
    string? Title,
    Guid? CaseManagerAppUserId);

public sealed record UpdateCaseRequest(
    string? Title,
    string? Description,
    Ben.Data.Common.Enums.CaseStatus Status,
    string? PublicPseudonym,
    bool IsPublic,
    Guid? CaseManagerAppUserId);

public sealed record UpsertTimelineEntryRequest(
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string? Title,
    string? Body,
    bool IsPublic,
    IList<Guid> ExperienceTypeIds);

/// <summary>Updates a client-request org application to Viewed or UnderReview.</summary>
public sealed record UpdateRequestStatusRequest(
    Ben.Data.Common.Enums.ClientOrgRequestStatus Status);
