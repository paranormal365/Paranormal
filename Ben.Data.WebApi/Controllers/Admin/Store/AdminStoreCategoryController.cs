using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The store's categories — EMF meters, spirit boxes — each with an address, a picture and an
/// on/off switch (storefront S1.3).
/// </summary>
/// <remarks>
/// <para><b>Not behind the store switch.</b> The back office has to work while the shop is dark;
/// that is how the catalogue gets entered before anybody can see it.</para>
///
/// <para><b>Hiding a category hides its products</b> without touching them: each keeps its own
/// switch, and showing the category again puts back exactly what was live. The list carries each
/// category's live-product count so the page can say what hiding will do BEFORE it saves, and the
/// save answers with the number it actually took off.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/categories")]
public sealed class AdminStoreCategoryController(
    IDbContextFactory<BenDataContext> dbFactory, IAuditLogService auditLog, StoreImageStorage images)
    : BenControllerBase
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 1000;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreCategoryAdminRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, ct));
    }

    [HttpPost]
    public async Task<ActionResult<StoreCategoryAdminRecord>> Create(
        [FromBody] SaveStoreCategoryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = new StoreCategory
        {
            Id = Guid.NewGuid(), DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            SortOrder = (await db.StoreCategories.MaxAsync(c => (int?)c.SortOrder, ct) ?? -1) + 1,
        };
        if (await ApplyAsync(db, category, request, ct) is { } refused) return refused;

        db.StoreCategories.Add(category);
        if (await SaveOrConflictAsync(db, category, ct) is { } conflict) return conflict;
        await TryAuditAsync(auditLog.LogCreateAsync(nameof(StoreCategory), category.Id, category, userId, AppSources.WebApi));

        return Ok((await ListAsync(db, ct)).Single(c => c.Id == category.Id));
    }

    /// <summary>Saves a category; says how many live products a hide took off the store.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StoreCategorySaveResult>> Update(
        Guid id, [FromBody] SaveStoreCategoryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = await db.StoreCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound();
        var before = Clone(category);

        var hidden = category.IsActive && !request.IsActive
            ? await db.StoreProducts.CountAsync(p => p.CategoryId == id && p.IsActive, ct)
            : 0;

        if (await ApplyAsync(db, category, request, ct) is { } refused) return refused;
        category.DateUpdated = DateTime.UtcNow;
        category.UpdatedByAppUserId = userId;

        if (await SaveOrConflictAsync(db, category, ct) is { } conflict) return conflict;
        await TryAuditAsync(auditLog.LogUpdateAsync(nameof(StoreCategory), id, before, category, userId, AppSources.WebApi));

        return Ok(new StoreCategorySaveResult((await ListAsync(db, ct)).Single(c => c.Id == id), hidden));
    }

    /// <summary>Puts the categories in a new order. Every category, once — a partial list is refused.</summary>
    [HttpPut("reorder")]
    public async Task<ActionResult<IEnumerable<StoreCategoryAdminRecord>>> Reorder(
        [FromBody] ReorderRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var categories = await db.StoreCategories.ToListAsync(ct);
        var ordered = request.OrderedIds ?? [];
        // Same length and every category present means no repeats either.
        if (ordered.Count != categories.Count || !categories.All(c => ordered.Contains(c.Id)))
            return BadRequest("That isn't the full list of categories.");

        var now = DateTime.UtcNow;
        foreach (var c in categories)
        {
            var position = IndexOf(ordered, c.Id);
            if (c.SortOrder == position) continue;
            c.SortOrder = position;
            c.DateUpdated = now;
            c.UpdatedByAppUserId = userId;
        }
        await db.SaveChangesAsync(ct);
        return Ok(await ListAsync(db, ct));
    }

    /// <summary>Gives a category its picture, replacing any it had.</summary>
    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<StoreCategoryAdminRecord>> SetImage(Guid id, IFormFile? file, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = await db.StoreCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");

        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);

        var (stored, refusal) = await images.SaveAsync(db, buffer.ToArray(), file.ContentType, file.FileName,
            $"categories/{id:N}", userId, ct);
        if (refusal == StoreImageRefusal.NotAPicture)
            return BadRequest("A category picture is a photograph — JPEG, PNG or similar.");
        if (stored is null) return BadRequest("That picture could not be read.");

        var previous = category.ImageUploadFileId;
        category.ImageUploadFileId = stored.Id;
        category.DateUpdated = DateTime.UtcNow;
        category.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        if (previous is { } old) await images.RemoveAsync(db, old, ct);
        return Ok((await ListAsync(db, ct)).Single(c => c.Id == id));
    }

    [HttpDelete("{id:guid}/image")]
    public async Task<ActionResult<StoreCategoryAdminRecord>> RemoveImage(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = await db.StoreCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound();

        if (category.ImageUploadFileId is { } old)
        {
            category.ImageUploadFileId = null;
            category.DateUpdated = DateTime.UtcNow;
            category.UpdatedByAppUserId = userId;
            await db.SaveChangesAsync(ct);
            await images.RemoveAsync(db, old, ct);
        }
        return Ok((await ListAsync(db, ct)).Single(c => c.Id == id));
    }

    /// <summary>Removes an empty category. One holding products, or the last one, stays.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = await db.StoreCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound();

        var products = await db.StoreProducts.CountAsync(p => p.CategoryId == id, ct);
        if (products > 0)
            return BadRequest($"{category.Name} still holds {products} product{(products == 1 ? "" : "s")}. "
                            + "Move them to another category first.");
        if (!await db.StoreCategories.AnyAsync(c => c.Id != id, ct))
            return BadRequest($"{category.Name} is the last category. Add another before removing it.");

        var image = category.ImageUploadFileId;
        db.StoreCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        await TryAuditAsync(auditLog.LogDeleteAsync(nameof(StoreCategory), id, category, userId, AppSources.WebApi));
        if (image is { } old) await images.RemoveAsync(db, old, ct);

        return NoContent();
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static int IndexOf(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
        return -1;
    }

    internal static Task<List<StoreCategoryAdminRecord>> ListAsync(BenDataContext db, CancellationToken ct)
        => db.StoreCategories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new StoreCategoryAdminRecord(
                c.Id, c.Name, c.Slug, c.Description, c.ImageUploadFileId, c.SortOrder, c.IsActive, c.IsNew,
                db.StoreProducts.Count(p => p.CategoryId == c.Id),
                c.IsActive ? db.StoreProducts.Count(p => p.CategoryId == c.Id && p.IsActive) : 0,
                c.DateCreated))
            .ToListAsync(ct);

    /// <summary>Copies the request onto the row, or answers why not.</summary>
    private async Task<ActionResult?> ApplyAsync(
        BenDataContext db, StoreCategory category, SaveStoreCategoryRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest("A category needs a name.");
        if (name.Length > MaxNameLength) return BadRequest($"A category name is {MaxNameLength} characters at most.");
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description?.Length > MaxDescriptionLength)
            return BadRequest($"A category description is {MaxDescriptionLength:N0} characters at most.");

        var lowered = name.ToLower();
        if (await db.StoreCategories.AnyAsync(c => c.Id != category.Id && c.Name.ToLower() == lowered, ct))
            return BadRequest($"There's already a category called {name}.");

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var slug = StoreSlugs.Typed(request.Slug);
            if (slug is null) return BadRequest("A web address needs letters or digits in it.");
            if (await db.StoreCategories.AnyAsync(c => c.Id != category.Id && c.Slug == slug, ct))
                return Conflict($"There is already a category at /store/c/{slug}.");
            category.Slug = slug;
        }
        else if (string.IsNullOrEmpty(category.Slug))
        {
            category.Slug = await StoreSlugs.ForCategoryAsync(db, category.Id, name, ct);
        }

        category.Name = name;
        category.Description = description;
        category.IsActive = request.IsActive;
        category.IsNew = request.IsNew;
        return null;
    }

    /// <summary>Saves; two admins racing for one address get the sentence, not a 500.</summary>
    private async Task<ActionResult?> SaveOrConflictAsync(BenDataContext db, StoreCategory category, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            if (await db.StoreCategories.AsNoTracking().AnyAsync(c => c.Id != category.Id && c.Slug == category.Slug, ct))
                return Conflict($"There is already a category at /store/c/{category.Slug}.");
            throw;
        }
    }

    /// <summary>Same-type copy for the audit diff — the tracker refuses anonymous objects.</summary>
    private static StoreCategory Clone(StoreCategory c) => new()
    {
        Id = c.Id, Name = c.Name, Slug = c.Slug, Description = c.Description,
        ImageUploadFileId = c.ImageUploadFileId, SortOrder = c.SortOrder, IsActive = c.IsActive, IsNew = c.IsNew,
    };
}
