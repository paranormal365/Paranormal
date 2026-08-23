using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

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

    private readonly Services.Billing.SubscriptionLimitGuard _limits;

    public CaseController(
        IDbContextFactory<BenDataContext> db, IMapper mapper,
        Services.Billing.SubscriptionLimitGuard limits,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    {
        _db = db;
        _mapper = mapper;
        _limits = limits;
        _security = security;
    }

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    /// <summary>
    /// Admin-or-role, the Phase B shape (item 156): the historical admin gate stays exactly as
    /// it was, and a custom-role grant on the named table now ALSO opens the door. Purely
    /// additive — nobody loses anything, and a Case Manager Role or Client Manager Role becomes
    /// real. Owner/Administrator/SuperAdmin still pass through HasAccessAsync's own bypass, but
    /// the explicit admin check is kept in front so this reads as what it is: the old rule OR
    /// the new one.
    /// </summary>
    private async Task<bool> IsAdminOrHasAsync(
        Guid orgId, OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken ct)
        => await IsOrgAdminOrSuperAsync(orgId, ct)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId, table, action, ct);

    /// <summary>
    /// The subscription cap on concurrent work. Closed and later statuses do not count —
    /// capping total history would let a group's own past lock them out, and asking somebody
    /// to delete last year's investigation to start this year's is data loss, not a plan prompt.
    /// </summary>
    private async Task<string?> WhyNotAnotherOpenCaseAsync(
        BenDataContext db, Guid orgId, CancellationToken ct)
    {
        var open = await db.Cases.CountAsync(c =>
            c.OrganizationId == orgId && c.Status <= CaseStatus.Summarized, ct);

        return await _limits.WhyNotOneMoreAsync(
            orgId, Ben.Data.Common.Enums.SubscriptionLimit.OpenCases, open, ct);
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
            .Include(x => x.CaseManagerAppUser)
            .FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == orgId, ct);
        return c is null ? NotFound() : Ok(_mapper.Map<CaseRecord>(c));
    }

    /// <summary>
    /// The client request this case was created from, read-only.
    /// </summary>
    /// <remarks>
    /// <para>Exists because there was no way for an investigating org to read the request their own
    /// case came from: <c>GET api/client-requests/{id}</c> is owner-or-SuperAdmin only, and the
    /// case's own description is a snapshot that diverges the moment anyone edits it. Scoped to the
    /// org route so the same active-membership check as every other case action applies — the
    /// request contains a client's home address and demographics, and only the org handling the
    /// case has any business reading it.</para>
    /// <para>404 when the case has no originating request, which is normal: cases can be raised
    /// internally rather than from a client submission.</para>
    /// </remarks>
    [HttpGet("{caseId:guid}/client-request")]
    public async Task<ActionResult<CaseClientRequestRecord>> GetClientRequest(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);

        // Matched on both ids: a caseId from another org must not resolve just because the caller
        // belongs to the org they named in the route.
        var caseRow = await db.Cases.AsNoTracking()
            .Where(x => x.Id == caseId && x.OrganizationId == orgId)
            .Select(x => new { x.ClientRequestId })
            .FirstOrDefaultAsync(ct);
        if (caseRow is null) return NotFound("Case not found.");
        if (caseRow.ClientRequestId is not { } requestId)
            return NotFound("This case wasn't created from a client request.");

        var request = await db.ClientRequests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new CaseClientRequestRecord(
                r.Id,
                r.DateCreated,
                r.Description,
                r.StreetAddress1,
                r.StreetAddress2,
                r.City,
                r.State,
                r.ZipCode,
                r.Country,
                r.Gender,
                r.BirthYear,
                db.ClientRequestFiles
                  .Where(f => f.ClientRequestId == r.Id)
                  .OrderBy(f => f.DateCreated)
                  .Select(f => new CaseClientRequestFileRecord(
                      f.UploadFileId,
                      f.UploadFile.FileName,
                      f.UploadFile.ContentType,
                      f.UploadFile.FileSize))
                  .ToList()))
            .FirstOrDefaultAsync(ct);

        // The FK pointed at a row that no longer exists — report it as missing rather than 500.
        return request is null ? NotFound("The originating request no longer exists.") : Ok(request);
    }

    // ── Create (internally proposed) ──────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<CaseRecord>> Create(
        Guid orgId, [FromBody] CreateCaseRequest request, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Create, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (await WhyNotAnotherOpenCaseAsync(db, orgId, ct) is { } capped) return BadRequest(capped);

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
        if (!await IsAdminOrHasAsync(orgId, OrganizationSecurityTable.ClientRequest, OrganizationSecurityAction.Update, ct)) return Forbid();
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

        // If every organization this request was sent to has now declined it,
        // flip the parent request to Declined so the client can pick another org.
        var stillActive = await db.ClientRequestOrganizations
            .AnyAsync(a => a.ClientRequestId == clientRequestId &&
                (a.Status == ClientOrgRequestStatus.Pending || a.Status == ClientOrgRequestStatus.Viewed ||
                 a.Status == ClientOrgRequestStatus.UnderReview || a.Status == ClientOrgRequestStatus.Accepted), ct);
        if (!stillActive)
        {
            var clientRequest = await db.ClientRequests.FirstOrDefaultAsync(r => r.Id == clientRequestId, ct);
            if (clientRequest is not null && clientRequest.Status == ClientRequestStatus.Submitted)
            {
                clientRequest.Status      = ClientRequestStatus.Declined;
                clientRequest.DateUpdated = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

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
        if (!await IsAdminOrHasAsync(orgId, OrganizationSecurityTable.ClientRequest, OrganizationSecurityAction.Update, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        // The cap applies to accepting a request too — it opens a case just as surely as creating
        // one, and a cap only on the other door would simply move the traffic.
        if (await WhyNotAnotherOpenCaseAsync(db, orgId, ct) is { } capped) return BadRequest(capped);

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
            Description           = clientReq.Description,
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

        // Both saves must land together — otherwise a failure between them leaves an
        // Accepted Case with no CMS pages, and the guard above rejects retrying an
        // already-Accepted application. The in-memory provider used by tests doesn't
        // support transactions, so skip it there rather than fail every test.
        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await using var _ = transaction;
        await db.SaveChangesAsync(ct);

        // Auto-generate standard CMS pages
        await AutoGenerateCmsPagesAsync(db, orgId, newCase.Id, caseTitle, newCase.CaseYear, newCase.OrgCaseNumber, userId, ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);

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
        if (!isCaseManager && !await IsAdminOrHasAsync(orgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Update, ct)) return Forbid();

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

        if (await EnsurePublicSlugAsync(db, entity, ct) is string refusal)
            return BadRequest(refusal);

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<CaseRecord>(entity));
    }

    /// <summary>
    /// Gives a newly-public case its readable address, or explains why it cannot have one.
    /// </summary>
    /// <remarks>
    /// <para>Built from the <b>title</b>, which is already shown on the public case page — so the
    /// URL exposes nothing the page does not. That is why it is derived rather than typed: free
    /// text in a public address is a much easier way to publish something nobody meant to.</para>
    ///
    /// <para><b>A case is somebody's home.</b> A title carrying a street address is refused rather
    /// than quietly slugged, because a URL outlives the page — it sits in browser histories,
    /// referrer headers and pasted links long after anybody thinks about it, and would hand back
    /// exactly what redacting the coordinates was for.</para>
    ///
    /// <para>Assigned once. Renaming a case afterwards leaves the address alone, or every link
    /// somebody has already shared would break.</para>
    /// </remarks>
    private static async Task<string?> EnsurePublicSlugAsync(
        BenDataContext db, Case entity, CancellationToken ct)
    {
        var isPubliclyVisible = entity.IsPublic
            && entity.Status is CaseStatus.Public or CaseStatus.Haunted;

        if (!isPubliclyVisible || entity.UrlName is not null) return null;

        if (UrlSlug.LooksLikeAStreetAddress(entity.Title))
            return "This case's title looks like a street address, and the title becomes part of its "
                 + "public web address. Give it a name that doesn't identify the property — "
                 + "\"The Mill House Investigation\", for instance — before publishing it.";

        var candidate = UrlSlug.From(entity.Title)
                        ?? $"case-{entity.CaseYear}-{entity.OrgCaseNumber:D3}";

        entity.UrlName = await UrlSlug.MakeUniqueAsync(candidate, async slug =>
            await db.Cases.AnyAsync(c => c.OrganizationId == entity.OrganizationId
                                      && c.UrlName == slug
                                      && c.Id != entity.Id, ct));
        return null;
    }

    // ── Timeline entries ──────────────────────────────────────────────────────

    /// <summary>
    /// The case timeline, optionally narrowed to one investigation.
    /// </summary>
    /// <param name="orgId">Owning org.</param>
    /// <param name="caseId">The case.</param>
    /// <param name="investigationId">
    /// When supplied, returns only entries recorded during that investigation — the "binder" view.
    /// A binder is a filtered timeline rather than a separate store, so entries written in one show
    /// up on the case timeline automatically and carry the same visibility rules and attachments.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{caseId:guid}/timeline")]
    public async Task<ActionResult<IEnumerable<CaseTimelineEntryRecord>>> GetTimeline(
        Guid orgId, Guid caseId, [FromQuery] Guid? investigationId, CancellationToken ct)
    {
        if (!await CanReadAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var exists = await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct);
        if (!exists) return NotFound();

        var query = db.CaseTimelineEntries
            .AsNoTracking()
            .Include(e => e.AuthorAppUser)
            .Include(e => e.ExperienceTypes)
            .Include(e => e.Files).ThenInclude(f => f.UploadFile)
            // ClientOnly is history the client declined to carry into this organization after a
            // move (item 84) — the one visibility an org never sees, breaking the cumulative rule
            // by design.
            .Where(e => e.CaseId == caseId && e.Visibility != CaseTimelineVisibility.ClientOnly);

        if (investigationId is { } invId)
            query = query.Where(e => e.InvestigationId == invId);

        // Two people can report the same moment — or two unrelated things can happen at
        // the same moment — so ties on event time are expected, not an edge case. Sorting
        // on event time alone leaves tied entries in whatever order the provider returns,
        // which can differ between requests; a timeline that reshuffles isn't citable.
        // DateCreated breaks the tie by who logged it first, Id guarantees a total order.
        var entries = await query
            .OrderBy(e => e.EventDateTime ?? e.DateCreated)
            .ThenBy(e => e.DateCreated)
            .ThenBy(e => e.Id)
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

        // Item 84: a lapsed group reads everything and adds nothing — timeline entries included.
        if (await _limits.WhyReadOnlyAsync(orgId, ct) is { } readOnly) return BadRequest(readOnly);

        var entry = new CaseTimelineEntry
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            AuthorAppUserId    = userId,
            EntryType          = request.EntryType,
            EventDateTime      = request.EventDateTime,
            Title              = request.Title?.Trim(),
            Body               = request.Body?.Trim(),
            Visibility         = request.Visibility,
            InvestigationId    = request.InvestigationId,
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
            .Include(e => e.Files).ThenInclude(f => f.UploadFile)
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
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
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
        entry.Visibility         = request.Visibility;
        entry.InvestigationId    = request.InvestigationId;
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
            .Include(e => e.Files).ThenInclude(f => f.UploadFile)
            .FirstAsync(e => e.Id == entry.Id, ct);
        return Ok(_mapper.Map<CaseTimelineEntryRecord>(loaded));
    }

    [HttpDelete("{caseId:guid}/timeline/{entryId:guid}")]
    public async Task<IActionResult> DeleteTimelineEntry(
        Guid orgId, Guid caseId, Guid entryId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
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

    // Item 156 Phase D: reading cases answers to HasAccessAsync(Case, Read) — SuperAdmin,
    // owner/admin, the area gate, and the grants (grandfather bridge included) in one place.
    private async Task<bool> CanReadAsync(Guid orgId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               OrganizationSecurityTable.Case, OrganizationSecurityAction.Read, ct);

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
    Ben.Data.Common.Enums.CaseTimelineVisibility Visibility,
    IList<Guid> ExperienceTypeIds,
    Guid? InvestigationId = null);

/// <summary>Updates a client-request org application to Viewed or UnderReview.</summary>
public sealed record UpdateRequestStatusRequest(
    Ben.Data.Common.Enums.ClientOrgRequestStatus Status);
