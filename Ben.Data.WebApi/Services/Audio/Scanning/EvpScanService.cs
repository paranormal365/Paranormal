using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Audio.Scanning;

/// <summary>What one scan of one recording did.</summary>
/// <param name="Markers">The new Pending markers, and how many old ones they replaced.</param>
public sealed record EvpScanOutcome(EvpScanRecorder.Replaced Markers, Guid? ScanId, float? TopScore);

/// <summary>
/// Scans one recording: reads it a piece at a time, finds the candidates, proposes them, and keeps
/// the settings on the recording for whoever may change it (phase 6, 10/26/2026).
/// </summary>
/// <remarks>
/// The one way a recording is scanned. The editor's quick scan and every background job call this,
/// so they cannot disagree about what a scan does. Throws what the decoder throws
/// (<see cref="AudioDecoderUnavailableException"/>, <see cref="AudioDecodeFailedException"/>,
/// <see cref="NotSupportedException"/>); each caller says it in its own way.
/// </remarks>
public sealed class EvpScanService(
    IDbContextFactory<BenDataContext> dbContextFactory,
    IFileStorageService fileStorage,
    IAudioDecoder decoder,
    EvpScanRecorder recorder,
    ILogger<EvpScanService> logger)
{
    /// <returns>
    /// The outcome, or null and the reason when the candidates could not be saved (too many, say).
    /// </returns>
    public async Task<(EvpScanOutcome? Outcome, string? Problem)> RunAsync(
        UploadFile file, EvpSensitivity sensitivity, EvpDetectionOptions settings, Guid userId,
        bool isSuperAdmin, IProgress<EvpScanProgress>? progress, CancellationToken ct)
    {
        double? total = null;
        await using (var probe = await OpenAsync(file, ct))
        {
            try { total = await decoder.DurationSecondsAsync(probe, file.ContentType, file.FileName, ct); }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidDataException) { }
        }

        EvpAnalysis analysis;
        await using (var source = await OpenAsync(file, ct))
        {
            analysis = await EvpDetector.AnalyzeAsync(
                decoder.DecodeMono16kAsync(source, file.ContentType, file.FileName, ct),
                AudioSourceReader.DetectionSampleRate, settings, EvpScanRecorder.MaxCandidatesPerScan,
                total, progress, ct);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var (proposed, problem) = await recorder.ProposeAsync(db, file.Id, analysis, sensitivity, settings, userId, ct);
        if (proposed is null) return (null, problem);

        await TryRememberSettingsAsync(db, file, settings, userId, isSuperAdmin, ct);

        var top = proposed.Markers.Fresh.Select(m => m.DetectionScore).Max();
        return (new EvpScanOutcome(proposed.Markers, proposed.ScanId, top), null);
    }

    /// <summary>
    /// Keeps the settings this scan ran with on the recording, so the next scan of it starts from
    /// them (phase 5). Only for someone who may change the recording, the same people whose editor
    /// view is saved; anybody else's scan still runs, it just isn't remembered.
    /// </summary>
    /// <remarks>
    /// Never fails the scan the person asked for. A view save rewrites the rest of the config row,
    /// so this column is written here and nowhere else.
    /// </remarks>
    private async Task TryRememberSettingsAsync(
        BenDataContext db, UploadFile file, EvpDetectionOptions settings, Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        try
        {
            if (!await FileAudienceAccess.CanManageFileAsync(db, file, userId, isSuperAdmin, ct)) return;

            var json   = JsonSerializer.Serialize(settings, EvpScanRecorder.OptionsJson);
            var config = await db.UploadFileAudioConfigs.FirstOrDefaultAsync(c => c.UploadFileId == file.Id, ct);
            if (config is null)
            {
                db.UploadFileAudioConfigs.Add(new UploadFileAudioConfig
                {
                    Id = Guid.NewGuid(), UploadFileId = file.Id, EvpScanOptionsJson = json,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                });
            }
            else
            {
                config.EvpScanOptionsJson = json;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            logger.LogWarning(ex, "The scan settings for file {FileId} were not remembered", file.Id);
        }
    }

    private async Task<Stream> OpenAsync(UploadFile file, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(file.StoragePath))
            return await fileStorage.OpenReadAsync(file.StoragePath, ct);
        if (file.FileData is not null)
            return new MemoryStream(file.FileData);
        throw new InvalidOperationException($"File {file.Id} has no StoragePath and no FileData.");
    }
}
