using AutoMapper;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NAudio.Wave;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Clips audio from an existing UploadFile to a time range.
/// Supports WAV and MP3 source formats; always outputs WAV (PCM, lossless).
/// Use <c>GET preview</c> for an in-browser preview without saving;
/// <c>POST</c> persists the clip as a new UploadFile record on disk.
/// </summary>
/// <remarks>
/// Both actions previously had no check on the source file at all — any authenticated user could
/// clip audio out of someone else's private file, and <c>POST</c> persists that clip as a brand
/// new file the caller owns (permanent content exfiltration bypassing the source's visibility
/// entirely). Both now require <see cref="FileAudienceAccess.CanViewFileAsync"/> on the source.
/// </remarks>
[ApiController]
[Route("api/upload-files/{fileId:guid}/clip")]
[Authorize]
public sealed class UploadFileAudioClipController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;
    private readonly IMediaIngestService _mediaIngest;

    public UploadFileAudioClipController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IFileStorageService fileStorage,
        IAuditLogService auditLog,
        IMediaIngestService mediaIngest)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
        _mediaIngest = mediaIngest;
    }

    /// <summary>
    /// Returns clipped audio bytes for a time range without persisting to the database.
    /// Supports WAV and MP3 sources; always outputs WAV.
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> ClipPreview(
        Guid fileId,
        [FromQuery] double start,
        [FromQuery] double end,
        CancellationToken ct)
    {
        if (end <= start) return BadRequest("end must be greater than start.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var source = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (source is null) return NotFound();
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        try
        {
            Stream sourceStream = await OpenSourceStreamAsync(source, ct);
            await using (sourceStream)
            {
                var (bytes, contentType, _) = AudioClipper.Clip(sourceStream, source.ContentType, start, end);
                return File(bytes, contentType);
            }
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileRecord>> Clip(
        Guid fileId,
        [FromBody] ClipAudioRequest request,
        CancellationToken ct)
    {
        if (request.End <= request.Start)
            return BadRequest("End must be greater than Start.");

        var userId = GetCurrentUserIdOrThrow();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var source = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (source is null) return NotFound("Source file not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        if (!await db.UploadFileTypes.AnyAsync(t => t.Id == request.UploadFileTypeId, ct))
            return BadRequest("Upload file type not found.");

        // Resolved before any work: cutting a clip "from" a marker that isn't on this file would
        // produce a link that misrepresents where the evidence came from.
        AudioMarker? sourceMarker = null;
        if (request.SourceMarkerId is { } markerId)
        {
            sourceMarker = await db.AudioMarkers
                .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);
            if (sourceMarker is null)
                return BadRequest("That marker isn't on this file.");
        }

        byte[] clippedBytes;
        string outContentType;
        string outExtension;
        try
        {
            Stream sourceStream = await OpenSourceStreamAsync(source, ct);
            await using (sourceStream)
            {
                (clippedBytes, outContentType, outExtension) =
                    AudioClipper.Clip(sourceStream, source.ContentType, request.Start, request.End,
                                      request.Normalize);
            }
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }

        var baseName = System.IO.Path.GetFileNameWithoutExtension(source.FileName);
        var start    = TimeSpan.FromSeconds(request.Start);
        var end      = TimeSpan.FromSeconds(request.End);
        var newName  = $"{baseName}_clip_{start:mm\\mss\\s}-{end:mm\\mss\\s}{outExtension}";
        if (!string.IsNullOrWhiteSpace(request.Label))
            newName = $"{request.Label}{outExtension}";

        var entity = new UploadFile
        {
            Id                 = Guid.NewGuid(),
            UploadFileTypeId   = request.UploadFileTypeId,
            AppUserId          = userId,
            FileName           = newName,
            StoredFileName     = $"{Guid.NewGuid()}{outExtension}",
            ContentType        = outContentType,
            FileSize           = clippedBytes.Length,
            FileData           = null,  // written to disk below
            Description        = string.IsNullOrWhiteSpace(request.Label)
                ? $"Clip of '{source.FileName}' [{start.Minutes:D2}m{start.Seconds:D2}s-{end.Minutes:D2}m{end.Seconds:D2}s]"
                : request.Label,
            IsPublic           = request.IsPublic,
            SortOrder          = 0,
            ParentFileId       = fileId,
            RegionStart        = request.Start,
            RegionEnd          = request.End,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        // Write clip to disk before committing the DB record
        var relativePath = _fileStorage.UserFilePath(userId, entity.StoredFileName);
        using (var ms = new MemoryStream(clippedBytes))
            await _fileStorage.WriteAsync(relativePath, ms, ct);
        entity.StoragePath = relativePath;

        db.UploadFiles.Add(entity);

        // Ben's rule (2026-08-24): a clip keeps the recording's location. An encoder writes no
        // EXIF, so without carrying it forward the choice would be to lose where the audio was
        // captured or to imply the clip was measured there — the row says it was inherited, and
        // the duration comes from the clip's own bytes rather than the source's.
        if (await _mediaIngest.DeriveMetadataAsync(db, fileId, entity.Id, "Audio", ct) is { } derived)
        {
            derived.DurationSeconds = request.End - request.Start;
            db.UploadFileMetadata.Add(derived);
        }

        // Saved together with the file: a marker pointing at a clip row that failed to insert would
        // be a dangling reference the UI renders as a broken link.
        if (sourceMarker is not null)
        {
            sourceMarker.LinkedClipUploadFileId = entity.Id;
            sourceMarker.DateUpdated            = DateTime.UtcNow;
            sourceMarker.UpdatedByAppUserId     = userId;
        }

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), entity.Id, entity, userId, AppSources.WebApi));

        return CreatedAtAction("GetById", "UploadFile", new { id = entity.Id },
            _mapper.Map<UploadFileRecord>(entity));
    }

    /// <summary>
    /// Opens the source audio as a stream: from disk if StoragePath is set,
    /// otherwise falls back to FileData bytes (legacy rows awaiting migration).
    /// </summary>
    private async Task<Stream> OpenSourceStreamAsync(UploadFile file, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(file.StoragePath))
            return await _fileStorage.OpenReadAsync(file.StoragePath, ct);
        if (file.FileData is not null)
            return new MemoryStream(file.FileData);
        throw new InvalidOperationException($"File {file.Id} has no StoragePath and no FileData.");
    }
}

// ── Audio clipping helper ────────────────────────────────────────────────────

internal static class AudioClipper
{
    /// <summary>
    /// Clips <paramref name="sourceStream"/> to [<paramref name="startSeconds"/>, <paramref name="endSeconds"/>].
    /// Returns the clipped PCM bytes as WAV together with its content-type and file extension.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when the source format cannot be decoded by NAudio.</exception>
    /// <param name="sourceStream">The audio to clip from.</param>
    /// <param name="sourceContentType">Content type of the source, used to pick a decoder.</param>
    /// <param name="startSeconds">Clip start.</param>
    /// <param name="endSeconds">Clip end.</param>
    /// <param name="normalize">
    /// Scale the clip so its loudest peak sits just below full scale. Applied after the cut, so the
    /// gain is chosen from the clip's own peak rather than the whole recording's — which is the
    /// point, since the recording's peak is usually something far louder than the EVP.
    /// </param>
    public static (byte[] Bytes, string ContentType, string Extension) Clip(
        Stream sourceStream, string sourceContentType, double startSeconds, double endSeconds,
        bool normalize = false)
    {
        // See AudioSourceReader: the default NAudio MP3 reader is Windows-only.
        var waveStream = AudioSourceReader.Open(sourceStream, sourceContentType);

        using (waveStream)
        {
            var startOffset = TimeSpan.FromSeconds(startSeconds);
            var endOffset   = TimeSpan.FromSeconds(endSeconds);

            // Clamp to actual duration
            if (startOffset < TimeSpan.Zero) startOffset = TimeSpan.Zero;
            if (endOffset > waveStream.TotalTime) endOffset = waveStream.TotalTime;

            waveStream.CurrentTime = startOffset;

            using var outputStream = new MemoryStream();
            using var writer       = new WaveFileWriter(outputStream, waveStream.WaveFormat);

            var buffer = new byte[waveStream.WaveFormat.AverageBytesPerSecond];
            while (waveStream.CurrentTime < endOffset)
            {
                var remaining    = (endOffset - waveStream.CurrentTime).TotalSeconds;
                var maxBytes     = (int)(waveStream.WaveFormat.AverageBytesPerSecond * remaining);
                var toRead       = Math.Min(buffer.Length, maxBytes);
                if (toRead <= 0) break;

                var bytesRead = waveStream.Read(buffer, 0, toRead);
                if (bytesRead == 0) break;
                writer.Write(buffer, 0, bytesRead);
            }
            writer.Flush();
            var clipped = outputStream.ToArray();
            return (normalize ? NormalizePeak(clipped) : clipped, "audio/wav", ".wav");
        }
    }

    /// <summary>Peak level to normalize to: −1 dBFS, leaving headroom so playback can't clip.</summary>
    private const float NormalizeTargetPeak = 0.891f;

    /// <summary>
    /// Scales a WAV so its loudest sample sits at <see cref="NormalizeTargetPeak"/>. Silence is
    /// returned untouched — there is no peak to scale, and dividing by one would amplify nothing
    /// into noise.
    /// </summary>
    private static byte[] NormalizePeak(byte[] wavBytes)
    {
        using var input  = new MemoryStream(wavBytes);
        using var reader = new WaveFileReader(input);
        var provider = reader.ToSampleProvider();

        var samples = new List<float>();
        var buffer  = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            samples.AddRange(buffer.AsSpan(0, read).ToArray());

        var peak = 0f;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        if (peak <= 0.0001f) return wavBytes;

        var scale = NormalizeTargetPeak / peak;

        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(
            output, new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels)))
        {
            foreach (var s in samples)
                writer.WriteSample(Math.Clamp(s * scale, -1f, 1f));
            writer.Flush();
        }
        return output.ToArray();
    }
}
