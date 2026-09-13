using AutoMapper;
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
/// The host's pictures on an event's public page (item 235 phase 11).
/// </summary>
/// <remarks>
/// A copy of the tour gallery's rules, deliberately: fitted inside 1920×1080, stripped of what the camera
/// wrote, owned by the group, fifty at most, first one the hero. Added by whoever may edit the event.
/// A guest's photo from the room never lands here by itself — see <see cref="HostedEventGalleryImage"/>.
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/gallery")]
public sealed class HostedEventGalleryController : OrgCmsControllerBase
{
    private static readonly Guid GalleryFileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly Services.Access.HostedEventAccess _access;
    private readonly IFileStorageService _storage;
    private readonly IMediaIngestService _ingest;
    private readonly IMediaSanitizationService _images;

    public HostedEventGalleryController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, IFileStorageService storage, IMediaIngestService ingest,
        IMediaSanitizationService images)
        : base(dbFactory, mapper, security)
    { _access = access; _storage = storage; _ingest = ingest; _images = images; }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HostedEventImageRecord>>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();
        return Ok(await ListAsync(db, eventId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<HostedEventImageRecord>>> Upload(
        Guid orgId, Guid eventId, IFormFile? file, [FromForm] string? caption, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");
        if (!_images.CanSanitize(file.ContentType)) return BadRequest("A gallery takes photographs — JPEG, PNG or similar.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();
        if (await FullAsync(db, eventId, ct) is { } full) return BadRequest(full);
        if (await Services.Events.EventStorage.WhyItDoesNotFitAsync(db, eventId, file.Length, ct) is { } noRoom) return BadRequest(noRoom);

        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);

        byte[] fitted;
        int width, height;
        try
        {
            var info = SkiaSharp.SKBitmap.DecodeBounds(buffer.ToArray());
            if (info.Width <= 0 || info.Height <= 0) return BadRequest("That picture could not be read.");
            var scale = Math.Min(Math.Min((double)TourGalleryImage.MaxWidth / info.Width, (double)TourGalleryImage.MaxHeight / info.Height), 1.0);
            fitted = _images.Sanitize(buffer.ToArray(), Math.Max(1, (int)Math.Round(Math.Max(info.Width, info.Height) * scale)));
            var bounds = SkiaSharp.SKBitmap.DecodeBounds(fitted);
            (width, height) = (bounds.Width, bounds.Height);
        }
        catch (UnreadableImageException)
        {
            return BadRequest("That picture could not be read.");
        }

        var now = DateTime.UtcNow;
        var storedName = $"{Guid.NewGuid():N}.jpg";
        var storagePath = _storage.OrgFilePath(orgId, $"events/{eventId:N}/gallery/{storedName}");
        await _storage.WriteAsync(storagePath, new MemoryStream(fitted), ct);

        var upload = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = GalleryFileTypeId, OwnerOrganizationId = orgId,
            FileName = Path.ChangeExtension(Path.GetFileName(file.FileName), ".jpg"), StoredFileName = storedName,
            ContentType = "image/jpeg", FileSize = fitted.LongLength, StoragePath = storagePath, IsPublic = true,
            DateCreated = now, CreatedByAppUserId = userId.Value,
        };
        db.UploadFiles.Add(upload);
        // What the copy is, truthfully: decoded and re-encoded, so no camera and no co-ordinates.
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = upload.Id, MediaKind = "Image",
            WidthPixels = width, HeightPixels = height, ExtractedAtUtc = now,
        });

        if (await FullAsync(db, eventId, ct) is { } nowFull)
        {
            await _storage.DeleteAsync(storagePath, ct);
            return BadRequest(nowFull);
        }

        var next = await db.HostedEventGalleryImages.Where(g => g.HostedEventId == eventId).Select(g => (int?)g.SortOrder).MaxAsync(ct) ?? -1;
        db.HostedEventGalleryImages.Add(new HostedEventGalleryImage
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, UploadFileId = upload.Id, SortOrder = next + 1,
            Caption = caption?.Trim() is { Length: > 0 } c ? (c.Length > 300 ? c[..300] : c) : null,
            DateCreated = now, CreatedByAppUserId = userId.Value,
        });
        await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, eventId, ct));
    }

    [HttpPut("{imageId:guid}")]
    public async Task<ActionResult<IReadOnlyList<HostedEventImageRecord>>> Update(
        Guid orgId, Guid eventId, Guid imageId, [FromBody] UpdateHostedEventImageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        // Scoped through the event's own group, not the route's, as the tour gallery learned to.
        var image = await db.HostedEventGalleryImages.FirstOrDefaultAsync(
            g => g.Id == imageId && g.HostedEventId == eventId && g.HostedEvent.OrganizationId == orgId, ct);
        if (image is null) return NotFound();

        image.Caption = request.Caption?.Trim() is { Length: > 0 } c ? (c.Length > 300 ? c[..300] : c) : null;
        if (request.SortOrder is int order) image.SortOrder = order;
        image.DateUpdated = DateTime.UtcNow;
        image.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, eventId, ct));
    }

    [HttpDelete("{imageId:guid}")]
    public async Task<ActionResult<IReadOnlyList<HostedEventImageRecord>>> Delete(Guid orgId, Guid eventId, Guid imageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var image = await db.HostedEventGalleryImages.Include(g => g.UploadFile).FirstOrDefaultAsync(
            g => g.Id == imageId && g.HostedEventId == eventId && g.HostedEvent.OrganizationId == orgId, ct);
        if (image is null) return NotFound();

        var path = image.UploadFile.StoragePath;
        db.HostedEventGalleryImages.Remove(image);
        await db.SaveChangesAsync(ct);

        if (await Services.Admin.UploadFileRows.TryDeleteAsync(db, image.UploadFileId, ct) && path is { Length: > 0 })
            await _ingest.DeleteAllAsync(path, ct);

        return Ok(await ListAsync(db, eventId, ct));
    }

    private static async Task<string?> FullAsync(BenDataContext db, Guid eventId, CancellationToken ct)
        => await db.HostedEventGalleryImages.CountAsync(g => g.HostedEventId == eventId, ct) >= HostedEventGalleryImage.MaxPerEvent
            ? $"This event's gallery is full at {HostedEventGalleryImage.MaxPerEvent} pictures. Take one off to make room for another."
            : null;

    private static async Task<IReadOnlyList<HostedEventImageRecord>> ListAsync(BenDataContext db, Guid eventId, CancellationToken ct)
        => await db.HostedEventGalleryImages.AsNoTracking()
            .Where(g => g.HostedEventId == eventId)
            .OrderBy(g => g.SortOrder)
            .Select(g => new HostedEventImageRecord(g.Id, g.UploadFileId, g.SortOrder, g.Caption))
            .ToListAsync(ct);
}
