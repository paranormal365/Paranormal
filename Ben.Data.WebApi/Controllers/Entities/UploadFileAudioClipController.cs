using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NAudio.Wave;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Clips an existing UploadFile's audio to a time range and persists the result as a new UploadFile.
/// Supported input formats: WAV, MP3. Output is always WAV (PCM, lossless clip).
/// </summary>
[ApiController]
[Route("api/upload-files/{fileId:guid}/clip")]
[Authorize]
public sealed class UploadFileAudioClipController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;

    public UploadFileAudioClipController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileRecord>> Clip(
        Guid fileId,
        [FromBody] ClipAudioRequest request,
        CancellationToken ct)
    {
        if (request.End <= request.Start)
            return BadRequest("End must be greater than Start.");

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var source = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (source is null) return NotFound("Source file not found.");

        if (!await db.UploadFileTypes.AnyAsync(t => t.Id == request.UploadFileTypeId, ct))
            return BadRequest("Upload file type not found.");

        byte[] clippedBytes;
        string outContentType;
        string outExtension;
        try
        {
            (clippedBytes, outContentType, outExtension) =
                AudioClipper.Clip(source.FileData, source.ContentType, request.Start, request.End);
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
            FileData           = clippedBytes,
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
        db.UploadFiles.Add(entity);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction("GetById", "UploadFile", new { id = entity.Id },
            _mapper.Map<UploadFileRecord>(entity));
    }

    private Guid GetCurrentUserId()
    {
        var appUserIdClaim = User.FindFirst("app_user_id")?.Value;
        if (Guid.TryParse(appUserIdClaim, out var id)) return id;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var subId) ? subId : Guid.Empty;
    }
}

// ── Audio clipping helper ────────────────────────────────────────────────────

internal static class AudioClipper
{
    /// <summary>
    /// Clips <paramref name="sourceBytes"/> to [<paramref name="startSeconds"/>, <paramref name="endSeconds"/>].
    /// Returns the clipped PCM bytes as WAV together with its content-type and file extension.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when the source format cannot be decoded by NAudio.</exception>
    public static (byte[] Bytes, string ContentType, string Extension) Clip(
        byte[] sourceBytes, string sourceContentType, double startSeconds, double endSeconds)
    {
        using var inputStream = new MemoryStream(sourceBytes);

        WaveStream waveStream;
        if (sourceContentType.Contains("wav", StringComparison.OrdinalIgnoreCase))
        {
            waveStream = new WaveFileReader(inputStream);
        }
        else if (sourceContentType.Contains("mp3", StringComparison.OrdinalIgnoreCase) ||
                 sourceContentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
        {
            waveStream = new Mp3FileReader(inputStream);
        }
        else
        {
            throw new NotSupportedException(
                $"Audio clipping is supported for WAV and MP3. " +
                $"Received content-type: '{sourceContentType}'. " +
                "For other formats, use the Download endpoint and clip locally.");
        }

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
            return (outputStream.ToArray(), "audio/wav", ".wav");
        }
    }
}
