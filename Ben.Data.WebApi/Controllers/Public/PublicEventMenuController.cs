using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Menus;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// An event's published menus, for anybody looking at the event (Ben, 10/13/2026).
/// </summary>
/// <remarks>
/// <para>Ben chose "everyone" over confirmed guests only: a menu and its prices help somebody decide
/// to book. Drafts are never here, and nothing is, unless the event itself is on the public site.</para>
///
/// <para>The menus come in the order a guest reads them: by the first meal each is served at, then
/// what's on offer throughout the event.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public")]
public sealed class PublicEventMenuController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _storage;

    public PublicEventMenuController(IDbContextFactory<BenDataContext> db, IFileStorageService storage)
    { _db = db; _storage = storage; }

    [HttpGet("hosted-events/{eventId:guid}/published-menus")]
    public async Task<ActionResult<IReadOnlyList<EventMenuRecord>>> Menus(Guid eventId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await PublishedAsync(db, eventId, ct));
    }

    [HttpGet("hosted-events/{eventId:guid}/published-menus/{menuId:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid eventId, Guid menuId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var menu = (await PublishedAsync(db, eventId, ct)).FirstOrDefault(m => m.Id == menuId);
        if (menu is null) return NotFound();

        var bytes = await EventMenuPdf.RenderAsync(menu, id => EventMenuImages.ReadAsync(db, _storage, id, ct), DateTime.UtcNow.Year);
        return File(bytes, "application/pdf", EventMenuPdf.FileName(menu));
    }

    /// <summary>A menu item's photo or a menu's logo; see <see cref="EventMenuImages.MaySeeAsync"/>.</summary>
    [HttpGet("menu-image/{uploadFileId:guid}")]
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    public async Task<IActionResult> Image(Guid uploadFileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await EventMenuImages.MaySeeAsync(db, uploadFileId, GetCurrentUserId(), ct)) return NotFound();

        var file = await db.UploadFiles.AsNoTracking().Where(f => f.Id == uploadFileId)
            .Select(f => new { f.StoragePath, f.ContentType }).FirstOrDefaultAsync(ct);
        if (file?.StoragePath is not { Length: > 0 } path || !_storage.Exists(path)) return NotFound();

        Response.Headers.CacheControl = "private, max-age=600";
        return File(await _storage.OpenReadAsync(path, ct), file.ContentType ?? "image/jpeg");
    }

    /// <summary>
    /// Every design and font file, for the iPhone app, which draws a menu itself (10/13/2026).
    /// </summary>
    [HttpGet("menu-designs")]
    public ActionResult<MenuDesignCatalog> Designs()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(new MenuDesignCatalog(MenuDesigns.All, MenuFonts.Files));
    }

    /// <summary>One of the menu fonts, the same bytes the website serves and the PDF embeds.</summary>
    [HttpGet("menu-fonts/{file}")]
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    public IActionResult Font(string file)
    {
        if (MenuFonts.Read(file) is not { } bytes) return NotFound();
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(bytes, "font/ttf");
    }

    /// <summary>The published menus of an event that is on the public site, as guests read them.</summary>
    public static async Task<IReadOnlyList<EventMenuRecord>> PublishedAsync(BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var menus = await EventMenus.Whole(db)
            .Where(m => m.HostedEventId == eventId && m.PublishedUtc != null
                     && HostedEventStates.OnThePublicSite.Contains(m.HostedEvent.LifecycleState))
            .ToListAsync(ct);

        // Nobody reading a published menu can change an item, so no group is "the viewer's".
        // The planner's own title is only for the planner (Ben's menu rules: "They are given titles so
        // the event manager will know which menu they are looking at"); guests read the heading.
        return [.. EventMenus.InReadingOrder(menus.Select(m => EventMenus.Record(m, Guid.Empty, null))
                                                  .Select(m => m with { Title = m.ShownHeading }))
            // A menu with nowhere to be served isn't on show, even if it was published before its meal went.
            .Where(m => m.ThroughoutTheEvent || m.Meals.Count > 0)];
    }
}
