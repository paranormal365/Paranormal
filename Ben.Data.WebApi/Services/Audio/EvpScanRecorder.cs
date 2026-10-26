using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// Turns what a scan found into a file's pending candidates, and keeps the scan's record.
/// </summary>
/// <remarks>
/// <para>Lifted out of <c>AudioMarkerController</c> unchanged so that every way of scanning — the
/// editor's button today, a background job and a field session later — proposes candidates by the
/// same rules: only Pending markers are replaced, nothing already ruled on is proposed again, and
/// the delete and insert happen in one transaction.</para>
///
/// <para>The scan's learning record (item 253) is written after the candidates, in its own save,
/// and allowed to fail: the person asked for a scan, not for a training record.</para>
/// </remarks>
public sealed class EvpScanRecorder
{
    /// <summary>
    /// Ceiling on one scan's proposals. A detector set too sensitive on a noisy recording can
    /// produce thousands of runs, and nobody reviews a queue that long — better to reject the scan
    /// than to bury the file's real markers under it.
    /// </summary>
    public const int MaxCandidatesPerScan = 500;

    // How much a proposal must overlap an already-reviewed span to count as the same event is
    // EvpDetectionOptions.ReviewedOverlapFraction since phase 5. Its default, half, is deliberately
    // lenient: the detector's bounds shift slightly with sensitivity, and re-proposing something
    // the reviewer already dismissed is worse than missing a near-miss.

    /// <summary>How a scan's settings are kept: the same shape the editor sends them in.</summary>
    public static readonly JsonSerializerOptions OptionsJson = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly ILogger<EvpScanRecorder> _logger;

    public EvpScanRecorder(IDbContextFactory<BenDataContext> dbContextFactory, ILogger<EvpScanRecorder> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    /// <summary>What a replace did: the new pending markers, and how many old ones they replaced.</summary>
    public sealed record Replaced(IReadOnlyList<AudioMarker> Fresh, int ReplacedCount);

    /// <summary>What a scan proposed, and the id of its record when that was written.</summary>
    public sealed record Proposed(Replaced Markers, Guid? ScanId);

    /// <summary>
    /// Why these candidates cannot be saved, or null when they can.
    /// </summary>
    public static string? CandidateProblem(IReadOnlyCollection<AudioCandidateRequest> candidates)
    {
        if (candidates.Count > MaxCandidatesPerScan)
            return $"A scan may propose at most {MaxCandidatesPerScan} candidates; got {candidates.Count}.";
        if (candidates.Any(c => c.EndSeconds <= c.StartSeconds))
            return "Every candidate must end after it starts.";
        if (candidates.Any(c => c.StartSeconds < 0))
            return "A candidate cannot start before the recording does.";
        if (candidates.Any(c => c.Score is < 0 or > 100 or float.NaN))
            return "Candidate scores must be between 0 and 100.";
        return null;
    }

    /// <summary>
    /// Replaces this file's pending candidates with <paramref name="candidates"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Only Pending markers are replaced.</b> Confirmed markers are findings someone stands
    /// behind and Dismissed ones are the record of what a reviewer already rejected — wiping either
    /// would make a re-scan resurrect everything the reviewer had worked through. Deleting and
    /// inserting run in one transaction so a failure can't leave the file showing half of two
    /// different scans.</para>
    /// <para>The caller has already checked <see cref="CandidateProblem"/> and that the person may
    /// see the file.</para>
    /// </remarks>
    public async Task<Replaced> ReplacePendingAsync(
        BenDataContext db, Guid fileId, IEnumerable<AudioCandidateRequest> candidates,
        Guid createdBy, CancellationToken ct, Func<AudioCandidateRequest, string?>? noteFor = null,
        Func<AudioCandidateRequest, bool>? exploratory = null)
    {
        var now = DateTime.UtcNow;
        var stale = await db.AudioMarkers
            .Where(m => m.UploadFileId == fileId && m.ReviewStatus == EvpReviewStatus.Pending)
            .ToListAsync(ct);

        var fresh = candidates
            .OrderBy(c => c.StartSeconds)
            .Select(c => new AudioMarker
            {
                Id                 = Guid.NewGuid(),
                UploadFileId       = fileId,
                TimeSeconds        = c.StartSeconds,
                EndSeconds         = c.EndSeconds,
                // Deliberately not "Possible EVP": the detector found signal standing out from the
                // noise floor, which is a different claim from a voice being present.
                Label              = "Detected signal",
                ConfidenceLevel    = EvpConfidenceLevel.Possible,
                IsAutoDetected     = true,
                DetectionScore     = c.Score,
                IsExploratory      = exploratory?.Invoke(c) ?? false,
                ReviewStatus       = EvpReviewStatus.Pending,
                Note               = noteFor?.Invoke(c),
                DateCreated        = now,
                CreatedByAppUserId = createdBy,
            })
            .ToList();

        // The in-memory provider used by tests doesn't support transactions, so skip it there
        // rather than fail every test — same accommodation as CaseController.
        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await using (transaction)
        {
            db.AudioMarkers.RemoveRange(stale);
            db.AudioMarkers.AddRange(fresh);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }

        return new Replaced(fresh, stale.Count);
    }

    /// <summary>
    /// Proposes what <paramref name="analysis"/> found as this file's pending candidates, leaving
    /// out anything a person has already ruled on, and records the scan.
    /// </summary>
    /// <remarks>
    /// Anything overlapping a marker the reviewer has already ruled on — confirmed or dismissed —
    /// is dropped before saving. Without that, every re-scan hands back the same rejected noise
    /// and the review queue never converges.
    /// </remarks>
    /// <returns>A null result and the reason when the proposals cannot be saved.</returns>
    public async Task<(Proposed? Result, string? Problem)> ProposeAsync(
        BenDataContext db, Guid fileId, EvpAnalysis analysis, EvpSensitivity sensitivity,
        EvpDetectionOptions settings, Guid createdBy, CancellationToken ct,
        Func<AudioCandidateRequest, string?>? noteFor = null)
    {
        // Everything a person has already ruled on, so the scan doesn't re-propose it.
        var reviewed = await db.AudioMarkers.AsNoTracking()
            .Where(m => m.UploadFileId == fileId && m.ReviewStatus != EvpReviewStatus.Pending)
            .Select(m => new { m.TimeSeconds, m.EndSeconds })
            .ToListAsync(ct);

        var proposed = analysis.Candidates
            .Where(c => !reviewed.Any(r => OverlapsEnough(c, r.TimeSeconds, r.EndSeconds, settings.ReviewedOverlapFraction)))
            .ToList();
        var requests = proposed
            .Select(c => new AudioCandidateRequest(c.StartSeconds, c.EndSeconds, c.Score))
            .ToList();
        var exploratorySpans = proposed.Where(c => c.Exploratory)
            .Select(c => (c.StartSeconds, c.EndSeconds)).ToHashSet();

        if (CandidateProblem(requests) is { } problem) return (null, problem);

        var replaced = await ReplacePendingAsync(db, fileId, requests, createdBy, ct, noteFor,
            r => exploratorySpans.Contains((r.StartSeconds, r.EndSeconds)));
        var scanId   = await TryRecordScanAsync(fileId, sensitivity, settings, analysis, proposed, replaced.Fresh, ct);
        return (new Proposed(replaced, scanId), null);
    }

    /// <summary>
    /// Keeps what this scan measured, for the detector to learn from later (item 253).
    /// </summary>
    /// <remarks>
    /// <para>Written after the candidates are saved, in its own save, and allowed to fail: the
    /// person asked for a scan, not for a training record, and must get their candidates either
    /// way. A failure is logged, the way a failed audit write is.</para>
    /// <para>Only scans made here are recorded. Candidates sent from outside, with scores nobody
    /// here measured, are not evidence of anything the detector did.</para>
    /// </remarks>
    private async Task<Guid?> TryRecordScanAsync(
        Guid fileId, EvpSensitivity sensitivity, EvpDetectionOptions settings, EvpAnalysis analysis,
        IReadOnlyList<EvpCandidate> proposed, IReadOnlyList<AudioMarker> created, CancellationToken ct)
    {
        try
        {
            // Each marker was made from one proposal's exact bounds, so the bounds pair them.
            var markerBySpan = created
                .Where(m => m.EndSeconds is not null)
                .ToDictionary(m => (m.TimeSeconds, m.EndSeconds!.Value), m => m.Id);
            var proposedSet = proposed.ToHashSet();

            var scan = new EvpScan
            {
                Id                 = Guid.NewGuid(),
                UploadFileId       = fileId,
                DateCreated        = DateTime.UtcNow,
                DetectorVersion    = EvpDetector.Version,
                Sensitivity        = sensitivity,
                ThresholdDb        = settings.ThresholdDb,
                MinDurationSeconds = settings.MinDurationSeconds,
                MergeGapSeconds    = settings.MergeGapSeconds,
                ContextPadSeconds  = settings.ContextPadSeconds,
                MaxEventSeconds    = settings.MaxEventSeconds,
                OptionsJson        = JsonSerializer.Serialize(settings, OptionsJson),
                BandLowHz          = settings.BandLowHz,
                BandHighHz         = settings.BandHighHz,
                TraceBlob          = analysis.Trace is { } trace ? EvpTraceCodec.Encode(trace) : null,
                TraceBucketMs      = analysis.Trace is { } t ? (int)Math.Round(t.BucketSeconds * 1000) : null,
                RecordingSeconds   = analysis.RecordingSeconds,
                FoundCount         = analysis.Candidates.Count,
                ProposedCount      = proposed.Count,
            };
            foreach (var c in analysis.Candidates)
            {
                var wasProposed = proposedSet.Contains(c);
                scan.Candidates.Add(new EvpScanCandidate
                {
                    Id                = Guid.NewGuid(),
                    AudioMarkerId     = wasProposed && markerBySpan.TryGetValue((c.StartSeconds, c.EndSeconds), out var markerId)
                                            ? markerId : null,
                    Proposed          = wasProposed,
                    StartSeconds      = c.StartSeconds,
                    EndSeconds        = c.EndSeconds,
                    Score             = c.Score,
                    PeakProminenceDb  = c.Features.PeakProminenceDb,
                    MeanBandGapDb     = c.Features.MeanBandGapDb,
                    EventSeconds      = c.Features.EventSeconds,
                    MeanFloorDb       = c.Features.MeanFloorDb,
                    PeakBandDb        = c.Features.PeakBandDb,
                    BandLevelSpreadDb = c.Features.BandLevelSpreadDb,
                    ZeroCrossingRate  = c.Features.ZeroCrossingRate,
                    Exploratory       = c.Exploratory,
                });
            }

            await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
            db.EvpScans.Add(scan);
            await db.SaveChangesAsync(ct);
            return scan.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "EVP learning record ({What}) not written for file {FileId}", "scan", fileId);
            return null;
        }
    }

    /// <summary>
    /// True when a proposal covers enough of an already-reviewed marker to be the same event.
    /// A reviewed point marker (no end) counts as a hit if the proposal simply contains it.
    /// </summary>
    private static bool OverlapsEnough(
        EvpCandidate candidate, double reviewedStart, double? reviewedEnd, double overlapFraction)
    {
        if (reviewedEnd is not { } end || end <= reviewedStart)
            return candidate.StartSeconds <= reviewedStart && candidate.EndSeconds >= reviewedStart;

        var overlap = Math.Min(candidate.EndSeconds, end) - Math.Max(candidate.StartSeconds, reviewedStart);
        if (overlap <= 0) return false;

        // Measured against the shorter of the two, so a long candidate can't dodge the check by
        // dwarfing a short reviewed span (or the reverse).
        var shortest = Math.Min(candidate.DurationSeconds, end - reviewedStart);
        return shortest > 0 && overlap / shortest >= overlapFraction;
    }
}
