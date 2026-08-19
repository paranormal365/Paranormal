using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
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
                i.EquipmentModelId,
                i.DisplayName,
                i.EquipmentModel.EquipmentBrand.Name,
                i.EquipmentModel.Name,
                i.EquipmentModel.EquipmentCategory.Name,
                i.AcquisitionDate,
                i.Notes,
                i.LoanAudience,
                i.WebsiteUrl,
                i.Photos.OrderBy(p => p.SortOrder)
                    .Select(p => new EquipmentItemPhotoRecord(p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder, p.ExcludeFromCatalog))
                    .ToList()))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Records that somebody looked at a piece of equipment.
    /// </summary>
    /// <remarks>
    /// <para>Anonymous, because the pages that call it are. Always answers 204, including for an id
    /// that does not exist — varying the response would make this a cheaper existence probe than
    /// any real endpoint offers.</para>
    ///
    /// <para>Fire-and-forget and unauthenticated by design: these are interest numbers, not
    /// billing. Protection is the app's rate limiting; a determined script can inflate a counter
    /// and that is an accepted trade for not making every page view an authenticated write.</para>
    /// </remarks>
    [HttpPost("items/{id:guid}/viewed")]
    [AllowAnonymous]
    public Task<IActionResult> RecordView(Guid id, CancellationToken ct)
        => BumpAsync(id, item => item.ViewCount++, ct);

    /// <summary>Records that somebody followed a piece's website link.</summary>
    /// <remarks>
    /// The client opens the link first and calls this afterwards, so a failure here can never cost
    /// the reader the thing they actually asked for.
    /// </remarks>
    [HttpPost("items/{id:guid}/link-clicked")]
    [AllowAnonymous]
    public Task<IActionResult> RecordLinkClick(Guid id, CancellationToken ct)
        => BumpAsync(id, item => item.LinkClickCount++, ct);

    private async Task<IActionResult> BumpAsync(Guid id, Action<EquipmentItem> bump, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.FirstOrDefaultAsync(i => i.Id == id, ct);

        // No such item, or it is retired and out of circulation — nothing to count, and still 204.
        if (item is null || item.IsRetired) return NoContent();

        bump(item);
        // Through the change tracker rather than ExecuteUpdate: the InMemory provider the tests run
        // against does not support the latter, and a lost increment under concurrency costs nothing
        // that matters for a number like this.
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// One make and model, with everything its owners have contributed pooled together.
    /// </summary>
    /// <remarks>
    /// <para>Public. Photos come from every copy of the model whose owner has not excluded them,
    /// shown anonymously — the projection carries no owner, no item id and no file id, so there is
    /// nothing for a mistaken filter to leak later.</para>
    ///
    /// <para>The exception is <c>LinkedItemId</c>, resolved <i>per viewer</i>: an item is linked
    /// only when this caller may open it — their own, one listed publicly, one shared into a group
    /// they belong to, or their own group's. Everyone else gets a photo with nowhere to click.</para>
    /// </remarks>
    /// <summary>
    /// The same page, at the address a person would actually type or share.
    /// </summary>
    /// <remarks>
    /// <para><c>/equipment/zoom/h1n</c> rather than <c>/equipment-models/3f2a9c81-…</c>. This was
    /// the last thing in the readable-URL work still wearing a GUID, and it is the one Ben raised
    /// first: an address nobody can read is one nobody clicks and nobody recognises in their own
    /// history.</para>
    ///
    /// <para>Resolves to an id and hands off, so the whole audience and photo-visibility apparatus
    /// stays in one place. A second copy of those rules living behind a prettier route is how two
    /// endpoints for one page start disagreeing about who may see what.</para>
    /// </remarks>
    [HttpGet("makes/{brandSlug}/{modelSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<EquipmentModelPageRecord>> GetModelPageBySlug(
        string brandSlug, string modelSlug, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var brand = Ben.Data.Common.SlugText.NormalizeOrEmpty(brandSlug);
        var model = Ben.Data.Common.SlugText.NormalizeOrEmpty(modelSlug);

        var id = await db.EquipmentModels.AsNoTracking()
            .Where(m => m.UrlName == model && m.EquipmentBrand.UrlName == brand)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);

        return id is Guid modelId ? await GetModelPage(modelId, ct) : NotFound();
    }

    [HttpGet("models/{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<EquipmentModelPageRecord>> GetModelPage(Guid id, CancellationToken ct)
    {
        var callerId = GetCurrentUserIdOrNull();
        await using var db = await _db.CreateDbContextAsync(ct);

        var model = await db.EquipmentModels.AsNoTracking()
            .Include(m => m.EquipmentBrand)
            .Include(m => m.EquipmentCategory)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null) return NotFound();

        // An unapproved model is visible to whoever proposed it and to SuperAdmin, matching the
        // search endpoint's rule — otherwise it is not public yet.
        if (!model.IsApproved
            && !(callerId is not null && model.ProposedByAppUserId == callerId)
            && !User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin))
            return NotFound();

        var items = await db.EquipmentItems.AsNoTracking()
            .Include(i => i.Photos)
            .Where(i => i.EquipmentModelId == id && !i.IsRetired)
            .ToListAsync(ct);

        // Which of these items may this caller actually open? Resolved in one pass over the small
        // per-model set rather than a query per photo.
        var openableItemIds = await ResolveOpenableAsync(db, items, callerId, ct);

        var photos = items
            .SelectMany(i => i.Photos
                .Where(p => !p.ExcludeFromCatalog)
                .Select(p => new CatalogPhotoRecord(
                    p.Id, p.Caption, p.SortOrder,
                    openableItemIds.Contains(i.Id) ? i.Id : null)))
            .OrderBy(p => p.SortOrder)
            .ToList();

        var links = items
            .Select(i => i.WebsiteUrl)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Publicly-listed items only, for every caller alike. Widening this for members would let a
        // reader work out that somebody in one of their groups owns one — the aggregate would be
        // saying, by its length, something no individual entry says.
        var publicItemIds = items.Where(i => i.IncludeInGlobalCatalog).Select(i => i.Id).ToList();
        var faqs = publicItemIds.Count == 0
            ? []
            : await db.EquipmentItemFaqs.AsNoTracking()
                .Where(f => publicItemIds.Contains(f.EquipmentItemId))
                .OrderBy(f => f.SortOrder).ThenBy(f => f.DateCreated)
                .Select(f => new CatalogFaqRecord(f.Question, f.Answer))
                .ToListAsync(ct);

        var record = new EquipmentModelRecord(
            model.Id, model.EquipmentBrandId, model.EquipmentBrand.Name,
            model.EquipmentCategoryId, model.EquipmentCategory.Name,
            model.Name, model.ModelNumber, model.Description, model.IsApproved,
            model.ProposedByOrganizationId, model.ProposedByAppUserId, model.DateCreated,
            model.UrlName, model.EquipmentBrand.UrlName);

        return Ok(new EquipmentModelPageRecord(
            record,
            items.Count,
            items.Count(i => i.OwningOrganizationId is not null || i.LoanAudience != EquipmentLoanAudience.NotLoanable),
            links,
            photos,
            faqs));
    }

    /// <summary>
    /// Of these items, the ones <paramref name="callerId"/> is allowed to open a page for.
    /// </summary>
    /// <remarks>
    /// Mirrors the photo-visibility rule rather than inventing a second one: own item, publicly
    /// listed, shared into a group both parties are in, or the caller's own group's gear.
    /// </remarks>
    private static async Task<HashSet<Guid>> ResolveOpenableAsync(
        BenDataContext db, IReadOnlyList<EquipmentItem> items, Guid? callerId, CancellationToken ct)
    {
        var openable = items
            .Where(i => i.IncludeInGlobalCatalog)
            .Select(i => i.Id)
            .ToHashSet();

        if (callerId is not Guid userId) return openable;

        foreach (var item in items.Where(i => i.OwnerAppUserId == userId))
            openable.Add(item.Id);

        var myOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);
        if (myOrgIds.Count == 0) return openable;

        foreach (var item in items.Where(i => i.OwningOrganizationId is Guid o && myOrgIds.Contains(o)))
            openable.Add(item.Id);

        // Personal gear shared into one of my groups, where the owner is still a member of it.
        var candidateIds = items
            .Where(i => i.OwnerAppUserId is not null && !openable.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();
        if (candidateIds.Count == 0) return openable;

        var shared = await db.EquipmentItemShares.AsNoTracking()
            .Where(s => candidateIds.Contains(s.EquipmentItemId) && myOrgIds.Contains(s.OrganizationId))
            .Where(s => db.OrganizationUserMemberships.Any(m =>
                m.OrganizationId == s.OrganizationId
                && m.AppUserId == s.EquipmentItem.OwnerAppUserId
                && m.IsActive))
            .Select(s => s.EquipmentItemId)
            .ToListAsync(ct);

        foreach (var itemId in shared) openable.Add(itemId);
        return openable;
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

        // Case-insensitively, so "samsung" and "Samsung" are one manufacturer whatever the
        // database's collation happens to be — the same rule slugs now follow.
        var normalized = name.ToLowerInvariant();
        var existing = await db.EquipmentBrands
            .FirstOrDefaultAsync(b => b.Name.ToLower() == normalized, ct);

        if (existing is null)
        {
            // Nothing matches exactly — but something close might, and this is the only cheap
            // moment to ask. Afterwards it takes a SuperAdmin noticing, and a merge.
            var nearMisses = await FindProbableTyposAsync(db, name, ct);
            if (nearMisses.Count > 0 && !request.ConfirmDistinct)
                return Conflict(new ProbableDuplicateResponse(name, nearMisses));
        }

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
            await EquipmentCatalogSlugs.AssignAsync(db, existing, ct);
            await db.SaveChangesAsync(ct);
        }

        return Ok(new EquipmentBrandRecord(existing.Id, existing.Name, existing.IsApproved,
            existing.ProposedByOrganizationId, existing.ProposedByAppUserId, existing.DateCreated));
    }


    /// <summary>
    /// Approved brands whose names look like mistypings of this one.
    /// </summary>
    /// <remarks>
    /// Only <b>approved</b> brands are offered as suggestions. Proposing against somebody else's
    /// unapproved typo would spread it rather than catch it, and the approved set is the shared
    /// vocabulary the catalog is actually trying to converge on.
    /// </remarks>
    private static async Task<List<string>> FindProbableTyposAsync(
        BenDataContext db, string name, CancellationToken ct)
    {
        // Length-bounded in the query so this reads a handful of rows rather than the catalogue:
        // anything more than two characters different in length is not a typo of this.
        var lower = name.Length - 2;
        var upper = name.Length + 2;

        var candidates = await db.EquipmentBrands.AsNoTracking()
            .Where(b => b.IsApproved && b.Name.Length >= lower && b.Name.Length <= upper)
            .Select(b => b.Name)
            .ToListAsync(ct);

        return [.. candidates.Where(c => NameSimilarity.IsProbableTypo(name, c)).OrderBy(c => c)];
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
            await EquipmentCatalogSlugs.AssignAsync(db, existing, ct);
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


    // ── Renaming, and the merge a rename sometimes turns out to be ────────────

    /// <summary>
    /// Renames a brand, or says that the name is taken and offers to merge.
    /// </summary>
    /// <remarks>
    /// <para>Ben's question: <i>"what happens when I try to change Samsung to Sansung?"</i> Until now
    /// nothing happened, because nothing could rename — only approve and delete, and delete is
    /// refused while anything references the row. So a typo was unfixable and a correct name was
    /// unchangeable.</para>
    ///
    /// <para><b>A collision is refused, not silently merged.</b> Renaming onto an existing name
    /// means two manufacturers become one and somebody's equipment changes make — a large thing to
    /// have happen because a name was typed. The 409 carries the id of the brand it collided with,
    /// so the caller can choose the merge deliberately.</para>
    /// </remarks>
    [HttpPut("brands/{id:guid}")]
    public async Task<ActionResult<EquipmentBrandRecord>> RenameBrand(
        Guid id, [FromBody] UpsertEquipmentBrandRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var brand = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand is null) return NotFound();

        var normalized = name.ToLowerInvariant();
        var clash = await db.EquipmentBrands.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id != id && b.Name.ToLower() == normalized, ct);

        if (clash is not null)
            return Conflict(new TaxonomyMergeOffer(
                brand.Id, brand.Name, clash.Id, clash.Name,
                $"\"{clash.Name}\" already exists. Merging moves everything under \"{brand.Name}\" "
                + "onto it and removes the duplicate. That cannot be undone."));

        brand.Name = name;
        brand.DateUpdated = DateTime.UtcNow;
        // The address follows the corrected name — see EquipmentCatalogSlugs for why this one is
        // regenerated while a case's or an organization's is frozen.
        await EquipmentCatalogSlugs.AssignAsync(db, brand, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new EquipmentBrandRecord(
            brand.Id, brand.Name, brand.IsApproved,
            brand.ProposedByOrganizationId, brand.ProposedByAppUserId, brand.DateCreated));
    }

    /// <summary>
    /// Folds one brand into another: its models move across, and it is removed.
    /// </summary>
    /// <remarks>
    /// <para><b>Refused when it would lose an approval.</b> Merging an approved brand into an
    /// unapproved one is almost always the direction reversed — somebody correcting "Sansung" has
    /// the two the wrong way round — and the result would be a catalog where the endorsed name
    /// vanished and the typo survived. Saying so is more useful than doing it.</para>
    ///
    /// <para>A model whose name already exists under the target is <b>folded rather than moved</b>:
    /// two "H1n" rows under one brand would break the unique index, so its items move to the
    /// surviving model and the duplicate goes. That is the same merge one level down, and doing it
    /// here rather than failing is what makes the operation usable on real data.</para>
    /// </remarks>
    [HttpPost("brands/{id:guid}/merge-into/{targetId:guid}")]
    public async Task<IActionResult> MergeBrand(Guid id, Guid targetId, CancellationToken ct)
    {
        if (id == targetId) return BadRequest("A brand cannot be merged into itself.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var source = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Id == id, ct);
        var target = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Id == targetId, ct);
        if (source is null || target is null) return NotFound();

        if (source.IsApproved && !target.IsApproved)
            return Conflict(
                $"\"{source.Name}\" is approved and \"{target.Name}\" is not. Merge the other way "
                + "round, or approve the target first — otherwise the endorsed name is the one that "
                + "disappears.");

        var sourceModels = await db.EquipmentModels
            .Where(m => m.EquipmentBrandId == source.Id).ToListAsync(ct);
        var targetModels = await db.EquipmentModels.AsNoTracking()
            .Where(m => m.EquipmentBrandId == target.Id).ToListAsync(ct);

        foreach (var model in sourceModels)
        {
            var twin = targetModels.FirstOrDefault(
                t => string.Equals(t.Name, model.Name, StringComparison.OrdinalIgnoreCase));

            if (twin is null)
            {
                model.EquipmentBrandId = target.Id;
                continue;
            }

            // Same model name on both sides: move its items to the survivor and drop the duplicate,
            // because two rows with one name under one brand is exactly what the index forbids.
            var items = await db.EquipmentItems
                .Where(i => i.EquipmentModelId == model.Id).ToListAsync(ct);
            foreach (var item in items) item.EquipmentModelId = twin.Id;

            db.EquipmentModels.Remove(model);
        }

        db.EquipmentBrands.Remove(source);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Renames a model, or says the name is taken under this brand and offers to merge.</summary>
    [HttpPut("models/{id:guid}")]
    public async Task<ActionResult<EquipmentModelRecord>> RenameModel(
        Guid id, [FromBody] RenameEquipmentModelRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var model = await db.EquipmentModels
            .Include(m => m.EquipmentBrand).Include(m => m.EquipmentCategory)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null) return NotFound();

        var normalized = name.ToLowerInvariant();
        var clash = await db.EquipmentModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id != id
                                   && m.EquipmentBrandId == model.EquipmentBrandId
                                   && m.Name.ToLower() == normalized, ct);

        if (clash is not null)
            return Conflict(new TaxonomyMergeOffer(
                model.Id, model.Name, clash.Id, clash.Name,
                $"\"{clash.Name}\" already exists under this make. Merging moves everything under "
                + $"\"{model.Name}\" onto it and removes the duplicate. That cannot be undone."));

        model.Name = name;
        if (request.ModelNumber is not null) model.ModelNumber = request.ModelNumber.Trim();
        model.DateUpdated = DateTime.UtcNow;
        await EquipmentCatalogSlugs.AssignAsync(db, model, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new EquipmentModelRecord(
            model.Id, model.EquipmentBrandId, model.EquipmentBrand.Name,
            model.EquipmentCategoryId, model.EquipmentCategory.Name,
            model.Name, model.ModelNumber, model.Description, model.IsApproved,
            model.ProposedByOrganizationId, model.ProposedByAppUserId, model.DateCreated));
    }

    /// <summary>Folds one model into another under the same brand: its items move across.</summary>
    [HttpPost("models/{id:guid}/merge-into/{targetId:guid}")]
    public async Task<IActionResult> MergeModel(Guid id, Guid targetId, CancellationToken ct)
    {
        if (id == targetId) return BadRequest("A model cannot be merged into itself.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var source = await db.EquipmentModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        var target = await db.EquipmentModels.FirstOrDefaultAsync(m => m.Id == targetId, ct);
        if (source is null || target is null) return NotFound();

        // Across brands this would silently change what make somebody's equipment is. That may be
        // a legitimate correction, but it is a different decision and belongs to the brand merge.
        if (source.EquipmentBrandId != target.EquipmentBrandId)
            return BadRequest("Those models belong to different makes. Merge the makes instead.");

        if (source.IsApproved && !target.IsApproved)
            return Conflict(
                $"\"{source.Name}\" is approved and \"{target.Name}\" is not. Merge the other way "
                + "round, or approve the target first.");

        var items = await db.EquipmentItems.Where(i => i.EquipmentModelId == source.Id).ToListAsync(ct);
        foreach (var item in items) item.EquipmentModelId = target.Id;

        db.EquipmentModels.Remove(source);
        await db.SaveChangesAsync(ct);
        return NoContent();
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
