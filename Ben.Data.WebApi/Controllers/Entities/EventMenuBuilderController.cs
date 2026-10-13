using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Menus;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Building an event's menus: the item library, the menus, publishing and the PDF (Ben, 10/13/2026).
/// </summary>
/// <remarks>
/// <para><b>Who:</b> <see cref="Services.Access.HostedEventAccess.CanEditMenusAsync"/>, the event's
/// editors and the staff given "Menus and the kitchen's sheet". (The meals screen's own controller
/// checked event editing only, so kitchen staff couldn't open it; this one doesn't repeat that.)</para>
///
/// <para><b>Refusals are sentences</b>, never records, so the website shows the reason as written.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/menu-builder")]
public sealed class EventMenuBuilderController : OrgCmsControllerBase
{
    private static readonly Guid ImageFileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly Services.Access.HostedEventAccess _access;
    private readonly IFileStorageService _storage;
    private readonly IMediaSanitizationService _images;

    public EventMenuBuilderController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, IFileStorageService storage, IMediaSanitizationService images)
        : base(dbFactory, mapper, security)
    { _access = access; _storage = storage; _images = images; }

    /// <summary>The caller, the event, and the venue's group, once the caller may build menus here.</summary>
    private async Task<(Guid UserId, HostedEvent Event, Guid? VenueOrgId, BenDataContext Db)?> OpenAsync(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return null;
        var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditMenusAsync(userId, orgId, eventId, db, ct)) { await db.DisposeAsync(); return null; }

        var hosted = await db.HostedEvents.Include(e => e.Place)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) { await db.DisposeAsync(); return null; }

        return (userId, hosted, await EventMenus.VenueOrgAsync(db, hosted.PlaceId, ct), db);
    }

    private ActionResult Refused() => GetCurrentUserId() is null ? Unauthorized() : NotFound();

    // ── everything at once ───────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<EventMenuWorkspaceRecord>> Workspace(Guid orgId, Guid eventId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;
        return Ok(await WorkspaceAsync(db, open.Event, orgId, open.VenueOrgId, ct));
    }

    private static async Task<EventMenuWorkspaceRecord> WorkspaceAsync(
        BenDataContext db, HostedEvent hosted, Guid orgId, Guid? venueOrgId, CancellationToken ct)
    {
        var menus = (await EventMenus.Whole(db).Where(m => m.HostedEventId == hosted.Id).ToListAsync(ct))
            .Select(m => EventMenus.Record(m, orgId, venueOrgId));
        var library = await EventMenus.AvailableTo(db, hosted).AsNoTracking()
            .Include(i => i.MenuCategory).Include(i => i.Organization)
            .OrderBy(i => i.MenuCategory.SortOrder).ThenBy(i => i.Name).ToListAsync(ct);
        var categories = await db.MenuCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);

        return new EventMenuWorkspaceRecord(
            [.. EventMenus.InReadingOrder(menus).Select(EventMenus.Summary)],
            [.. categories.Select(EventMenus.CategoryRecord)],
            [.. library.Select(i => EventMenus.ItemRecord(i, orgId, venueOrgId))],
            await EventMenus.MealsAsync(db, hosted.Id, ct),
            hosted.Name, hosted.Place?.Name, hosted.Place is not null);
    }

    [HttpGet("designs")]
    public ActionResult<IReadOnlyList<MenuDesignRecord>> Designs()
        => Ok(MenuDesigns.All.Select(d => new MenuDesignRecord(d.Key, d.Name, d.Family, d.Description)).ToList());

    // ── categories ───────────────────────────────────────────────────────────

    /// <summary>Adds a category to the site's list, or hands back the one it already has by that name.</summary>
    [HttpPost("categories")]
    public async Task<ActionResult<AddMenuCategoryResult>> AddCategory(
        Guid orgId, Guid eventId, [FromBody] AddMenuCategoryRequest request, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        if (string.IsNullOrWhiteSpace(request.Name) || MenuCategory.KeyFor(request.Name).Length == 0)
            return BadRequest("Give the category a name.");
        if (request.Name.Trim().Length > 60) return BadRequest("A category's name can be at most 60 characters.");

        var (category, existed) = await EventMenus.FindOrAddCategoryAsync(db, request.Name, open.UserId, ct);
        return Ok(new AddMenuCategoryResult(EventMenus.CategoryRecord(category), existed));
    }

    // ── the library ──────────────────────────────────────────────────────────

    [HttpPost("library")]
    public async Task<ActionResult<MenuLibraryItemRecord>> AddItem(
        Guid orgId, Guid eventId, [FromBody] SaveMenuLibraryItemRequest request, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var categories = (await db.MenuCategories.Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        if (EventMenus.WhyTheItemCannotBeSaved(request, categories) is { } why) return BadRequest(why);

        var now = DateTime.UtcNow;
        var item = new MenuLibraryItem
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, DateCreated = now, CreatedByAppUserId = open.UserId,
        };
        Fill(item, request, open.Event);
        db.MenuLibraryItems.Add(item);
        await db.SaveChangesAsync(ct);

        return Ok(await ItemAsync(db, item.Id, orgId, open.VenueOrgId, ct));
    }

    [HttpPut("library/{itemId:guid}")]
    public async Task<ActionResult<MenuLibraryItemRecord>> SaveItem(
        Guid orgId, Guid eventId, Guid itemId, [FromBody] SaveMenuLibraryItemRequest request, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var item = await EventMenus.AvailableTo(db, open.Event).FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();
        if (!EventMenus.MayEdit(item, orgId, open.VenueOrgId))
            return StatusCode(StatusCodes.Status403Forbidden, "Only the group that saved this item, or the venue, can change it.");

        var categories = (await db.MenuCategories.Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        if (EventMenus.WhyTheItemCannotBeSaved(request, categories) is { } why) return BadRequest(why);

        // A venue's own item stays the venue's: another group can't move it into its own keeping.
        if (item.OrganizationId != orgId && request.Scope != MenuItemScope.Venue)
            return BadRequest("This item belongs to the venue, so it stays kept at the venue.");

        Fill(item, request, open.Event);
        item.DateUpdated = DateTime.UtcNow;
        item.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);

        return Ok(await ItemAsync(db, item.Id, orgId, open.VenueOrgId, ct));
    }

    /// <summary>Stops offering an item for new menus. Menus that already have it keep it.</summary>
    [HttpPost("library/{itemId:guid}/retire")]
    public async Task<ActionResult<MenuLibraryItemRecord>> RetireItem(Guid orgId, Guid eventId, Guid itemId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var item = await EventMenus.AvailableTo(db, open.Event).FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();
        if (!EventMenus.MayEdit(item, orgId, open.VenueOrgId))
            return StatusCode(StatusCodes.Status403Forbidden, "Only the group that saved this item, or the venue, can remove it.");

        item.RetiredUtc = DateTime.UtcNow;
        item.DateUpdated = item.RetiredUtc;
        item.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await ItemAsync(db, item.Id, orgId, open.VenueOrgId, ct));
    }

    [HttpPost("library/{itemId:guid}/image")]
    public async Task<ActionResult<MenuLibraryItemRecord>> ItemImage(
        Guid orgId, Guid eventId, Guid itemId, IFormFile? file, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var item = await EventMenus.AvailableTo(db, open.Event).FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();
        if (!EventMenus.MayEdit(item, orgId, open.VenueOrgId))
            return StatusCode(StatusCodes.Status403Forbidden, "Only the group that saved this item, or the venue, can change its picture.");

        var (upload, refusal) = await StorePictureAsync(db, file, item.OrganizationId, $"menus/items/{item.Id:N}", open.UserId, ct);
        if (upload is null) return BadRequest(refusal);

        item.ImageUploadFileId = upload.Id;
        item.DateUpdated = DateTime.UtcNow;
        item.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await ItemAsync(db, item.Id, orgId, open.VenueOrgId, ct));
    }

    [HttpDelete("library/{itemId:guid}/image")]
    public async Task<ActionResult<MenuLibraryItemRecord>> RemoveItemImage(Guid orgId, Guid eventId, Guid itemId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var item = await EventMenus.AvailableTo(db, open.Event).FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();
        if (!EventMenus.MayEdit(item, orgId, open.VenueOrgId))
            return StatusCode(StatusCodes.Status403Forbidden, "Only the group that saved this item, or the venue, can change its picture.");

        item.ImageUploadFileId = null;
        item.DateUpdated = DateTime.UtcNow;
        item.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await ItemAsync(db, item.Id, orgId, open.VenueOrgId, ct));
    }

    private static void Fill(MenuLibraryItem item, SaveMenuLibraryItemRequest r, HostedEvent hosted)
    {
        item.Scope = r.Scope;
        item.HostedEventId = r.Scope == MenuItemScope.Event ? hosted.Id : null;
        item.PlaceId = r.Scope == MenuItemScope.Venue ? hosted.PlaceId : null;
        item.MenuCategoryId = r.MenuCategoryId;
        item.Name = r.Name.Trim();
        item.Description = r.Description?.Trim() is { Length: > 0 } d ? d : null;
        item.Dietary = r.Dietary;
        item.Allergens = r.Allergens;
        item.DietaryNote = r.DietaryNote?.Trim() is { Length: > 0 } n ? n : null;
    }

    private static async Task<MenuLibraryItemRecord> ItemAsync(
        BenDataContext db, Guid itemId, Guid orgId, Guid? venueOrgId, CancellationToken ct)
    {
        var item = await db.MenuLibraryItems.AsNoTracking()
            .Include(i => i.MenuCategory).Include(i => i.Organization)
            .FirstAsync(i => i.Id == itemId, ct);
        return EventMenus.ItemRecord(item, orgId, venueOrgId);
    }

    // ── menus ────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<EventMenuRecord>> Create(
        Guid orgId, Guid eventId, [FromBody] CreateEventMenuRequest request, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Give the menu a name, so you can tell it from the others.");
        if (request.Title.Trim().Length > 120) return BadRequest("The menu's name can be at most 120 characters.");

        var now = DateTime.UtcNow;
        var menu = new EventMenu
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, Title = request.Title.Trim(),
            DesignKey = MenuDesigns.FindOrDefault(request.DesignKey).Key,
            DateCreated = now, CreatedByAppUserId = open.UserId,
        };
        db.EventMenus.Add(menu);
        await db.SaveChangesAsync(ct);
        return Ok(await MenuAsync(db, menu.Id, orgId, open.VenueOrgId, ct));
    }

    [HttpGet("{menuId:guid}")]
    public async Task<ActionResult<EventMenuRecord>> Get(Guid orgId, Guid eventId, Guid menuId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;
        if (!await db.EventMenus.AnyAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct)) return NotFound();
        return Ok(await MenuAsync(db, menuId, orgId, open.VenueOrgId, ct));
    }

    /// <summary>
    /// Saves the whole menu. A published menu stays published, and a change that would make it
    /// unpublishable is refused rather than saved half-shown.
    /// </summary>
    [HttpPut("{menuId:guid}")]
    public async Task<ActionResult<EventMenuRecord>> Save(
        Guid orgId, Guid eventId, Guid menuId, [FromBody] SaveEventMenuRequest request, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var menu = await db.EventMenus
            .Include(m => m.Prices).Include(m => m.Servings).Include(m => m.Sections).ThenInclude(s => s.Entries)
            .FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (menu is null) return NotFound();

        // An item already on this menu stays usable here even after it is retired or another
        // group stops sharing it; only items being newly added must be available.
        var onIt = menu.Sections.SelectMany(s => s.Entries).Select(e => e.MenuLibraryItemId).ToHashSet();
        var usable = (await EventMenus.AvailableTo(db, open.Event).Select(i => i.Id).ToListAsync(ct)).ToHashSet();
        usable.UnionWith(onIt);
        var categories = (await db.MenuCategories.Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        var meals = (await db.HostedEventMenus.Where(m => m.HostedEventNight.HostedEventId == eventId)
            .Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        if (EventMenus.WhyItCannotBeSaved(request, usable, categories, meals) is { } why) return BadRequest(why);

        EventMenus.Apply(db, menu, request, open.UserId, DateTime.UtcNow);

        if (menu.IsPublished && EventMenus.WhyItCannotBePublished(menu) is { } stillShown)
            return BadRequest($"This menu is published, so it has to stay complete. {stillShown} "
                            + "Or unpublish it first.");

        await db.SaveChangesAsync(ct);
        return Ok(await MenuAsync(db, menuId, orgId, open.VenueOrgId, ct));
    }

    [HttpDelete("{menuId:guid}")]
    public async Task<ActionResult> Delete(Guid orgId, Guid eventId, Guid menuId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var menu = await db.EventMenus.FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (menu is null) return NotFound();

        // Copies keep their content; they only lose the note of where they came from.
        foreach (var copy in await db.EventMenus.Where(m => m.CopiedFromEventMenuId == menuId).ToListAsync(ct))
            copy.CopiedFromEventMenuId = null;
        db.EventMenus.Remove(menu);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>"Friday dinner Copy 1": everything but its meals and its publishing.</summary>
    [HttpPost("{menuId:guid}/copy")]
    public async Task<ActionResult<EventMenuRecord>> Copy(Guid orgId, Guid eventId, Guid menuId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var source = await db.EventMenus.AsNoTracking()
            .Include(m => m.Prices).Include(m => m.Sections).ThenInclude(s => s.Entries)
            .FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (source is null) return NotFound();

        var titles = await db.EventMenus.Where(m => m.HostedEventId == eventId).Select(m => m.Title).ToListAsync(ct);
        var copy = EventMenus.Copy(source, EventMenus.CopyTitle(source.Title, titles), eventId, open.UserId, DateTime.UtcNow);
        db.EventMenus.Add(copy);
        await db.SaveChangesAsync(ct);
        return Ok(await MenuAsync(db, copy.Id, orgId, open.VenueOrgId, ct));
    }

    [HttpPost("{menuId:guid}/publish")]
    public async Task<ActionResult<EventMenuRecord>> Publish(Guid orgId, Guid eventId, Guid menuId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var menu = await db.EventMenus
            .Include(m => m.Prices).Include(m => m.Servings).Include(m => m.Sections).ThenInclude(s => s.Entries)
            .FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (menu is null) return NotFound();
        if (EventMenus.WhyItCannotBePublished(menu) is { } why) return BadRequest(why);

        menu.PublishedUtc ??= DateTime.UtcNow;
        menu.DateUpdated = DateTime.UtcNow;
        menu.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await MenuAsync(db, menuId, orgId, open.VenueOrgId, ct));
    }

    [HttpPost("{menuId:guid}/unpublish")]
    public async Task<ActionResult<EventMenuRecord>> Unpublish(Guid orgId, Guid eventId, Guid menuId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var menu = await db.EventMenus.FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (menu is null) return NotFound();

        menu.PublishedUtc = null;
        menu.DateUpdated = DateTime.UtcNow;
        menu.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await MenuAsync(db, menuId, orgId, open.VenueOrgId, ct));
    }

    [HttpPost("{menuId:guid}/logo")]
    public async Task<ActionResult<EventMenuRecord>> Logo(Guid orgId, Guid eventId, Guid menuId, IFormFile? file, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var menu = await db.EventMenus.FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (menu is null) return NotFound();

        var (upload, refusal) = await StorePictureAsync(db, file, orgId, $"events/{eventId:N}/menus/{menuId:N}", open.UserId, ct);
        if (upload is null) return BadRequest(refusal);

        menu.LogoUploadFileId = upload.Id;
        menu.DateUpdated = DateTime.UtcNow;
        menu.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await MenuAsync(db, menuId, orgId, open.VenueOrgId, ct));
    }

    [HttpDelete("{menuId:guid}/logo")]
    public async Task<ActionResult<EventMenuRecord>> RemoveLogo(Guid orgId, Guid eventId, Guid menuId, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var menu = await db.EventMenus.FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (menu is null) return NotFound();

        menu.LogoUploadFileId = null;
        menu.DateUpdated = DateTime.UtcNow;
        menu.UpdatedByAppUserId = open.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await MenuAsync(db, menuId, orgId, open.VenueOrgId, ct));
    }

    /// <summary>The menu as a PDF, published or not, in the design it is set to (or the one asked for).</summary>
    [HttpGet("{menuId:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid orgId, Guid eventId, Guid menuId, [FromQuery] string? design, CancellationToken ct)
    {
        if (await OpenAsync(orgId, eventId, ct) is not { } open) return Refused();
        await using var db = open.Db;

        var menu = await EventMenus.Whole(db).FirstOrDefaultAsync(m => m.Id == menuId && m.HostedEventId == eventId, ct);
        if (menu is null) return NotFound();

        var record = EventMenus.Record(menu, orgId, open.VenueOrgId);
        if (MenuDesigns.Find(design) is { } chosen) record = record with { DesignKey = chosen.Key };

        var bytes = await EventMenuPdf.RenderAsync(record, id => ReadImageAsync(db, id, ct), DateTime.UtcNow.Year);
        return File(bytes, "application/pdf", EventMenuPdf.FileName(record));
    }

    private async Task<byte[]?> ReadImageAsync(BenDataContext db, Guid uploadFileId, CancellationToken ct)
        => await EventMenuImages.ReadAsync(db, _storage, uploadFileId, ct);

    private static async Task<EventMenuRecord> MenuAsync(
        BenDataContext db, Guid menuId, Guid orgId, Guid? venueOrgId, CancellationToken ct)
        => EventMenus.Record(await EventMenus.Whole(db).FirstAsync(m => m.Id == menuId, ct), orgId, venueOrgId);

    // ── pictures ─────────────────────────────────────────────────────────────

    /// <summary>Fits and stores a picture the way the gallery does, owned by the given group.</summary>
    private async Task<(UploadFile? Upload, string? Refusal)> StorePictureAsync(
        BenDataContext db, IFormFile? file, Guid ownerOrgId, string folder, Guid userId, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return (null, "There was no picture in that upload.");

        var (picture, unreadable) = await PictureFitting.FitAsync(file, _images, ct);
        if (picture is null) return (null, unreadable);

        var now = DateTime.UtcNow;
        var storedName = $"{Guid.NewGuid():N}.jpg";
        var storagePath = _storage.OrgFilePath(ownerOrgId, $"{folder}/{storedName}");
        await _storage.WriteAsync(storagePath, new MemoryStream(picture.Bytes), ct);

        var upload = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = ImageFileTypeId, OwnerOrganizationId = ownerOrgId,
            FileName = Path.ChangeExtension(Path.GetFileName(file.FileName), ".jpg"), StoredFileName = storedName,
            ContentType = "image/jpeg", FileSize = picture.Bytes.LongLength, StoragePath = storagePath, IsPublic = false,
            DateCreated = now, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(upload);
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = upload.Id, MediaKind = "Image",
            WidthPixels = picture.Width, HeightPixels = picture.Height, ExtractedAtUtc = now,
        });
        return (upload, null);
    }
}
