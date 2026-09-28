using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services.FieldSessions;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Recounts the marks on field sessions stored before photographs, videos and sound stopped being
/// counted as marks (2026-09-27).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> <see cref="DeviceDataSummary"/> counted every reading carrying a marker label,
/// and the phone notes each capture with one — so a session with three marks, three photographs, a
/// video and its sound was stored as "8 marked", on the website, the public archive, the phone's
/// server list and a client's report. The door counts correctly now; this puts the rows written
/// before that right, from the one copy that is definitely what the device wrote: its document.</para>
///
/// <para><b>Idempotent with no marker, like the other backfills.</b> Each document is re-read and a
/// row is written only where the count differs, so a second run changes nothing. A session whose
/// document cannot be read is left exactly as it is and said once in the log — a guessed number is
/// worse than a stale one.</para>
///
/// <para><b>Timestamps are not touched.</b> Nobody changed these sessions.</para>
/// </remarks>
public sealed class FieldSessionMarkerRecountService : BackgroundService
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IFileStorageService _storage;
    private readonly IBenBundleStore _bundles;
    private readonly ILogger<FieldSessionMarkerRecountService> _logger;

    public FieldSessionMarkerRecountService(IDbContextFactory<BenDataContext> dbFactory,
                                            IFileStorageService storage, IBenBundleStore bundles,
                                            ILogger<FieldSessionMarkerRecountService> logger)
    {
        _dbFactory = dbFactory;
        _storage = storage;
        _bundles = bundles;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var changed = await RecountAsync(stoppingToken);
            if (changed > 0)
                _logger.LogInformation("Recounted the marks on {Changed} field session(s).", changed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The field-session mark recount did not finish; it runs again at the next start.");
        }
    }

    /// <summary>Recounts every session that has any marks recorded; returns how many rows changed.</summary>
    public async Task<int> RecountAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Only sessions with a count to be wrong about: one with none cannot have counted a capture.
        var sessions = await db.FieldSessionUploads
            .Where(s => s.MarkerCount > 0)
            .Select(s => new
            {
                s.Id, s.IsBundle, s.MarkerCount,
                s.DocumentUploadFile.StoragePath, s.DocumentUploadFile.FileData,
            })
            .ToListAsync(ct);

        var changed = 0;
        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            string? json;
            try
            {
                json = await ReadDocumentAsync(session.IsBundle, session.StoragePath, session.FileData, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not read the document for field session {Id}; its mark count is left as it was.",
                                   session.Id);
                continue;
            }
            if (json is null) continue;

            int recounted;
            try { recounted = DeviceDataSummary.Read(json).MarkerCount; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Field session {Id}'s document could not be read as one; its mark count is left as it was.",
                                   session.Id);
                continue;
            }
            if (recounted == session.MarkerCount) continue;

            await db.FieldSessionUploads.Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.MarkerCount, recounted), ct);
            changed++;
        }
        return changed;
    }

    private async Task<string?> ReadDocumentAsync(bool isBundle, string? storagePath, byte[]? fileData,
                                                  CancellationToken ct)
    {
        if (isBundle)
        {
            if (storagePath is not { Length: > 0 } || !_storage.Exists(storagePath)) return null;
            await using var entry = await _bundles.OpenEntryAsync(storagePath, BenBundle.DocumentEntryPath, ct);
            if (entry is null) return null;
            using var reader = new StreamReader(entry);
            return await reader.ReadToEndAsync(ct);
        }
        if (storagePath is { Length: > 0 } && _storage.Exists(storagePath))
        {
            await using var stream = await _storage.OpenReadAsync(storagePath, ct);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        return fileData is { Length: > 0 } ? System.Text.Encoding.UTF8.GetString(fileData) : null;
    }
}
