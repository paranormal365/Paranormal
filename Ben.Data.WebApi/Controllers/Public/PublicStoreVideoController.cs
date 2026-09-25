using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Store product videos (store sellers, backlog 251, P14) — the copy with its metadata stripped where
/// the host could, in ranges, so a phone can seek without downloading the lot.
/// </summary>
/// <remarks>
/// <para><b>Not behind the store switch</b>, like the pictures: the editor's preview plays them while
/// the shop is dark.</para>
///
/// <para><b>The authority is being referenced.</b> A file serves here only while a product's video
/// row holds it, and never any other upload, so this door can't be used to read somebody's case
/// recording by guessing its id. A video is never edited in place, so it is cached for a year.</para>
/// </remarks>
[ApiController]
[Route("api/public/store-video")]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class PublicStoreVideoController(IDbContextFactory<BenDataContext> dbFactory, StoreImageStorage storage) : BenControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProductVideos.AnyAsync(v => v.UploadFileId == id, ct)) return NotFound();
        var file = await db.UploadFiles.AsNoTracking().Where(f => f.Id == id)
            .Select(f => new { f.StoragePath, f.ContentType }).FirstOrDefaultAsync(ct);
        if (file is not { StoragePath: { Length: > 0 } path }) return NotFound();

        Response.Headers.CacheControl = PublicStoreImageController.CacheForever;
        return File(await storage.OpenServingAsync(path, ct), file.ContentType ?? "video/mp4", enableRangeProcessing: true);
    }
}
