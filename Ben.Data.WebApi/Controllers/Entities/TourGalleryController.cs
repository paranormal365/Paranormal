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
using AutoMapper;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The pictures on a tour's page, and the keep for a photograph somebody sent in (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "The tour can keep up to 50 1920x1080 72ppi images and they can
/// swap them out or tag ones from tours to keep as well, but 50 images per tour max."</para>
///
/// <para><b>Everything here becomes the business's own copy.</b> Whether the picture came off the
/// business's camera or out of a guest's submission, it is decoded, fitted inside 1920×1080 and
/// re-encoded, which also drops the EXIF — a photograph taken on a phone carries the co-ordinates
/// of where it was taken, and a tour page is not the place to publish somebody's location trail.
/// The guest's original is untouched and keeps its own clock.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/tours/{tourId:guid}/gallery")]
public sealed class TourGalleryController : OrgCmsControllerBase
{
    /// <summary>Same stored type as other organization media.</summary>
    private static readonly Guid GalleryFileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly IFileStorageService _storage;
    private readonly IMediaIngestService _ingest;
    private readonly IMediaSanitizationService _images;

    public TourGalleryController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        IFileStorageService storage, IMediaIngestService ingest, IMediaSanitizationService images)
        : base(dbFactory, mapper, security)
    { _storage = storage; _ingest = ingest; _images = images; }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TourImageRecord>>> GetAll(
        Guid orgId, Guid tourId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await Services.Access.FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId.Value, ct)
            && !User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin))
            return Forbid();

        return Ok(await LoadAsync(db, orgId, tourId, ct));
    }

    /// <summary>Adds one picture from the business's own camera.</summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<TourImageRecord>> Upload(
        Guid orgId, Guid tourId, IFormFile file, [FromForm] string? caption, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");
        if (!_images.CanSanitize(file.ContentType))
            return BadRequest("A gallery takes photographs — JPEG, PNG or similar.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (await FindTourAsync(db, orgId, tourId, ct) is not { } tour) return NotFound();
        if (await FullAsync(db, tourId, ct) is string full) return BadRequest(full);

        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        var record = await StoreAsync(db, tour, buffer.ToArray(), file.FileName, caption, null, userId.Value, ct);
        return record is null
            ? BadRequest("That picture could not be read.")
            : CreatedAtAction(nameof(GetAll), new { orgId, tourId }, record);
    }

    /// <summary>
    /// Keeps a photograph a guest submitted, by copying it into the tour's gallery.
    /// </summary>
    /// <remarks>
    /// This is the <b>keep</b> Ben's retention rule turns on: a guest's photograph goes away after
    /// its month unless the business says otherwise, and saying otherwise means putting it on the
    /// page. The guest's original is not moved, re-owned or given a new clock — the business gets
    /// a copy it is free to publish, which is the only claim it should have over somebody else's
    /// photograph.
    /// </remarks>
    [HttpPost("from-submission/{submissionId:guid}")]
    public async Task<ActionResult<TourImageRecord>> KeepSubmission(
        Guid orgId, Guid tourId, Guid submissionId, [FromQuery] string? caption, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (await FindTourAsync(db, orgId, tourId, ct) is not { } tour) return NotFound();
        if (await FullAsync(db, tourId, ct) is string full) return BadRequest(full);

        var submission = await db.EventEvidenceSubmissions.AsNoTracking()
            .Include(s => s.UploadFile)
            .Include(s => s.OrgCalendarEvent)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null || submission.OrgCalendarEvent.OrganizationId != orgId)
            return NotFound("That submission isn't one of yours.");

        if (await db.TourGalleryImages.AnyAsync(
                g => g.TourId == tourId && g.SourceEventEvidenceSubmissionId == submissionId, ct))
            return BadRequest("That picture is already on this tour's page.");

        if (submission.UploadFile.StoragePath is not { Length: > 0 } path || !_storage.Exists(path))
            return BadRequest("That picture is no longer stored.");

        using var source = await _storage.OpenReadAsync(path, ct);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);

        var record = await StoreAsync(db, tour, buffer.ToArray(),
            submission.UploadFile.FileName, caption ?? submission.Note, submissionId, userId.Value, ct);

        return record is null ? BadRequest("That picture could not be read.") : Ok(record);
    }

    [HttpPut("{imageId:guid}")]
    public async Task<ActionResult<IReadOnlyList<TourImageRecord>>> Update(
        Guid orgId, Guid tourId, Guid imageId, [FromBody] UpdateTourImageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var image = await db.TourGalleryImages
            .FirstOrDefaultAsync(g => g.Id == imageId && g.TourId == tourId, ct);
        if (image is null) return NotFound();

        image.Caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();
        if (request.SortOrder is { } order) image.SortOrder = order;
        image.DateUpdated = DateTime.UtcNow;
        image.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok(await LoadAsync(db, orgId, tourId, ct));
    }

    /// <summary>
    /// Takes a picture off the page and deletes the business's copy.
    /// </summary>
    /// <remarks>
    /// The copy goes because it exists only to be shown here; a guest's original, when there was
    /// one, is a different file and is untouched.
    /// </remarks>
    [HttpDelete("{imageId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid tourId, Guid imageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var image = await db.TourGalleryImages.Include(g => g.UploadFile)
            .FirstOrDefaultAsync(g => g.Id == imageId && g.TourId == tourId, ct);
        if (image is null) return NotFound();

        var path = image.UploadFile.StoragePath;
        var fileId = image.UploadFileId;
        db.TourGalleryImages.Remove(image);
        await db.SaveChangesAsync(ct);

        await Services.Admin.UploadFileRows.TryDeleteAsync(db, fileId, ct);
        if (!string.IsNullOrWhiteSpace(path)) await _ingest.DeleteAllAsync(path, ct);

        return NoContent();
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static Task<Tour?> FindTourAsync(BenDataContext db, Guid orgId, Guid tourId, CancellationToken ct)
        => db.Tours.FirstOrDefaultAsync(t => t.Id == tourId && t.OrganizationId == orgId, ct);

    private static async Task<string?> FullAsync(BenDataContext db, Guid tourId, CancellationToken ct)
        => await db.TourGalleryImages.CountAsync(g => g.TourId == tourId, ct) >= TourGalleryImage.MaxPerTour
            ? $"This tour's gallery is full at {TourGalleryImage.MaxPerTour} pictures. Take one off "
              + "to make room for another."
            : null;

    /// <summary>
    /// Fits the picture inside the box, strips what it was carrying, and files it under the tour.
    /// </summary>
    private async Task<TourImageRecord?> StoreAsync(
        BenDataContext db, Tour tour, byte[] original, string fileName,
        string? caption, Guid? submissionId, Guid userId, CancellationToken ct)
    {
        byte[] fitted;
        int fittedWidth, fittedHeight;
        try
        {
            fitted = FitInsideBox(original);
            var bounds = SkiaSharp.SKBitmap.DecodeBounds(fitted);
            fittedWidth = bounds.Width;
            fittedHeight = bounds.Height;
        }
        catch (UnreadableImageException) { return null; }

        var storedName = $"{Guid.NewGuid()}.jpg";
        var storagePath = _storage.OrgFilePath(tour.OrganizationId, $"tour-gallery/{storedName}");
        await _storage.WriteAsync(storagePath, new MemoryStream(fitted), ct);

        var uploadFile = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = GalleryFileTypeId,
            // Owned by the business, not by whoever pressed the button: a gallery picture outlives
            // the member who added it, and item 180's reassignment rules read this column.
            OwnerOrganizationId = tour.OrganizationId,
            FileName = Path.ChangeExtension(Path.GetFileName(fileName), ".jpg"),
            StoredFileName = storedName, ContentType = "image/jpeg", FileSize = fitted.LongLength,
            StoragePath = storagePath, IsPublic = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);

        // Every upload records what its bytes are (Ben's rule, 2026-08-24). This one records the
        // truth about the COPY rather than carrying the original's row across: the copy was
        // decoded and re-encoded, so it holds no EXIF, no camera and — the point — no
        // co-ordinates. Publishing a guest's photograph must not publish where they were
        // standing, and a metadata row copied from the source would say it did.
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(),
            UploadFileId = uploadFile.Id,
            MediaKind = "Image",
            WidthPixels = fittedWidth,
            HeightPixels = fittedHeight,
            ExtractedAtUtc = DateTime.UtcNow,
        });

        var nextOrder = await db.TourGalleryImages.Where(g => g.TourId == tour.Id)
            .Select(g => (int?)g.SortOrder).MaxAsync(ct) ?? -1;

        var image = new TourGalleryImage
        {
            Id = Guid.NewGuid(), TourId = tour.Id, UploadFileId = uploadFile.Id,
            SortOrder = nextOrder + 1,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            SourceEventEvidenceSubmissionId = submissionId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.TourGalleryImages.Add(image);
        await db.SaveChangesAsync(ct);

        return new TourImageRecord(image.Id, uploadFile.Id, image.SortOrder, image.Caption,
            submissionId is not null);
    }

    /// <summary>
    /// Scales a picture down until it fits inside 1920×1080, and never scales one up.
    /// </summary>
    /// <remarks>
    /// The sanitizer takes a single long edge, which is the right answer for a square box and the
    /// wrong one for this: a tall photograph capped at 1920 on its long edge is still 1920 tall.
    /// The scale is worked out here from both edges and handed over as the long edge that results,
    /// so a landscape shot lands at 1920×1080 and a portrait one at 607×1080.
    /// </remarks>
    private byte[] FitInsideBox(byte[] original)
    {
        var info = SkiaSharp.SKBitmap.DecodeBounds(original);
        if (info.Width <= 0 || info.Height <= 0)
            throw new UnreadableImageException("The file could not be read as an image.");

        var scale = Math.Min(
            Math.Min((double)TourGalleryImage.MaxWidth / info.Width,
                     (double)TourGalleryImage.MaxHeight / info.Height),
            1.0);

        var longEdge = Math.Max(1, (int)Math.Round(Math.Max(info.Width, info.Height) * scale));
        return _images.Sanitize(original, longEdge);
    }

    private static async Task<List<TourImageRecord>> LoadAsync(
        BenDataContext db, Guid orgId, Guid tourId, CancellationToken ct)
        => await db.TourGalleryImages.AsNoTracking()
            .Where(g => g.TourId == tourId && g.Tour.OrganizationId == orgId)
            .OrderBy(g => g.SortOrder)
            .Select(g => new TourImageRecord(g.Id, g.UploadFileId, g.SortOrder, g.Caption,
                g.SourceEventEvidenceSubmissionId != null))
            .ToListAsync(ct);
}
