using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The one place an uploaded media file is taken in: metadata off to its own table, original kept,
/// a sanitized copy and a thumbnail written beside it.
/// </summary>
/// <remarks>
/// Exists so the three photo-upload paths (personal equipment, group equipment, loan condition
/// photos) cannot drift apart on the policy. Any future upload surface — the media library, case
/// evidence — adopts the whole rule by calling this rather than by remembering four steps in the
/// right order.
/// </remarks>
public interface IMediaIngestService
{
    /// <summary>
    /// Stores <paramref name="file"/> at <paramref name="storagePath"/> and returns what the
    /// caller needs to finish building its <see cref="UploadFile"/> row.
    /// </summary>
    /// <exception cref="UnreadableImageException">
    /// The content type says image but the bytes will not decode.
    /// </exception>
    /// <param name="file">The uploaded file, as received.</param>
    /// <param name="storagePath">Where the original is to be written.</param>
    /// <param name="uploadFileId">The row this ingest belongs to; the metadata is keyed to it.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="stripAudioVideo">
    /// Whether this group's plan and settings say audio and video should have their embedded
    /// metadata removed (item 181). Images are stripped regardless. Defaults to false so that a
    /// caller with no organization — a personal upload — keeps today's behaviour.
    /// </param>
    Task<IngestedMedia> IngestAsync(
        IFormFile file, string storagePath, Guid uploadFileId, CancellationToken ct,
        bool stripAudioVideo = false);

    /// <summary>
    /// Picks which stored file a read should serve: the sanitized copy when one exists, otherwise
    /// the original. Serving falls back rather than failing, so a file ingested before this service
    /// existed still loads.
    /// </summary>
    string ServingPathFor(string storagePath);

    /// <summary>Deletes an original and any derivatives sitting beside it. Best-effort.</summary>
    Task DeleteAllAsync(string storagePath, CancellationToken ct);

    /// <summary>
    /// The metadata row for a DERIVED file — a clip, an edit, a mix, a copy — carrying the
    /// source's capture details forward (Ben's rule, 2026-08-24). Null when the source has no
    /// metadata row to carry.
    /// </summary>
    /// <remarks>
    /// Only the facts that stay true of a derivative travel: where and when the recording was
    /// made, and on what device. Duration, sample rate, channels and pixel dimensions belong to
    /// the NEW bytes — a thirty-second clip of a ten-minute recording is thirty seconds — so they
    /// are left for the caller to set from what it actually produced, and the raw dump is not
    /// copied because it describes a file this is not.
    /// </remarks>
    Task<UploadFileMetadata?> DeriveMetadataAsync(
        BenDataContext db, Guid sourceUploadFileId, Guid derivedUploadFileId,
        string mediaKind, CancellationToken ct);

    /// <summary>
    /// Opens the thumbnail, generating and storing it first if it is missing. Null when the file
    /// is not something we can shrink (video, audio, SVG) — the caller serves the real thing.
    /// </summary>
    Task<Stream?> OpenThumbnailAsync(string storagePath, CancellationToken ct);
}

/// <summary>What ingesting produced, for the caller to record.</summary>
/// <param name="Metadata">Row to add — carries GPS/EXIF, deliberately away from the served bytes.</param>
/// <param name="ServedFileSize">Size of what viewers actually download.</param>
/// <param name="ServedContentType">Content type of what viewers actually download.</param>
/// <param name="WasSanitized">False for types we cannot strip yet (video, audio, SVG).</param>
public sealed record IngestedMedia(
    UploadFileMetadata Metadata,
    long ServedFileSize,
    string ServedContentType,
    bool WasSanitized);

/// <inheritdoc />
public sealed class MediaIngestService(
    IFileStorageService fileStorage,
    FileMetadataExtractorService metadataExtractor,
    IMediaSanitizationService sanitizer,
    IAvMetadataStripper avStripper,
    ILogger<MediaIngestService> logger) : IMediaIngestService
{
    public async Task<IngestedMedia> IngestAsync(
        IFormFile file, string storagePath, Guid uploadFileId, CancellationToken ct,
        bool stripAudioVideo = false)
    {
        // Read once: the bytes are needed for extraction, for the original, and for sanitizing,
        // and re-reading an IFormFile stream after it has been consumed is a familiar trap.
        byte[] originalBytes;
        await using (var source = file.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, ct);
            originalBytes = buffer.ToArray();
        }

        // 1. Metadata comes off the ORIGINAL — after sanitizing there would be nothing left to
        //    read. READING IS UNCONDITIONAL AND UNGATED (Ben, 2026-08-24): every file of every
        //    kind gets a row, whatever the group's plan says. What a plan can withhold is the
        //    REMOVAL of that metadata from audio and video, because removal costs a remux —
        //    knowing where a recording was made is free, hiding it from the served copy is the
        //    part with a price. Images are stripped for everyone regardless.
        var metadata = metadataExtractor.Extract(uploadFileId, file.ContentType, originalBytes);

        // 2. The original is always kept, untouched. It is the evidence.
        await using (var original = new MemoryStream(originalBytes, writable: false))
            await fileStorage.WriteAsync(storagePath, original, ct);

        if (!sanitizer.CanSanitize(file.ContentType))
        {
            // Item 181: audio and video are stripped by remuxing through ffmpeg, when the group's
            // plan includes it, the group has left it on, and the host has the tool. The metadata
            // was already read off the ORIGINAL above, so the record survives the strip — which is
            // the whole design: the group keeps the facts, the served file does not carry them.
            if (stripAudioVideo && avStripper.CanStrip(file.ContentType)
                && await avStripper.StripAsync(originalBytes, file.FileName, ct) is { } stripped)
            {
                // The stripped copy sits beside the original under the sanitized name, so every
                // serve path finds it through ServingPathFor exactly as it finds a cleaned image.
                await using var clean = new MemoryStream(stripped, writable: false);
                await fileStorage.WriteAsync(sanitizer.StrippedPathFor(storagePath), clean, ct);
                return new IngestedMedia(metadata, stripped.LongLength, file.ContentType, WasSanitized: true);
            }

            // Not stripped: SVG, a group whose plan or settings say no, or a host with no tool.
            return new IngestedMedia(metadata, file.Length, file.ContentType, WasSanitized: false);
        }

        // 3. The sanitized copy — this is what every serve path returns.
        var sanitized = sanitizer.Sanitize(originalBytes);
        await using (var clean = new MemoryStream(sanitized, writable: false))
            await fileStorage.WriteAsync(sanitizer.SanitizedPathFor(storagePath), clean, ct);

        // 4. A thumbnail, so grids stop downloading full-resolution photos to draw them at 96px.
        try
        {
            var thumbnail = sanitizer.Sanitize(originalBytes, IMediaSanitizationService.ThumbnailLongEdge);
            await using var thumb = new MemoryStream(thumbnail, writable: false);
            await fileStorage.WriteAsync(sanitizer.ThumbnailPathFor(storagePath), thumb, ct);
        }
        catch (Exception ex)
        {
            // A missing thumbnail is a performance regression, not a failed upload — the endpoint
            // regenerates lazily. Never silently: this is exactly the class of failure that hides.
            logger.LogWarning(ex, "Thumbnail generation failed for {StoragePath}; it will be generated on demand.", storagePath);
        }

        return new IngestedMedia(metadata, sanitized.LongLength, "image/jpeg", WasSanitized: true);
    }

    public async Task<UploadFileMetadata?> DeriveMetadataAsync(
        BenDataContext db, Guid sourceUploadFileId, Guid derivedUploadFileId,
        string mediaKind, CancellationToken ct)
    {
        var source = await db.UploadFileMetadata.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UploadFileId == sourceUploadFileId, ct);
        if (source is null) return null;

        return new UploadFileMetadata
        {
            Id             = Guid.NewGuid(),
            UploadFileId   = derivedUploadFileId,
            MediaKind      = mediaKind,

            // Where and when it was recorded, and on what — all still true of a clip cut from it.
            CapturedAtUtc      = source.CapturedAtUtc,
            GpsLatitude        = source.GpsLatitude,
            GpsLongitude       = source.GpsLongitude,
            GpsAltitudeMeters  = source.GpsAltitudeMeters,
            CameraManufacturer = source.CameraManufacturer,
            CameraModel        = source.CameraModel,

            // Said plainly: these values were carried, not measured off these bytes.
            InheritedFromUploadFileId = sourceUploadFileId,
            ExtractedAtUtc            = DateTime.UtcNow,
        };
    }

    public string ServingPathFor(string storagePath)
    {
        // An image's cleaned copy is a JPEG under .clean.jpg; a stripped audio or video keeps its
        // own extension under .clean{ext}. Both are checked, so one call answers for every kind.
        var sanitized = sanitizer.SanitizedPathFor(storagePath);
        if (fileStorage.Exists(sanitized)) return sanitized;

        var stripped = sanitizer.StrippedPathFor(storagePath);
        if (stripped != sanitized && fileStorage.Exists(stripped)) return stripped;

        return storagePath;
    }

    public async Task<Stream?> OpenThumbnailAsync(string storagePath, CancellationToken ct)
    {
        var thumbnailPath = sanitizer.ThumbnailPathFor(storagePath);
        if (fileStorage.Exists(thumbnailPath))
            return await fileStorage.OpenReadAsync(thumbnailPath, ct);

        // Missing — either this file predates the pipeline, or its thumbnail failed at upload.
        // Generate from whatever we still have rather than making the caller care which.
        if (!fileStorage.Exists(storagePath)) return null;

        byte[] sourceBytes;
        await using (var source = await fileStorage.OpenReadAsync(storagePath, ct))
        await using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, ct);
            sourceBytes = buffer.ToArray();
        }

        byte[] thumbnail;
        try
        {
            thumbnail = sanitizer.Sanitize(sourceBytes, IMediaSanitizationService.ThumbnailLongEdge);
        }
        catch (UnreadableImageException)
        {
            return null;   // not an image — nothing to shrink
        }

        await using (var toStore = new MemoryStream(thumbnail, writable: false))
            await fileStorage.WriteAsync(thumbnailPath, toStore, ct);

        return new MemoryStream(thumbnail, writable: false);
    }

    public async Task DeleteAllAsync(string storagePath, CancellationToken ct)
    {
        foreach (var path in new[]
                 {
                     storagePath,
                     sanitizer.SanitizedPathFor(storagePath),
                     sanitizer.ThumbnailPathFor(storagePath),
                 })
        {
            try { await fileStorage.DeleteAsync(path, ct); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete {Path} while removing media.", path);
            }
        }
    }
}
