using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Public read access to the approved experience category taxonomy.
/// SuperAdmin CRUD at /api/admin/experience-categories.
/// </summary>
[ApiController]
[Route("api/experience-categories")]
public sealed class ExperienceCategoryController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public ExperienceCategoryController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Returns all approved, active categories (public — no auth required).</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ExperienceCategoryRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var cats = await db.ExperienceCategories
            .AsNoTracking()
            .Where(c => c.IsApproved && c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<ExperienceCategoryRecord>>(cats));
    }

    /// <summary>Returns approved, active types for a category (public — no auth required).</summary>
    [HttpGet("{categoryId:guid}/types")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ExperienceTypeRecord>>> GetTypes(
        Guid categoryId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var types = await db.ExperienceTypes
            .AsNoTracking()
            .Where(t => t.ExperienceCategoryId == categoryId && t.IsApproved && t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<ExperienceTypeRecord>>(types));
    }

    /// <summary>Returns all categories with their approved types in a single call (public).</summary>
    [HttpGet("with-types")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ExperienceCategoryWithTypesResponse>>> GetAllWithTypes(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var cats = await db.ExperienceCategories
            .AsNoTracking()
            .Include(c => c.ExperienceTypes.Where(t => t.IsApproved && t.IsActive))
            .Where(c => c.IsApproved && c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        var result = cats.Select(c => new ExperienceCategoryWithTypesResponse(
            _mapper.Map<ExperienceCategoryRecord>(c),
            _mapper.Map<IReadOnlyList<ExperienceTypeRecord>>(c.ExperienceTypes.OrderBy(t => t.SortOrder).ToList())));

        return Ok(result);
    }
}

/// <summary>SuperAdmin CRUD for experience categories.</summary>
[ApiController]
[Route("api/admin/experience-categories")]
[Authorize(Roles = "SuperAdmin")]
public sealed class AdminExperienceCategoryController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public AdminExperienceCategoryController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>All categories including pending and inactive.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExperienceCategoryRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var cats = await db.ExperienceCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<ExperienceCategoryRecord>>(cats));
    }

    [HttpPost]
    public async Task<ActionResult<ExperienceCategoryRecord>> Create(
        [FromBody] UpsertExperienceCategoryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new ExperienceCategory
        {
            Id                  = Guid.NewGuid(),
            Name                = request.Name.Trim(),
            Description         = request.Description?.Trim(),
            IconClass           = request.IconClass?.Trim(),
            ColorClass          = request.ColorClass?.Trim(),
            SortOrder           = request.SortOrder,
            IsActive            = request.IsActive,
            IsApproved          = true,  // SuperAdmin-created = auto-approved
            ApprovedByAppUserId = userId == Guid.Empty ? null : userId,
            DateApproved        = DateTime.UtcNow,
            DateCreated         = DateTime.UtcNow,
            CreatedByAppUserId  = userId,
        };
        db.ExperienceCategories.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAll), _mapper.Map<ExperienceCategoryRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExperienceCategoryRecord>> Update(
        Guid id, [FromBody] UpsertExperienceCategoryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ExperienceCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return NotFound();
        entity.Name        = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.IconClass   = request.IconClass?.Trim();
        entity.ColorClass  = request.ColorClass?.Trim();
        entity.SortOrder   = request.SortOrder;
        entity.IsActive    = request.IsActive;
        entity.DateUpdated         = DateTime.UtcNow;
        entity.UpdatedByAppUserId  = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ExperienceCategoryRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ExperienceCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return NotFound();
        db.ExperienceCategories.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Approve a pending org-proposed category.</summary>
    [HttpPut("{id:guid}/approve")]
    public async Task<ActionResult<ExperienceCategoryRecord>> Approve(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ExperienceCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return NotFound();
        entity.IsApproved           = true;
        entity.ApprovedByAppUserId  = userId == Guid.Empty ? null : userId;
        entity.DateApproved         = DateTime.UtcNow;
        entity.DateUpdated          = DateTime.UtcNow;
        entity.UpdatedByAppUserId   = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ExperienceCategoryRecord>(entity));
    }
}

/// <summary>SuperAdmin CRUD for experience types within a category.</summary>
[ApiController]
[Route("api/admin/experience-categories/{categoryId:guid}/types")]
[Authorize(Roles = "SuperAdmin")]
public sealed class AdminExperienceTypeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public AdminExperienceTypeController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExperienceTypeRecord>>> GetAll(
        Guid categoryId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var types = await db.ExperienceTypes
            .AsNoTracking()
            .Where(t => t.ExperienceCategoryId == categoryId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<ExperienceTypeRecord>>(types));
    }

    [HttpPost]
    public async Task<ActionResult<ExperienceTypeRecord>> Create(
        Guid categoryId, [FromBody] UpsertExperienceTypeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("A name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.ExperienceCategories.AnyAsync(c => c.Id == categoryId, ct))
            return NotFound("Category not found.");

        // The group-facing path has always deduped case-insensitively; this one never did, so an
        // administrator could quietly create the second "Knocking" that everyone else was being
        // stopped from creating. Returns the existing row rather than erroring — the caller wanted
        // a type with this name in this category, and there is one.
        var normalized = name.ToLowerInvariant();
        var duplicate = await db.ExperienceTypes.FirstOrDefaultAsync(
            t => t.ExperienceCategoryId == categoryId && t.Name.ToLower() == normalized, ct);
        if (duplicate is not null) return Ok(_mapper.Map<ExperienceTypeRecord>(duplicate));

        var entity = new ExperienceType
        {
            Id                   = Guid.NewGuid(),
            ExperienceCategoryId = categoryId,
            Name                 = name,
            Description          = request.Description?.Trim(),
            IconClass            = request.IconClass?.Trim(),
            SortOrder            = request.SortOrder,
            IsActive             = request.IsActive,
            IsApproved           = true,
            ApprovedByAppUserId  = userId == Guid.Empty ? null : userId,
            DateApproved         = DateTime.UtcNow,
            DateCreated          = DateTime.UtcNow,
            CreatedByAppUserId   = userId,
        };
        db.ExperienceTypes.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAll), new { categoryId }, _mapper.Map<ExperienceTypeRecord>(entity));
    }

    /// <summary>
    /// Renames a type, or says the name is taken in this category and offers to merge.
    /// </summary>
    /// <remarks>
    /// <para><b>A collision is refused, not silently applied.</b> Renaming "Knockign" to "Knocking"
    /// when "Knocking" exists used to leave two identical rows in one category — which is the exact
    /// mess the rename was trying to clear up, now with the added property that nobody can tell the
    /// two apart. The 409 carries the id it collided with, so the merge can be chosen deliberately.
    /// </para>
    ///
    /// <para>The merge itself is a separate call because it is not undoable: taggings move and a row
    /// disappears, and only the person renaming knows whether the two words really mean one thing.
    /// </para>
    /// </remarks>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExperienceTypeRecord>> Update(
        Guid categoryId, Guid id, [FromBody] UpsertExperienceTypeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("A name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ExperienceTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.ExperienceCategoryId == categoryId, ct);
        if (entity is null) return NotFound();

        var normalized = name.ToLowerInvariant();
        var clash = await db.ExperienceTypes.AsNoTracking().FirstOrDefaultAsync(
            t => t.Id != id && t.ExperienceCategoryId == categoryId && t.Name.ToLower() == normalized, ct);

        if (clash is not null)
            return Conflict(new TaxonomyMergeOffer(
                entity.Id, entity.Name, clash.Id, clash.Name,
                $"\"{clash.Name}\" already exists in this category. Merging moves everything tagged "
                + $"\"{entity.Name}\" onto it and removes the duplicate. That cannot be undone."));

        entity.Name                = name;
        entity.Description         = request.Description?.Trim();
        entity.IconClass           = request.IconClass?.Trim();
        entity.SortOrder           = request.SortOrder;
        entity.IsActive            = request.IsActive;
        entity.DateUpdated         = DateTime.UtcNow;
        entity.UpdatedByAppUserId  = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ExperienceTypeRecord>(entity));
    }

    /// <summary>
    /// Folds one type into another in the same category: its taggings move across, and it goes.
    /// </summary>
    /// <remarks>
    /// <para><b>Refused when it would lose a review.</b> Merging a human-reviewed type into an
    /// unreviewed one is almost always the direction reversed — somebody correcting a typo has the
    /// two the wrong way round — and the result would be a taxonomy where the endorsed word vanished
    /// and the slip survived.</para>
    ///
    /// <para><b>Same category only.</b> Moving a tagging from <i>Visual</i> to <i>Auditory</i>
    /// changes what somebody recorded about their own night, which is not a rename.</para>
    ///
    /// <para><b>Taggings are deleted and re-added, not repointed.</b> The join's primary key is the
    /// pair (entry, type), and EF refuses to modify a key property on a tracked entity — so
    /// assigning the new type id would throw rather than move the row. An entry already tagged with
    /// both types simply loses the source row and keeps the one it has.</para>
    /// </remarks>
    [HttpPost("{id:guid}/merge-into/{targetId:guid}")]
    public async Task<IActionResult> Merge(
        Guid categoryId, Guid id, Guid targetId, CancellationToken ct)
    {
        if (id == targetId) return BadRequest("A type cannot be merged into itself.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var source = await db.ExperienceTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.ExperienceCategoryId == categoryId, ct);
        var target = await db.ExperienceTypes
            .FirstOrDefaultAsync(t => t.Id == targetId && t.ExperienceCategoryId == categoryId, ct);
        if (source is null || target is null) return NotFound();

        // "Reviewed" is approved-with-an-approver: an org-proposed type is live (IsApproved) but
        // unreviewed until a human stamps ApprovedByAppUserId, so IsApproved alone cannot tell the
        // endorsed word from the one somebody typed last night.
        if (source.ApprovedByAppUserId is not null && target.ApprovedByAppUserId is null)
            return Conflict(
                $"\"{source.Name}\" has been reviewed and \"{target.Name}\" has not. Merge the other "
                + "way round, or review the target first — otherwise the endorsed word is the one "
                + "that disappears.");

        var sourceUsages = await db.CaseTimelineEntryExperienceTypes
            .Where(x => x.ExperienceTypeId == source.Id).ToListAsync(ct);
        var targetEntryIds = await db.CaseTimelineEntryExperienceTypes.AsNoTracking()
            .Where(x => x.ExperienceTypeId == target.Id)
            .Select(x => x.CaseTimelineEntryId)
            .ToListAsync(ct);

        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        db.CaseTimelineEntryExperienceTypes.RemoveRange(sourceUsages);

        foreach (var usage in sourceUsages.Where(u => !targetEntryIds.Contains(u.CaseTimelineEntryId)))
        {
            db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
            {
                CaseTimelineEntryId = usage.CaseTimelineEntryId,
                ExperienceTypeId    = target.Id,
            });
        }

        db.ExperienceTypes.Remove(source);
        await db.SaveChangesAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Removes a type that nothing is tagged with.
    /// </summary>
    /// <remarks>
    /// A type in use is refused rather than deleted. The foreign key is <c>NoAction</c>, so the old
    /// unguarded delete did not quietly strip the taggings — it failed at the database with a
    /// constraint violation and surfaced as a 500, which told the administrator nothing about what
    /// was wrong or what to do instead. Rejecting is the operation that removes a type <i>and</i>
    /// its taggings, and it reports the count; this says so.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid categoryId, Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ExperienceTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.ExperienceCategoryId == categoryId, ct);
        if (entity is null) return NotFound();

        var usages = await db.CaseTimelineEntryExperienceTypes
            .CountAsync(x => x.ExperienceTypeId == id, ct);

        if (usages > 0)
            return Conflict(
                $"\"{entity.Name}\" is tagged on {usages} timeline "
                + (usages == 1 ? "entry" : "entries")
                + ". Reject it instead to remove the tags with it, or merge it into the type it "
                + "should have been.");

        db.ExperienceTypes.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Approve a pending org-proposed type.</summary>
    [HttpPut("{id:guid}/approve")]
    public async Task<ActionResult<ExperienceTypeRecord>> Approve(
        Guid categoryId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.ExperienceTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.ExperienceCategoryId == categoryId, ct);
        if (entity is null) return NotFound();
        entity.IsApproved           = true;
        entity.ApprovedByAppUserId  = userId == Guid.Empty ? null : userId;
        entity.DateApproved         = DateTime.UtcNow;
        entity.DateUpdated          = DateTime.UtcNow;
        entity.UpdatedByAppUserId   = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<ExperienceTypeRecord>(entity));
    }

    /// <summary>
    /// Rejects a group-added type: removes it, and strips it from anything tagged with it.
    /// </summary>
    /// <remarks>
    /// <para>Deletes the <i>usages</i>, never the records. A timeline entry tagged with a rejected
    /// type loses the tag and keeps everything else — its text, its author, its files, its place
    /// on the timeline. Someone's account of what happened is not deleted because an
    /// administrator disliked the label put on it.</para>
    ///
    /// <para>Transactional: dropping the tags and dropping the type have to happen together, or a
    /// half-applied rejection leaves join rows pointing at a type that no longer exists.</para>
    /// </remarks>
    [HttpPut("{id:guid}/reject")]
    public async Task<ActionResult<RejectExperienceTypeResponse>> Reject(
        Guid categoryId, Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var entity = await db.ExperienceTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.ExperienceCategoryId == categoryId, ct);
        if (entity is null) return NotFound();

        var usages = await db.CaseTimelineEntryExperienceTypes
            .Where(x => x.ExperienceTypeId == id)
            .ToListAsync(ct);

        // EF InMemory has no transactions, so the guard keeps the unit tests runnable.
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        db.CaseTimelineEntryExperienceTypes.RemoveRange(usages);
        db.ExperienceTypes.Remove(entity);
        await db.SaveChangesAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);

        return Ok(new RejectExperienceTypeResponse(id, usages.Count));
    }
}

// ── Request / response records ────────────────────────────────────────────────
// Note: matching records are defined in Ben.Web.Library/Services/IBenAdminClient.cs
// These local copies keep the WebApi self-contained (no project reference to Web.Library).

public sealed record UpsertExperienceCategoryRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    int SortOrder,
    bool IsActive);

public sealed record UpsertExperienceTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    int SortOrder,
    bool IsActive);

/// <summary>What a rejection removed — the type, and how many taggings went with it.</summary>
public sealed record RejectExperienceTypeResponse(Guid ExperienceTypeId, int UsagesRemoved);

public sealed record ExperienceCategoryWithTypesResponse(
    ExperienceCategoryRecord Category,
    IReadOnlyList<ExperienceTypeRecord> Types);
