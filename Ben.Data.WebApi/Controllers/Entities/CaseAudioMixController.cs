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
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimiting.AudioProcessingPolicy)]
    public async Task<ActionResult<CaseFileRecord>> Export(
        Guid orgId, Guid caseId, [FromBody] ExportAudioMixRequest request, CancellationToken ct)
    {
        // Offsets were unbounded: one track placed at 10,000,000 seconds sizes the mix buffer from
        // that offset, so a slider dragged into a text field became a multi-gigabyte allocation or
        // an int overflow, both of which arrive as a 500 (2026-09-06 audio walk, finding 3).
        if (AudioRequestLimits.MixProblem(request.Tracks) is { } problem) return BadRequest(problem);

        var userId = GetCurrentUserId();

        // Before any bytes are written. The mix's UploadFile row carries AppUserId, so an unknown
        // claim used to fail as a foreign-key violation AFTER the WAV had been rendered and stored
        // (finding 14).
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Create, ct)) return Forbid();
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

                // Legacy rows hold their bytes in the database and have no StoragePath at all; the
                // dereference was a 500 the moment one of them reached the mixer (finding 4). The
                // edit and clip endpoints have always had this fallback.
                Stream stream;
                if (!string.IsNullOrEmpty(uploadFile.StoragePath))
                    stream = await _fileStorage.OpenReadAsync(uploadFile.StoragePath, ct);
                else if (uploadFile.FileData is not null)
                    stream = new MemoryStream(uploadFile.FileData);
                else
                    return BadRequest($"'{uploadFile.FileName}' has no stored audio to mix.");

                openStreams.Add(stream);
                trackInputs.Add(new AudioMixer.TrackInput(stream, uploadFile.ContentType, track.OffsetSeconds, track.GainDb, track.Pan));
            }

            (mixedBytes, mixContentType, mixExtension) = AudioMixer.Mix(trackInputs);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (AudioSourceReader.IsUndecodable(ex))
        {
            return BadRequest($"Couldn't read one of those recordings: {ex.Message}");
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

            // Where this mix came from. Every other derived audio file records its parent; a mix
            // recorded none, so a case file that is plainly made of other case files looked like an
            // original upload (finding 15). Several tracks went in and one has to be named: the
            // first audible one, the same track whose capture details are carried below.
            ParentFileId = audible.Select(t => caseFiles[t.CaseFileId].UploadFileId).FirstOrDefault()
                is var parent && parent != Guid.Empty ? parent : null,

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

        var inherited = firstSourceFileId != Guid.Empty
            ? await _mediaIngest.DeriveMetadataAsync(db, firstSourceFileId, uploadFileEntity.Id, "Audio", ct)
            : null;

        // Length and format measured off the rendered mix, which is the one thing about it nobody
        // can inherit — and what the mixer needs to draw a clip at its real width (finding 11).
        if (DerivedAudioMetadata.For(uploadFileEntity.Id, mixedBytes, inherited) is { } metadata)
            db.UploadFileMetadata.Add(metadata);

        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = uploadFileEntity.Id,
            Description = $"Audio mix — {audible.Count} track(s)",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            try { await _fileStorage.DeleteAsync(storagePath, CancellationToken.None); } catch { /* report the insert's failure, not the cleanup's */ }
            throw;
        }

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

    /// <summary>Whether the caller may take <paramref name="action"/> on this group's cases.</summary>
    /// <remarks>
    /// The single endpoint here renders a mix and ATTACHES it to the case as a new file, so it
    /// needs the grant a case-file upload needs. It asked <c>Case.Read</c> under the name
    /// <c>IsOrgMember</c>, which is how a read-only member could add files to a case through the
    /// mixer after the front door was locked.
    /// </remarks>
    private Task<bool> MayAsync(Guid orgId, Ben.Data.Common.Enums.OrganizationSecurityAction action, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId, Ben.Data.Common.Enums.OrganizationPermissionArea.Cases, action, ct);
}
