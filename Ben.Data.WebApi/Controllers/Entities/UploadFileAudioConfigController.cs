using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Manages per-file WaveSurfer player configuration.
/// One config row per UploadFile (one-to-one). Absent row = component defaults.
/// Endpoints: GET, PUT (upsert), DELETE.
/// </summary>
[ApiController]
[Authorize]
[Route("api/upload-files/{fileId:guid}/audio-config")]
public class UploadFileAudioConfigController(BenDataContext db, IMapper mapper) : BenControllerBase
{
    // ── GET /api/upload-files/{fileId}/audio-config ───────────────────────────

    /// <summary>Returns the audio config for an UploadFile, or null if none has been saved.</summary>
    [HttpGet]
    public async Task<ActionResult<UploadFileAudioConfigRecord?>> Get(Guid fileId)
    {
        if (!await FileExistsAsync(fileId)) return NotFound();

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

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

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

        return Ok(mapper.Map<UploadFileAudioConfigRecord>(existing));
    }

    // ── DELETE /api/upload-files/{fileId}/audio-config ────────────────────────

    /// <summary>Removes the audio config for an UploadFile (component will use defaults).</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(Guid fileId)
    {
        var entity = await db.UploadFileAudioConfigs
            .FirstOrDefaultAsync(c => c.UploadFileId == fileId);

        if (entity is null) return NoContent();

        db.UploadFileAudioConfigs.Remove(entity);
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> FileExistsAsync(Guid fileId)
        => await db.UploadFiles.AnyAsync(f => f.Id == fileId);

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
