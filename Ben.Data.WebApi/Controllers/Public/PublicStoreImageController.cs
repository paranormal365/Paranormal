using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Store pictures — a category's, a product's, and the one an order line was bought with
/// (storefront S1.8).
/// </summary>
/// <remarks>
/// <para><b>Not behind the store switch</b>, on purpose. The catalogue is entered while the shop is
/// dark, and an admin building a product page has to see its pictures; a buyer's order page shows
/// the picture of what they bought whether the shop is open or not.</para>
///
/// <para><b>The authority is being referenced.</b> A file id serves here only while a category, a
/// product picture or an order line points at it — active or not — and never any other upload,
/// so this door cannot be used to read somebody's case photo by guessing its id.</para>
///
/// <para><b>Cached for a year.</b> A store picture is never edited in place: replacing one makes a
/// new file with a new id, so the address of a picture is the address of those exact bytes.</para>
/// </remarks>
[ApiController]
[Route("api/public/store-image")]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class PublicStoreImageController(
    IDbContextFactory<BenDataContext> dbFactory, IFileStorageService storage, IMediaSanitizationService images)
    : BenControllerBase
{
    public const string CacheForever = "public, max-age=31536000, immutable";

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => ServeAsync(id, thumbnail: false, ct);

    [HttpGet("{id:guid}/thumb")]
    public Task<IActionResult> GetThumbnail(Guid id, CancellationToken ct) => ServeAsync(id, thumbnail: true, ct);

    private async Task<IActionResult> ServeAsync(Guid id, bool thumbnail, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var referenced = await db.StoreProductImages.AnyAsync(i => i.UploadFileId == id, ct)
                      || await db.StoreCategories.AnyAsync(c => c.ImageUploadFileId == id, ct)
                      || await db.StoreOrderItems.AnyAsync(i => i.ImageUploadFileId == id, ct);
        if (!referenced) return NotFound();

        var file = await db.UploadFiles.AsNoTracking().Where(f => f.Id == id)
            .Select(f => new { f.StoragePath, f.ContentType, HasData = f.FileData != null })
            .FirstOrDefaultAsync(ct);
        if (file is null) return NotFound();

        Response.Headers.CacheControl = CacheForever;
        var contentType = file.ContentType ?? "image/jpeg";

        if (!string.IsNullOrWhiteSpace(file.StoragePath))
        {
            var path = thumbnail && storage.Exists(images.ThumbnailPathFor(file.StoragePath))
                ? images.ThumbnailPathFor(file.StoragePath)
                : file.StoragePath;
            if (!storage.Exists(path)) return NotFound();
            return File(await storage.OpenReadAsync(path, ct), thumbnail ? "image/jpeg" : contentType);
        }

        // Seeded pictures live in the row. Only then is the column read — a stored file's row
        // carries no bytes and the query above never loaded them.
        if (!file.HasData) return NotFound();
        var bytes = await db.UploadFiles.AsNoTracking().Where(f => f.Id == id).Select(f => f.FileData!).SingleAsync(ct);
        return File(bytes, contentType);
    }
}
