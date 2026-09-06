using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Manages per-file WaveSurfer player configuration.
/// One config row per UploadFile (one-to-one). Absent row = component defaults.
/// Endpoints: GET, PUT (upsert), DELETE.
/// </summary>
/// <remarks>
/// <para><b>Reading and writing are different permissions here.</b> The config is the owner's
/// saved view of their own recording — zoom, colours, spectrogram, the listening chain. Writing it
/// asked only whether the caller could <i>view</i> the file, so anyone the recording had been
/// shared with could overwrite or delete those settings, and the owner would simply find them
/// changed. Reading had no per-file check at all (2026-09-06 audio walk, finding 9).</para>
///
/// <para>PUT and DELETE now need <see cref="FileAudienceAccess.CanManageFileAsync"/> — the same
/// grant that renames or deletes the file — and GET needs
/// <see cref="FileAudienceAccess.CanViewFileAsync"/>.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/upload-files/{fileId:guid}/audio-config")]
public class UploadFileAudioConfigController(BenDataContext db, IMapper mapper, IAuditLogService auditLog) : BenControllerBase
{
    // ── GET /api/upload-files/{fileId}/audio-config ───────────────────────────

    /// <summary>Returns the audio config for an UploadFile, or null if none has been saved.</summary>
    [HttpGet]
    public async Task<ActionResult<UploadFileAudioConfigRecord?>> Get(Guid fileId)
    {
        if (!await FileExistsAsync(fileId)) return NotFound();

        var viewerId = GetCurrentUserIdOrThrow();
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, viewerId, CancellationToken.None)) return Forbid();

        var entity = await db.UploadFileAudioConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UploadFileId == fileId);

        return entity is null ? Ok((UploadFileAudioConfigRecord?)null) : Ok(mapper.Map<UploadFileAudioConfigRecord>(entity));
    }

    // ── PUT /api/upload-files/{fileId}/audio-config ───────────────────────────

    /// <summary>Creates or fully replaces the audio config for an UploadFile.</summary>
    [HttpPut]
    public async Task<ActionResult<UploadFileAudioConfigRecord>> Upsert(Guid fileId, [FromBody] UpsertAudioConfigRequest request)
    {
        if (!await FileExistsAsync(fileId)) return NotFound();

        var userId = GetCurrentUserIdOrThrow();
        if (!await CanManageAsync(fileId, userId)) return Forbid();

        var existingBefore = await db.UploadFileAudioConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UploadFileId == fileId);

        var existing = await db.UploadFileAudioConfigs
            .FirstOrDefaultAsync(c => c.UploadFileId == fileId);

        if (existing is null)
        {
            existing = new UploadFileAudioConfig
            {
                Id                  = Guid.NewGuid(),
                UploadFileId        = fileId,
                DateCreated         = DateTime.UtcNow,
                CreatedByAppUserId  = userId,
            };
            db.UploadFileAudioConfigs.Add(existing);
        }
        else
        {
            existing.DateUpdated        = DateTime.UtcNow;
            existing.UpdatedByAppUserId = userId;
        }

        Apply(request, existing);
        await db.SaveChangesAsync();

        if (existingBefore is null)
            _ = TryAuditAsync(auditLog.LogCreateAsync(nameof(UploadFileAudioConfig), existing.Id, existing, userId, AppSources.WebApi));
        else
            _ = TryAuditAsync(auditLog.LogUpdateAsync(nameof(UploadFileAudioConfig), existing.Id, existingBefore, existing, userId, AppSources.WebApi));

        return Ok(mapper.Map<UploadFileAudioConfigRecord>(existing));
    }

    // ── DELETE /api/upload-files/{fileId}/audio-config ────────────────────────

    /// <summary>Removes the audio config for an UploadFile (component will use defaults).</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(Guid fileId)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (!await FileExistsAsync(fileId)) return NotFound();
        if (!await CanManageAsync(fileId, userId)) return Forbid();

        var entity = await db.UploadFileAudioConfigs
            .FirstOrDefaultAsync(c => c.UploadFileId == fileId);

        if (entity is null) return NoContent();

        db.UploadFileAudioConfigs.Remove(entity);
        await db.SaveChangesAsync();
        _ = TryAuditAsync(auditLog.LogDeleteAsync(nameof(UploadFileAudioConfig), entity.Id, entity, userId, AppSources.WebApi));

        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> FileExistsAsync(Guid fileId)
        => await db.UploadFiles.AnyAsync(f => f.Id == fileId);

    /// <summary>Whether the caller may change this file — and so the settings that belong to it.</summary>
    private async Task<bool> CanManageAsync(Guid fileId, Guid userId)
    {
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId);
        if (file is null) return false;

        return await FileAudienceAccess.CanManageFileAsync(
            db, file, userId, User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin),
            CancellationToken.None);
    }

    private static void Apply(UpsertAudioConfigRequest req, UploadFileAudioConfig entity)
    {
        entity.WaveColor                    = req.WaveColor;
        entity.ProgressColor                = req.ProgressColor;
        entity.CursorColor                  = req.CursorColor;
        entity.CursorWidth                  = req.CursorWidth;
        entity.Height                       = req.Height;
        entity.BarWidth                     = req.BarWidth;
        entity.BarGap                       = req.BarGap;
        entity.BarRadius                    = req.BarRadius;
        entity.BarHeight                    = req.BarHeight;
        entity.BarAlign                     = req.BarAlign;
        entity.Normalize                    = req.Normalize;
        entity.DragToSeek                   = req.DragToSeek;
        entity.HideScrollbar                = req.HideScrollbar;
        entity.AudioRate                    = req.AudioRate;
        entity.EnableHover                  = req.EnableHover;
        entity.EnableTimeline               = req.EnableTimeline;
        entity.EnableZoom                   = req.EnableZoom;
        entity.EnableMinimap                = req.EnableMinimap;
        entity.EnableSpectrogram            = req.EnableSpectrogram;
        entity.EnableSpectrogramWindowed    = req.EnableSpectrogramWindowed;
        entity.EnableEnvelope               = req.EnableEnvelope;
        entity.EnableRegions                = req.EnableRegions;
        entity.HoverOptionsJson             = req.HoverOptionsJson;
        entity.TimelineOptionsJson          = req.TimelineOptionsJson;
        entity.ZoomOptionsJson              = req.ZoomOptionsJson;
        entity.MinimapOptionsJson           = req.MinimapOptionsJson;
        entity.SpectrogramOptionsJson       = req.SpectrogramOptionsJson;
        entity.SpectrogramWindowedOptionsJson = req.SpectrogramWindowedOptionsJson;
        entity.EnvelopeOptionsJson          = req.EnvelopeOptionsJson;
        entity.InitialHeight                = req.InitialHeight;
        entity.MinHeight                    = req.MinHeight;
        entity.MaxHeight                    = req.MaxHeight;
        entity.ShowControls                 = req.ShowControls;
        entity.MinZoom                      = req.MinZoom;
        entity.MaxZoom                      = req.MaxZoom;
    }
}
