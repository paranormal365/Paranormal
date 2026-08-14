using AutoMapper;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Applies a destructive audio edit (cut, silence, normalize, gain, fade, reverse) to an
/// existing UploadFile and saves the result as a new UploadFile — the source is never modified.
/// </summary>
[ApiController]
[Route("api/upload-files/{fileId:guid}/audio-edit")]
[Authorize]
public sealed class UploadFileAudioEditController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;

    public UploadFileAudioEditController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IFileStorageService fileStorage,
        IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileRecord>> Edit(
        Guid fileId,
        [FromBody] AudioEditRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if ((request.Operation is AudioEditOperation.Cut or AudioEditOperation.Silence) &&
            (request.Start is null || request.End is null || request.End <= request.Start))
            return BadRequest("Start and End are required for Cut/Silence, and End must be greater than Start.");

        if (request.Operation == AudioEditOperation.Gain && request.GainDb is null)
            return BadRequest("GainDb is required for Gain.");

        if (request.Operation == AudioEditOperation.Speed &&
            (request.SpeedRatio is null || request.SpeedRatio is <= 0 or > 4))
            return BadRequest("SpeedRatio is required for Speed and must be between 0 (exclusive) and 4.");

        if (request.Operation == AudioEditOperation.Pitch &&
            (request.PitchSemitones is null || request.PitchSemitones is < -24 or > 24))
            return BadRequest("PitchSemitones is required for Pitch and must be between -24 and 24.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var source = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (source is null) return NotFound("Source file not found.");

        if (!await db.UploadFileTypes.AnyAsync(t => t.Id == request.UploadFileTypeId, ct))
            return BadRequest("Upload file type not found.");

        byte[] editedBytes;
        string outContentType;
        string outExtension;
        try
        {
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

        var baseName = System.IO.Path.GetFileNameWithoutExtension(source.FileName);
        var newName  = string.IsNullOrWhiteSpace(request.Label)
            ? $"{baseName}_{request.Operation.ToString().ToLowerInvariant()}{outExtension}"
            : $"{request.Label}{outExtension}";

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
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        var relativePath = _fileStorage.UserFilePath(userId, entity.StoredFileName);
        using (var ms = new MemoryStream(editedBytes))
            await _fileStorage.WriteAsync(relativePath, ms, ct);
        entity.StoragePath = relativePath;

        db.UploadFiles.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), entity.Id, entity, userId, AppSources.WebApi, ct));

        return CreatedAtAction("GetById", "UploadFile", new { id = entity.Id },
            _mapper.Map<UploadFileRecord>(entity));
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
