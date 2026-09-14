using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A venue's photo library: its own pictures, and the ones organizers offer from their events (item 235 phase 12).
/// </summary>
/// <remarks>
/// The same permission as the venue profile it belongs to. Pictures are fitted and stripped exactly as an event
/// gallery's are (<see cref="PictureFitting"/>). An offered picture waits for the venue to accept it; declining
/// removes only the offer, never the organizer's file.
/// </remarks>
[Route("api/organizations/{orgId:guid}/venue-profiles/{profileId:guid}/photos")]
public sealed class VenuePhotoController : OrgCmsControllerBase
{
    private static readonly Guid PhotoFileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly IFileStorageService _storage;
    private readonly IMediaIngestService _ingest;
    private readonly IMediaSanitizationService _images;

    public VenuePhotoController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        IFileStorageService storage, IMediaIngestService ingest, IMediaSanitizationService images)
        : base(dbFactory, mapper, security)
    { _storage = storage; _ingest = ingest; _images = images; }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VenuePhotoRecord>>> Get(Guid orgId, Guid profileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayEditAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await OwnsAsync(db, orgId, profileId, ct)) return NotFound();
        return Ok(await ListAsync(db, profileId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<VenuePhotoRecord>>> Upload(
        Guid orgId, Guid profileId, IFormFile? file, [FromForm] string? caption, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayEditAsync(userId.Value, orgId, ct)) return Forbid();
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await OwnsAsync(db, orgId, profileId, ct)) return NotFound();
        if (await WhyFullAsync(db, profileId, ct) is { } full) return BadRequest(full);

        var (picture, unreadable) = await PictureFitting.FitAsync(file, _images, ct);
        if (picture is null) return BadRequest(unreadable);

        var now = DateTime.UtcNow;
        var storedName = $"{Guid.NewGuid():N}.jpg";
        var storagePath = _storage.OrgFilePath(orgId, $"venue/{profileId:N}/{storedName}");
        await _storage.WriteAsync(storagePath, new MemoryStream(picture.Bytes), ct);

        var upload = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = PhotoFileTypeId, OwnerOrganizationId = orgId,
            FileName = Path.ChangeExtension(Path.GetFileName(file.FileName), ".jpg"), StoredFileName = storedName,
            ContentType = "image/jpeg", FileSize = picture.Bytes.LongLength, StoragePath = storagePath, IsPublic = true,
            DateCreated = now, CreatedByAppUserId = userId.Value,
        };
        db.UploadFiles.Add(upload);
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = upload.Id, MediaKind = "Image",
            WidthPixels = picture.Width, HeightPixels = picture.Height, ExtractedAtUtc = now,
        });
        db.VenuePhotos.Add(new VenuePhoto
        {
            Id = Guid.NewGuid(), OrganizationVenueProfileId = profileId, UploadFileId = upload.Id,
            Caption = Caption(caption), SortOrder = await NextSortAsync(db, profileId, ct),
            AcceptedUtc = now, DateCreated = now, CreatedByAppUserId = userId.Value,
        });
        await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, profileId, ct));
    }

    [HttpPut("{photoId:guid}")]
    public async Task<ActionResult<IReadOnlyList<VenuePhotoRecord>>> Update(
        Guid orgId, Guid profileId, Guid photoId, [FromBody] UpdateVenuePhotoRequest request, CancellationToken ct)
        => await ChangeAsync(orgId, profileId, photoId, (photo, now) =>
        {
            photo.Caption = Caption(request.Caption);
            if (request.SortOrder is { } order) photo.SortOrder = order;
            return Task.FromResult<string?>(null);
        }, ct);

    /// <summary>Keeps a picture an organizer offered: it joins the library and the public page.</summary>
    [HttpPost("{photoId:guid}/accept")]
    public async Task<ActionResult<IReadOnlyList<VenuePhotoRecord>>> Accept(Guid orgId, Guid profileId, Guid photoId, CancellationToken ct)
        => await ChangeAsync(orgId, profileId, photoId, async (photo, now) =>
        {
            await using var db = await DbFactory.CreateDbContextAsync(ct);
            if (photo.AcceptedUtc is null && await WhyFullAsync(db, profileId, ct) is { } full) return full;
            photo.AcceptedUtc ??= now;
            return null;
        }, ct);

    /// <summary>
    /// Removes a picture from the library, or declines an offer. The venue's own file goes too; an organizer's
    /// stays theirs.
    /// </summary>
    [HttpDelete("{photoId:guid}")]
    public async Task<ActionResult<IReadOnlyList<VenuePhotoRecord>>> Delete(Guid orgId, Guid profileId, Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayEditAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await OwnsAsync(db, orgId, profileId, ct)) return NotFound();

        var photo = await db.VenuePhotos.Include(p => p.UploadFile)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.OrganizationVenueProfileId == profileId, ct);
        if (photo is null) return NotFound();

        var ours = photo.OfferedByOrganizationId is null && photo.UploadFile.OwnerOrganizationId == orgId;
        var path = photo.UploadFile.StoragePath;
        db.VenuePhotos.Remove(photo);
        await db.SaveChangesAsync(ct);

        // The venue's own file goes with it. An organizer's file goes only when nothing of theirs still uses it —
        // a gallery tidied away by the 90-day rule left it alive for this library alone. TryDelete refuses while
        // anything else points at the file.
        var orphaned = !ours && !await db.HostedEventGalleryImages.AnyAsync(g => g.UploadFileId == photo.UploadFileId, ct);
        if ((ours || orphaned)
            && await Services.Admin.UploadFileRows.TryDeleteAsync(db, photo.UploadFileId, ct) && path is { Length: > 0 })
            await _ingest.DeleteAllAsync(path, ct);

        return Ok(await ListAsync(db, profileId, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private async Task<ActionResult<IReadOnlyList<VenuePhotoRecord>>> ChangeAsync(
        Guid orgId, Guid profileId, Guid photoId, Func<VenuePhoto, DateTime, Task<string?>> change, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayEditAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await OwnsAsync(db, orgId, profileId, ct)) return NotFound();

        var photo = await db.VenuePhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.OrganizationVenueProfileId == profileId, ct);
        if (photo is null) return NotFound();

        var now = DateTime.UtcNow;
        if (await change(photo, now) is { } refusal) return BadRequest(refusal);
        photo.DateUpdated = now;
        photo.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, profileId, ct));
    }

    private Task<bool> MayEditAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsCmsAuthorizedAsync(userId, orgId, OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private static Task<bool> OwnsAsync(BenDataContext db, Guid orgId, Guid profileId, CancellationToken ct)
        => db.OrganizationVenueProfiles.AnyAsync(p => p.Id == profileId && p.OrganizationId == orgId, ct);

    internal static async Task<string?> WhyFullAsync(BenDataContext db, Guid profileId, CancellationToken ct)
        => await db.VenuePhotos.CountAsync(p => p.OrganizationVenueProfileId == profileId && p.AcceptedUtc != null, ct) >= VenuePhoto.MaxPerVenue
            ? $"A venue's library holds up to {VenuePhoto.MaxPerVenue} pictures. Remove one to make room."
            : null;

    private static async Task<int> NextSortAsync(BenDataContext db, Guid profileId, CancellationToken ct)
        => (await db.VenuePhotos.Where(p => p.OrganizationVenueProfileId == profileId).Select(p => (int?)p.SortOrder).MaxAsync(ct) ?? -1) + 1;

    private static string? Caption(string? caption)
        => caption?.Trim() is { Length: > 0 } c ? (c.Length > 300 ? c[..300] : c) : null;

    internal static async Task<IReadOnlyList<VenuePhotoRecord>> ListAsync(BenDataContext db, Guid profileId, CancellationToken ct)
        => await db.VenuePhotos.AsNoTracking()
            .Where(p => p.OrganizationVenueProfileId == profileId)
            .OrderBy(p => p.AcceptedUtc == null ? 0 : 1).ThenBy(p => p.SortOrder)
            .Select(p => new VenuePhotoRecord(
                p.Id, p.UploadFileId, p.Caption, p.SortOrder, p.AcceptedUtc,
                p.OfferedByOrganization != null ? p.OfferedByOrganization.Name : null,
                p.OfferedFromHostedEvent != null ? p.OfferedFromHostedEvent.Name : null))
            .ToListAsync(ct);
}
