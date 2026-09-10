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
[Route("api/public")]
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

    /// <summary>
    /// One person's public photograph, addressed by the file rather than by the person.
    /// </summary>
    /// <remarks>
    /// By file id on purpose. A route keyed on the user would hand every caller a way to ask
    /// "does this person have a photograph?" for any id they could guess; this one only answers
    /// for a file somebody was already told about, and only while that file is still a photograph
    /// its owner has published.
    /// </remarks>
    [HttpGet("guide-photo/{uploadFileId:guid}")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    public async Task<IActionResult> GetPhoto(Guid uploadFileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var photo = await db.AppUserPhotos.AsNoTracking()
            .Where(p => p.UploadFileId == uploadFileId && p.IsPublic && p.IsActive)
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

    /// <summary>
    /// A picture from a tour's gallery (item 233).
    /// </summary>
    /// <remarks>
    /// The same shape as the guide photograph above and for the same reason: a tour page is read
    /// by people with no account, so its pictures cannot sit behind a bearer token. The gallery
    /// row is the authority — take the picture off the page and this stops serving it.
    /// </remarks>
    [HttpGet("tour-photo/{uploadFileId:guid}")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    public async Task<IActionResult> GetTourPhoto(Guid uploadFileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var picture = await db.TourGalleryImages.AsNoTracking()
            .Where(g => g.UploadFileId == uploadFileId)
            .Select(g => new { g.UploadFile.StoragePath, g.UploadFile.ContentType })
            .FirstOrDefaultAsync(ct);

        if (picture?.StoragePath is not { Length: > 0 } path) return NotFound();

        var serving = _media.ServingPathFor(path);
        if (!_storage.Exists(serving)) return NotFound();

        var stream = await _storage.OpenReadAsync(serving, ct);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, picture.ContentType ?? "image/jpeg");
    }
}
