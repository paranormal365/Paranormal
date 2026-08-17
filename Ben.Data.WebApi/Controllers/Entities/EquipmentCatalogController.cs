using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Public read access to the approved equipment taxonomy — category, brand, model — plus the
/// self-service propose endpoints that grow it. Anonymous, matching
/// <c>ExperienceCategoryController</c>'s split of a public-read piece and a SuperAdmin-CRUD piece;
/// SuperAdmin moderation is <see cref="AdminEquipmentTaxonomyController"/> below.
/// </summary>
[ApiController]
[Route("api/equipment-catalog")]
public sealed class EquipmentCatalogController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public EquipmentCatalogController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Active categories (public — no auth required).</summary>
    [HttpGet("categories")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<EquipmentCategoryRecord>>> GetCategories(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var categories = await db.EquipmentCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new EquipmentCategoryRecord(c.Id, c.Name, c.Description, c.IconClass, c.SortOrder, c.IsActive))
            .ToListAsync(ct);
        return Ok(categories);
    }

    /// <summary>
    /// Approved brands, optionally name-filtered. Also returns the caller's own pending
    /// proposals — a proposer can keep using their own unapproved entry immediately; nobody
    /// else's pending work is visible here.
    /// </summary>
    [HttpGet("brands")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<EquipmentBrandRecord>>> GetBrands(
        [FromQuery] string? search, CancellationToken ct)
    {
        var callerId = GetCurrentUserIdOrNull();
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.EquipmentBrands.AsNoTracking()
            .Where(b => b.IsApproved || (callerId != null && b.ProposedByAppUserId == callerId));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(b => EF.Functions.Like(b.Name, $"%{search}%"));

        var brands = await query
            .OrderBy(b => b.Name)
            .Select(b => new EquipmentBrandRecord(b.Id, b.Name, b.IsApproved, b.ProposedByOrganizationId, b.ProposedByAppUserId, b.DateCreated))
            .ToListAsync(ct);
        return Ok(brands);
    }

    /// <summary>Approved models under one brand (public), optionally filtered to a category.</summary>
    [HttpGet("brands/{brandId:guid}/models")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<EquipmentModelRecord>>> GetModelsForBrand(
        Guid brandId, [FromQuery] Guid? categoryId, CancellationToken ct)
    {
        var callerId = GetCurrentUserIdOrNull();
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.EquipmentModels.AsNoTracking()
            .Include(m => m.EquipmentBrand)
            .Include(m => m.EquipmentCategory)
            .Where(m => m.EquipmentBrandId == brandId
                     && (m.IsApproved || (callerId != null && m.ProposedByAppUserId == callerId)));
        if (categoryId is not null)
            query = query.Where(m => m.EquipmentCategoryId == categoryId);

        var models = await query
            .OrderBy(m => m.Name)
            .Select(m => new EquipmentModelRecord(
                m.Id, m.EquipmentBrandId, m.EquipmentBrand.Name, m.EquipmentCategoryId, m.EquipmentCategory.Name,
                m.Name, m.ModelNumber, m.Description, m.IsApproved, m.ProposedByOrganizationId, m.ProposedByAppUserId, m.DateCreated))
            .ToListAsync(ct);
        return Ok(models);
    }

    /// <summary>Approved models across the whole catalog (public), searchable, optionally category-filtered.</summary>
    [HttpGet("models")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<EquipmentModelRecord>>> SearchModels(
        [FromQuery] string? search, [FromQuery] Guid? categoryId, CancellationToken ct)
    {
        var callerId = GetCurrentUserIdOrNull();
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.EquipmentModels.AsNoTracking()
            .Include(m => m.EquipmentBrand)
            .Include(m => m.EquipmentCategory)
            .Where(m => m.IsApproved || (callerId != null && m.ProposedByAppUserId == callerId));
        if (categoryId is not null)
            query = query.Where(m => m.EquipmentCategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => EF.Functions.Like(m.Name, $"%{search}%") || EF.Functions.Like(m.EquipmentBrand.Name, $"%{search}%"));

        var models = await query
            .OrderBy(m => m.EquipmentBrand.Name).ThenBy(m => m.Name)
            .Select(m => new EquipmentModelRecord(
                m.Id, m.EquipmentBrandId, m.EquipmentBrand.Name, m.EquipmentCategoryId, m.EquipmentCategory.Name,
                m.Name, m.ModelNumber, m.Description, m.IsApproved, m.ProposedByOrganizationId, m.ProposedByAppUserId, m.DateCreated))
            .ToListAsync(ct);
        return Ok(models);
    }

    /// <summary>
    /// Items their owners have chosen to list publicly.
    /// </summary>
    /// <remarks>
    /// Opt-in only: <c>IncludeInGlobalCatalog</c> defaults to false, so nothing lands here because
    /// somebody forgot to hide it. Projects to <see cref="PublicEquipmentItemRecord"/>, which has
    /// no owner id, no owner name and no serial number on the shape at all — a projection that
    /// cannot carry them cannot leak them if a filter is later written wrongly. Retired items are
    /// excluded.
    /// </remarks>
    [HttpGet("items")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<PublicEquipmentItemRecord>>> GetPublicItems(
        [FromQuery] string? search, [FromQuery] Guid? categoryId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.EquipmentItems.AsNoTracking()
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .Where(i => i.IncludeInGlobalCatalog && !i.IsRetired);

        if (categoryId is not null)
            query = query.Where(i => i.EquipmentModel.EquipmentCategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i =>
                EF.Functions.Like(i.DisplayName, $"%{search}%")
                || EF.Functions.Like(i.EquipmentModel.Name, $"%{search}%")
                || EF.Functions.Like(i.EquipmentModel.EquipmentBrand.Name, $"%{search}%"));

        var items = await query
            .OrderBy(i => i.EquipmentModel.EquipmentBrand.Name).ThenBy(i => i.DisplayName)
            .Select(i => new PublicEquipmentItemRecord(
                i.Id,
                i.DisplayName,
                i.EquipmentModel.EquipmentBrand.Name,
                i.EquipmentModel.Name,
                i.EquipmentModel.EquipmentCategory.Name,
                i.AcquisitionDate,
                i.Notes,
                i.LoanAudience,
                i.Photos.OrderBy(p => p.SortOrder)
                    .Select(p => new EquipmentItemPhotoRecord(p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder))
                    .ToList()))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Proposes a brand. SuperAdmin-created entries are auto-approved; everyone else starts
    /// unapproved but can use their own entry right away. Dedupes by name (case-insensitive) —
    /// returns the existing row rather than 409, which is friendlier for an accumulating catalog
    /// than making every near-duplicate typing attempt an error.
    /// </summary>
    [HttpPost("brands")]
    [Authorize]
    public async Task<ActionResult<EquipmentBrandRecord>> ProposeBrand(
        [FromBody] UpsertEquipmentBrandRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var existing = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Name == name, ct);
        if (existing is null)
        {
            var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);
            existing = new EquipmentBrand
            {
                Id                   = Guid.NewGuid(),
                Name                 = name,
                IsApproved           = isSuperAdmin,
                ProposedByAppUserId  = isSuperAdmin ? null : userId,
                ApprovedByAppUserId  = isSuperAdmin ? userId : null,
                DateApproved         = isSuperAdmin ? DateTime.UtcNow : null,
                DateCreated          = DateTime.UtcNow,
                CreatedByAppUserId   = userId,
            };
            db.EquipmentBrands.Add(existing);
            await db.SaveChangesAsync(ct);
        }

        return Ok(new EquipmentBrandRecord(existing.Id, existing.Name, existing.IsApproved,
            existing.ProposedByOrganizationId, existing.ProposedByAppUserId, existing.DateCreated));
    }

    /// <summary>Proposes a model under a brand. Same auto-approve/dedupe rules as <see cref="ProposeBrand"/>.</summary>
    [HttpPost("models")]
    [Authorize]
    public async Task<ActionResult<EquipmentModelRecord>> ProposeModel(
        [FromBody] UpsertEquipmentModelRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.EquipmentBrands.AnyAsync(b => b.Id == request.EquipmentBrandId, ct))
            return BadRequest("Brand not found.");
        if (!await db.EquipmentCategories.AnyAsync(c => c.Id == request.EquipmentCategoryId && c.IsActive, ct))
            return BadRequest("Category not found.");

        var existing = await db.EquipmentModels
            .Include(m => m.EquipmentBrand).Include(m => m.EquipmentCategory)
            .FirstOrDefaultAsync(m => m.EquipmentBrandId == request.EquipmentBrandId && m.Name == name, ct);
        if (existing is null)
        {
            var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);
            existing = new EquipmentModel
            {
                Id                   = Guid.NewGuid(),
                EquipmentBrandId     = request.EquipmentBrandId,
                EquipmentCategoryId  = request.EquipmentCategoryId,
                Name                 = name,
                ModelNumber          = request.ModelNumber?.Trim(),
                Description          = request.Description?.Trim(),
                IsApproved           = isSuperAdmin,
                ProposedByAppUserId  = isSuperAdmin ? null : userId,
                ApprovedByAppUserId  = isSuperAdmin ? userId : null,
                DateApproved         = isSuperAdmin ? DateTime.UtcNow : null,
                DateCreated          = DateTime.UtcNow,
                CreatedByAppUserId   = userId,
            };
            db.EquipmentModels.Add(existing);
            await db.SaveChangesAsync(ct);
            await db.Entry(existing).Reference(m => m.EquipmentBrand).LoadAsync(ct);
            await db.Entry(existing).Reference(m => m.EquipmentCategory).LoadAsync(ct);
        }

        return Ok(new EquipmentModelRecord(
            existing.Id, existing.EquipmentBrandId, existing.EquipmentBrand.Name,
            existing.EquipmentCategoryId, existing.EquipmentCategory.Name,
            existing.Name, existing.ModelNumber, existing.Description,
            existing.IsApproved, existing.ProposedByOrganizationId, existing.ProposedByAppUserId, existing.DateCreated));
    }
}

/// <summary>SuperAdmin moderation for the equipment taxonomy — categories are seeded/CRUD; brands and models are approve/reject.</summary>
[ApiController]
[Route("api/admin/equipment-taxonomy")]
[Authorize(Roles = "SuperAdmin")]
public sealed class AdminEquipmentTaxonomyController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public AdminEquipmentTaxonomyController(IDbContextFactory<BenDataContext> db) => _db = db;

    // ── Categories ────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<EquipmentCategoryRecord>>> GetCategories(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var categories = await db.EquipmentCategories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new EquipmentCategoryRecord(c.Id, c.Name, c.Description, c.IconClass, c.SortOrder, c.IsActive))
            .ToListAsync(ct);
        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<ActionResult<EquipmentCategoryRecord>> CreateCategory(
        [FromBody] UpsertEquipmentCategoryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new EquipmentCategory
        {
            Id                 = Guid.NewGuid(),
            Name               = request.Name.Trim(),
            Description        = request.Description?.Trim(),
            IconClass          = request.IconClass?.Trim(),
            SortOrder          = request.SortOrder,
            IsActive           = request.IsActive,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.EquipmentCategories.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new EquipmentCategoryRecord(entity.Id, entity.Name, entity.Description, entity.IconClass, entity.SortOrder, entity.IsActive));
    }

    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<EquipmentCategoryRecord>> UpdateCategory(
        Guid id, [FromBody] UpsertEquipmentCategoryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return NotFound();
        entity.Name        = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.IconClass   = request.IconClass?.Trim();
        entity.SortOrder   = request.SortOrder;
        entity.IsActive    = request.IsActive;
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(new EquipmentCategoryRecord(entity.Id, entity.Name, entity.Description, entity.IconClass, entity.SortOrder, entity.IsActive));
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await db.EquipmentModels.AnyAsync(m => m.EquipmentCategoryId == id, ct))
            return Conflict("Category is in use by one or more models — reassign them first.");
        var entity = await db.EquipmentCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return NotFound();
        db.EquipmentCategories.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Brands ────────────────────────────────────────────────────────────────

    [HttpGet("brands")]
    public async Task<ActionResult<IEnumerable<EquipmentBrandRecord>>> GetAllBrands(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var brands = await db.EquipmentBrands.AsNoTracking()
            .OrderBy(b => b.IsApproved).ThenBy(b => b.Name)
            .Select(b => new EquipmentBrandRecord(b.Id, b.Name, b.IsApproved, b.ProposedByOrganizationId, b.ProposedByAppUserId, b.DateCreated))
            .ToListAsync(ct);
        return Ok(brands);
    }

    [HttpPut("brands/{id:guid}/approve")]
    public async Task<ActionResult<EquipmentBrandRecord>> ApproveBrand(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity is null) return NotFound();
        entity.IsApproved          = true;
        entity.ApprovedByAppUserId = userId;
        entity.DateApproved        = DateTime.UtcNow;
        entity.DateUpdated         = DateTime.UtcNow;
        entity.UpdatedByAppUserId  = userId;
        await db.SaveChangesAsync(ct);
        return Ok(new EquipmentBrandRecord(entity.Id, entity.Name, entity.IsApproved, entity.ProposedByOrganizationId, entity.ProposedByAppUserId, entity.DateCreated));
    }

    /// <summary>Rejects a pending brand. Refuses if any model already hangs off it — reassign or reject those first.</summary>
    [HttpDelete("brands/{id:guid}")]
    public async Task<IActionResult> RejectBrand(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await db.EquipmentModels.AnyAsync(m => m.EquipmentBrandId == id, ct))
            return Conflict("Brand has models — remove or reassign them first.");
        var entity = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity is null) return NotFound();
        db.EquipmentBrands.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Models ────────────────────────────────────────────────────────────────

    [HttpGet("models")]
    public async Task<ActionResult<IEnumerable<EquipmentModelRecord>>> GetAllModels([FromQuery] Guid? brandId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.EquipmentModels.AsNoTracking()
            .Include(m => m.EquipmentBrand).Include(m => m.EquipmentCategory)
            .AsQueryable();
        if (brandId is not null) query = query.Where(m => m.EquipmentBrandId == brandId);

        var models = await query
            .OrderBy(m => m.IsApproved).ThenBy(m => m.EquipmentBrand.Name).ThenBy(m => m.Name)
            .Select(m => new EquipmentModelRecord(
                m.Id, m.EquipmentBrandId, m.EquipmentBrand.Name, m.EquipmentCategoryId, m.EquipmentCategory.Name,
                m.Name, m.ModelNumber, m.Description, m.IsApproved, m.ProposedByOrganizationId, m.ProposedByAppUserId, m.DateCreated))
            .ToListAsync(ct);
        return Ok(models);
    }

    [HttpPut("models/{id:guid}/approve")]
    public async Task<ActionResult<EquipmentModelRecord>> ApproveModel(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentModels
            .Include(m => m.EquipmentBrand).Include(m => m.EquipmentCategory)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null) return NotFound();
        entity.IsApproved          = true;
        entity.ApprovedByAppUserId = userId;
        entity.DateApproved        = DateTime.UtcNow;
        entity.DateUpdated         = DateTime.UtcNow;
        entity.UpdatedByAppUserId  = userId;
        await db.SaveChangesAsync(ct);
        return Ok(new EquipmentModelRecord(
            entity.Id, entity.EquipmentBrandId, entity.EquipmentBrand.Name, entity.EquipmentCategoryId, entity.EquipmentCategory.Name,
            entity.Name, entity.ModelNumber, entity.Description, entity.IsApproved, entity.ProposedByOrganizationId, entity.ProposedByAppUserId, entity.DateCreated));
    }

    /// <summary>Rejects a pending model. Refuses if any item already uses it.</summary>
    [HttpDelete("models/{id:guid}")]
    public async Task<IActionResult> RejectModel(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await db.EquipmentItems.AnyAsync(i => i.EquipmentModelId == id, ct))
            return Conflict("Model is in use by one or more equipment items.");
        var entity = await db.EquipmentModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null) return NotFound();
        db.EquipmentModels.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
