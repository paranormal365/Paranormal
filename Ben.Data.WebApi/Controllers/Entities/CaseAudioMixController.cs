using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Renders a Phase E multi-track mix down to a single WAV, server-side via NAudio
/// (same architecture as <see cref="AudioEditor"/> — no client-side decode/encode pipeline exists),
/// and saves it as a new case file. The in-progress mix itself is never persisted — only the export.
/// </summary>
[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/audio-mix")]
[Authorize]
public sealed class CaseAudioMixController : BenControllerBase
{
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;

    private readonly IMediaIngestService _mediaIngest;

    public CaseAudioMixController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage,
        IMediaIngestService mediaIngest,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    {
        _db = db;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
     _security = security; }

    [HttpPost("export")]
    public async Task<ActionResult<CaseFileRecord>> Export(
        Guid orgId, Guid caseId, [FromBody] ExportAudioMixRequest request, CancellationToken ct)
    {
        if (request.Tracks.Count == 0) return BadRequest("At least one track is required.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct)) return NotFound();

        var caseFileIds = request.Tracks.Select(t => t.CaseFileId).ToHashSet();
        var caseFiles = await db.CaseFiles.AsNoTracking()
            .Include(f => f.UploadFile)
            .Where(f => f.CaseId == caseId && caseFileIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        if (caseFiles.Count != caseFileIds.Count)
            return BadRequest("One or more selected clips could not be found on this case.");
        if (caseFiles.Values.Any(f => !f.UploadFile.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
            return BadRequest("Only audio files can be placed in the mixer.");

        var anySolo = request.Tracks.Any(t => t.Solo);
        var audible = request.Tracks.Where(t => !t.Muted && (!anySolo || t.Solo)).ToList();
        if (audible.Count == 0) return BadRequest("At least one track must be audible (not muted, and soloed if any track is soloed).");

        var openStreams = new List<Stream>();
        byte[] mixedBytes;
        string mixContentType;
        string mixExtension;
        try
        {
            var trackInputs = new List<AudioMixer.TrackInput>();
            foreach (var track in audible)
            {
                var uploadFile = caseFiles[track.CaseFileId].UploadFile;
                var stream = await _fileStorage.OpenReadAsync(uploadFile.StoragePath!, ct);
                openStreams.Add(stream);
                trackInputs.Add(new AudioMixer.TrackInput(stream, uploadFile.ContentType, track.OffsetSeconds, track.GainDb, track.Pan));
            }

            (mixedBytes, mixContentType, mixExtension) = AudioMixer.Mix(trackInputs);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
        finally
        {
            foreach (var s in openStreams) await s.DisposeAsync();
        }

        var storedName  = $"{Guid.NewGuid()}{mixExtension}";
        var storagePath = _fileStorage.CaseFilePath(caseId, $"files/{storedName}");
        using (var ws = new MemoryStream(mixedBytes))
            await _fileStorage.WriteAsync(storagePath, ws, ct);

        var uploadFileEntity = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = UploadFileTypeSeeder.AudioMixFileTypeId, AppUserId = userId,
            FileName = $"Mix_{DateTime.UtcNow:yyyyMMdd_HHmmss}{mixExtension}", StoredFileName = storedName,
            ContentType = mixContentType, FileSize = mixedBytes.Length,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFileEntity);

        // A mix is a derivative of the tracks that went into it, so it keeps where they were
        // recorded (Ben's rule, 2026-08-24). Sources from ONE case are almost always the same
        // night at the same place; where they disagree the first audible track's provenance is
        // the honest choice — it is a real source of these bytes, and the row says it was carried.
        var firstSourceFileId = audible
            .Select(t => caseFiles[t.CaseFileId].UploadFileId)
            .FirstOrDefault();
        if (firstSourceFileId != Guid.Empty
            && await _mediaIngest.DeriveMetadataAsync(db, firstSourceFileId, uploadFileEntity.Id, "Audio", ct) is { } derived)
        {
            db.UploadFileMetadata.Add(derived);
        }

        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = uploadFileEntity.Id,
            Description = $"Audio mix — {audible.Count} track(s)",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);

        await db.SaveChangesAsync(ct);
        caseFile.UploadFile = uploadFileEntity;

        return Ok(new CaseFileRecord
        {
            Id = caseFile.Id,
            CaseId = caseFile.CaseId,
            UploadFileId = caseFile.UploadFileId,
            FileName = uploadFileEntity.FileName,
            ContentType = uploadFileEntity.ContentType,
            FileSize = uploadFileEntity.FileSize,
            Description = caseFile.Description,
            DateCreated = caseFile.DateCreated,
            CreatedByAppUserId = caseFile.CreatedByAppUserId,
        });
    }

    // Item 156 Phase D: bare membership stopped being the rule here — see CaseFileController.
    private async Task<bool> IsOrgMember(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(userId, orgId,
               Ben.Data.Common.Enums.OrganizationSecurityTable.Case,
               Ben.Data.Common.Enums.OrganizationSecurityAction.Read, ct);
}
