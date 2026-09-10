using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A person's public photograph, for anyone who has been told they will be meeting them.
/// </summary>
/// <remarks>
/// <para>Built for item 233: Ben asked that a guest be sent "name and photo of person who will be
/// leading the tour - for safety". The face has to reach somebody who may have no account at all —
/// it is in an email — so this route is anonymous.</para>
///
/// <para><b>It serves exactly one thing: a photo its owner marked public and active.</b> Anything
/// else answers 404, including a private photo, a retired one, and a person with none. There is no
/// listing here and no way to ask for somebody's photos in bulk: the caller must already know the
/// user id, which they only get by being handed it in a tour or an event.</para>
/// </remarks>
[ApiController]
[Route("api/public/users")]
public sealed class PublicUserPhotoController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly Ben.Data.Common.Interfaces.IFileStorageService _storage;
    private readonly IMediaIngestService _media;

    public PublicUserPhotoController(
        IDbContextFactory<BenDataContext> db,
        Ben.Data.Common.Interfaces.IFileStorageService storage,
        IMediaIngestService media)
    { _db = db; _storage = storage; _media = media; }

    [HttpGet("{userId:guid}/photo")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    public async Task<IActionResult> GetPhoto(Guid userId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var photo = await db.AppUserPhotos.AsNoTracking()
            .Where(p => p.AppUserId == userId && p.IsPublic && p.IsActive)
            .OrderByDescending(p => p.DateCreated)
            .Select(p => new { p.UploadFile.StoragePath, p.UploadFile.ContentType })
            .FirstOrDefaultAsync(ct);

        if (photo?.StoragePath is not { Length: > 0 } path) return NotFound();

        var serving = _media.ServingPathFor(path);
        if (!_storage.Exists(serving)) return NotFound();

        var stream = await _storage.OpenReadAsync(serving, ct);
        // Cached hard: a face does not change often, and a guest opening the same mail twice
        // should not fetch it twice.
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, photo.ContentType ?? "image/jpeg");
    }
}
