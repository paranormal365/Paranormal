using AutoMapper;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Applies a destructive audio edit (cut, silence, normalize, gain, fade, reverse) to an
/// existing UploadFile and saves the result as a new UploadFile — the source is never modified.
/// </summary>
/// <remarks>
/// Requires <see cref="FileAudienceAccess.CanViewFileAsync"/> on the source, for the same reason
/// <see cref="UploadFileAudioClipController"/> does: the edit result is persisted as a brand new
/// file the caller owns, so without the check any authenticated user could launder someone else's
/// private audio into their own library by "editing" it.
/// </remarks>
[ApiController]
[Route("api/upload-files/{fileId:guid}/audio-edit")]
[Authorize]
public sealed class UploadFileAudioEditController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;

    private readonly IMediaIngestService _mediaIngest;

    public UploadFileAudioEditController(
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

    [HttpPost]
    [EnableRateLimiting(RateLimiting.AudioProcessingPolicy)]
    public async Task<ActionResult<UploadFileRecord>> Edit(
        Guid fileId,
        [FromBody] AudioEditRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        // Every bound in one place, shared with the mixer — see AudioRequestLimits for what each
        // one is for and which of them a slider can reach on its own.
        if (AudioRequestLimits.EditProblem(request) is { } problem) return BadRequest(problem);

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var source = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (source is null) return NotFound("Source file not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        if (!await db.UploadFileTypes.AnyAsync(t => t.Id == request.UploadFileTypeId, ct))
            return BadRequest("Upload file type not found.");

        // The source's visibility is a ceiling, not a suggestion. Anyone who can SEE a private
        // recording can derive a copy from it — that is the point of the feature — and the derived
        // copy's IsPublic came straight from the request, so "edit it, then publish the edit" was a
        // way to publish somebody else's private audio (2026-09-06 audio walk, finding 6).
        if (request.IsPublic && !source.IsPublic)
            return BadRequest(
                "That recording is private, so an edit of it cannot be made public here. Ask "
                + "whoever owns it to publish the original first.");

        byte[] editedBytes;
        string outContentType;
        string outExtension;
        try
        {
            // Header only, so an impossible region costs nothing to refuse.
            TimeSpan sourceDuration;
            await using (var probeStream = await OpenSourceStreamAsync(source, ct))
                sourceDuration = AudioSourceReader.Probe(probeStream, source.ContentType).Duration;

            if (request.Operation is AudioEditOperation.Cut or AudioEditOperation.Silence
                && request.Start >= sourceDuration.TotalSeconds)
                return BadRequest(
                    $"That region starts at {request.Start:0.##}s, and the recording is only "
                    + $"{sourceDuration.TotalSeconds:0.##}s long.");

            Stream sourceStream = await OpenSourceStreamAsync(source, ct);
            await using (sourceStream)
            {
                (editedBytes, outContentType, outExtension) = request.Operation switch
                {
                    AudioEditOperation.Cut       => AudioEditor.CutRegion(sourceStream, source.ContentType, request.Start!.Value, request.End!.Value),
                    AudioEditOperation.Silence   => AudioEditor.SilenceRegion(sourceStream, source.ContentType, request.Start!.Value, request.End!.Value),
                    AudioEditOperation.Normalize => AudioEditor.Normalize(sourceStream, source.ContentType),
                    AudioEditOperation.Gain      => AudioEditor.Gain(sourceStream, source.ContentType, request.GainDb!.Value),
                    AudioEditOperation.Fade      => AudioEditor.Fade(sourceStream, source.ContentType, request.FadeInSeconds ?? 0, request.FadeOutSeconds ?? 0),
                    AudioEditOperation.Reverse   => AudioEditor.Reverse(sourceStream, source.ContentType),
                    AudioEditOperation.Speed     => AudioEditor.ChangeSpeed(sourceStream, source.ContentType, request.SpeedRatio!.Value),
                    AudioEditOperation.Pitch     => AudioEditor.PitchShift(sourceStream, source.ContentType, request.PitchSemitones!.Value),
                    _                            => throw new NotSupportedException($"Unknown operation: {request.Operation}"),
                };
            }
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (AudioSourceReader.IsUndecodable(ex))
        {
            // Same answer the EVP scan already gave for the same file: a 400 that says the bytes
            // could not be read, rather than a 500 that reads as "the site is broken" (finding 5).
            return BadRequest($"Couldn't read that audio: {ex.Message}");
        }

        var baseName = System.IO.Path.GetFileNameWithoutExtension(source.FileName);
        var newName  = string.IsNullOrWhiteSpace(request.Label)
            ? $"{baseName}_{request.Operation.ToString().ToLowerInvariant()}{outExtension}"
            : $"{request.Label}{outExtension}";

        var isRegionEdit = request.Operation is AudioEditOperation.Cut or AudioEditOperation.Silence;

        var entity = new UploadFile
        {
            Id                 = Guid.NewGuid(),
            UploadFileTypeId   = request.UploadFileTypeId,
            AppUserId          = userId,
            FileName           = newName,
            StoredFileName     = $"{Guid.NewGuid()}{outExtension}",
            ContentType        = outContentType,
            FileSize           = editedBytes.Length,
            FileData           = null,  // written to disk below
            Description        = string.IsNullOrWhiteSpace(request.Label)
                ? $"{request.Operation} of '{source.FileName}'"
                : request.Label,
            IsPublic           = request.IsPublic,
            SortOrder          = 0,
            ParentFileId       = fileId,

            // Which part of the recording this edit was about. Without it every edited file showed
            // "0:00–0:00" on its Saved Clips card, next to clips that showed their real range
            // (2026-09-06 audio walk, finding F). Only the region operations have one to state.
            RegionStart        = isRegionEdit ? request.Start : null,
            RegionEnd          = isRegionEdit ? request.End   : null,

            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        var relativePath = _fileStorage.UserFilePath(userId, entity.StoredFileName);
        using (var ms = new MemoryStream(editedBytes))
            await _fileStorage.WriteAsync(relativePath, ms, ct);
        entity.StoragePath = relativePath;

        db.UploadFiles.Add(entity);

        // An edited copy was recorded in the same place as its source (Ben's rule, 2026-08-24), and
        // now also carries how long it is and what shape it is — measured off the bytes just
        // produced rather than inherited, because no audio upload has a metadata row to inherit
        // from (finding 11).
        var inherited = await _mediaIngest.DeriveMetadataAsync(db, fileId, entity.Id, "Audio", ct);
        if (DerivedAudioMetadata.For(entity.Id, editedBytes, inherited) is { } metadata)
            db.UploadFileMetadata.Add(metadata);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // The bytes are on disk and the row is not, so nothing will ever point at them. Take
            // them back rather than leaving an orphan for a cleanup job to guess about (finding 7).
            await TryDeleteAsync(relativePath);
            throw;
        }

        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), entity.Id, entity, userId, AppSources.WebApi));

        return CreatedAtAction("GetById", "UploadFile", new { id = entity.Id },
            _mapper.Map<UploadFileRecord>(entity));
    }

    /// <summary>Removes a written file, never throwing over the failure that led here.</summary>
    private async Task TryDeleteAsync(string relativePath)
    {
        try { await _fileStorage.DeleteAsync(relativePath, CancellationToken.None); }
        catch { /* the insert already failed; a failed cleanup must not replace that error */ }
    }

    private async Task<Stream> OpenSourceStreamAsync(UploadFile file, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(file.StoragePath))
            return await _fileStorage.OpenReadAsync(file.StoragePath, ct);
        if (file.FileData is not null)
            return new MemoryStream(file.FileData);
        throw new InvalidOperationException($"File {file.Id} has no StoragePath and no FileData.");
    }
}
