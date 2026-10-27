using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The EVP learning tables, read back (audio phase 11, item 253, 10/27/2026): what the scanner found, what
/// people kept and dismissed, what it missed, and how each measurement differs between kept and dismissed.
/// </summary>
/// <remarks>
/// <para>Until now the scans, candidates and rulings were written and never read. This is the first
/// reader, and it changes nothing: whether the detector's weights should move is a person's judgment
/// from these charts, not something this page does (Ben, 10/27: hold off on retraining).</para>
/// <para>A ruling is joined to the candidate it judged through the marker; when a marker was ruled on more
/// than once, the latest ruling counts.</para>
/// </remarks>
[ApiController]
[Route("api/admin/evp")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminEvpController(IDbContextFactory<BenDataContext> dbContextFactory) : ControllerBase
{
    public const int MaxDays = 365;
    private const int Bins = 10;

    [HttpGet("summary")]
    public async Task<ActionResult<AdminEvpSummary>> Summary([FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var since = DateTime.UtcNow.Date.AddDays(1 - days);
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var scans = db.EvpScans.AsNoTracking().Where(s => s.DateCreated >= since);
        var scanDays = await scans.Select(s => s.DateCreated.Date).ToListAsync(ct);
        var candidates = db.EvpScanCandidates.AsNoTracking().Where(c => c.EvpScan.DateCreated >= since);

        var rulings = await db.EvpRulings.AsNoTracking()
            .Where(r => r.DateCreated >= since)
            .Select(r => new { r.AudioMarkerId, r.Ruling, r.PlayedFirst, r.Blind, r.DateCreated })
            .ToListAsync(ct);
        var latest = rulings.GroupBy(r => r.AudioMarkerId).Select(g => g.MaxBy(r => r.DateCreated)!).ToList();
        double? Share(int part, int whole) => whole == 0 ? null : (double)part / whole;
        var blind = latest.Where(r => r.Blind == true).ToList();
        var shown = latest.Where(r => r.Blind != true).ToList();

        // The measurements of every ruled candidate, for the histograms.
        var markerIds = latest.Select(r => r.AudioMarkerId).ToList();
        var ruledBy = latest.ToDictionary(r => r.AudioMarkerId, r => r.Ruling == EvpReviewStatus.Confirmed);
        var measured = await db.EvpScanCandidates.AsNoTracking()
            .Where(c => c.AudioMarkerId != null && markerIds.Contains(c.AudioMarkerId.Value))
            .Select(c => new
            {
                MarkerId = c.AudioMarkerId!.Value, c.Score, c.PeakProminenceDb, c.MeanBandGapDb, c.EventSeconds,
                c.BandLevelSpreadDb, c.ZeroCrossingRate, c.SpectralFlatness, c.VoicedFrameFraction, c.VoiceLikelihood,
            })
            .ToListAsync(ct);
        var rows = measured.Select(m => (Kept: ruledBy[m.MarkerId], m)).ToList();

        var histograms = new List<AdminEvpHistogram>
        {
            Histogram("Score", "0–100", 0, 100, rows.Select(r => (r.Kept, (double?)r.m.Score))),
            Histogram("Stood out above the room", "dB", 0, 30, rows.Select(r => (r.Kept, (double?)r.m.PeakProminenceDb))),
            Histogram("Share in the listening range", "dB from all of it", -20, 0, rows.Select(r => (r.Kept, (double?)r.m.MeanBandGapDb))),
            Histogram("Length", "seconds", 0, 3, rows.Select(r => (r.Kept, (double?)r.m.EventSeconds))),
            Histogram("Level movement", "dB", 0, 12, rows.Select(r => (r.Kept, (double?)r.m.BandLevelSpreadDb))),
            Histogram("Hiss-like crossings", "share of samples", 0, 0.5, rows.Select(r => (r.Kept, (double?)r.m.ZeroCrossingRate))),
            Histogram("Spectral flatness", "0–1", 0, 1, rows.Select(r => (r.Kept, (double?)r.m.SpectralFlatness))),
            Histogram("Voiced share", "0–1", 0, 1, rows.Select(r => (r.Kept, (double?)r.m.VoicedFrameFraction))),
            Histogram("Voice-like", "0–1", 0, 1, rows.Select(r => (r.Kept, (double?)r.m.VoiceLikelihood))),
        };

        var perDay = Enumerable.Range(0, days).Select(i => since.AddDays(i))
            .Select(d => new AdminEvpDay(d.ToString("yyyy-MM-dd"), scanDays.Count(s => s == d))).ToList();

        return Ok(new AdminEvpSummary(
            days,
            scanDays.Count,
            await candidates.CountAsync(ct),
            await candidates.CountAsync(c => c.Proposed, ct),
            await candidates.CountAsync(c => c.Proposed && c.Exploratory, ct),
            latest.Count,
            latest.Count(r => r.Ruling == EvpReviewStatus.Confirmed),
            latest.Count(r => r.Ruling == EvpReviewStatus.Dismissed),
            Share(latest.Count(r => r.PlayedFirst == true), latest.Count),
            Share(blind.Count, latest.Count),
            Share(blind.Count(r => r.Ruling == EvpReviewStatus.Confirmed), blind.Count),
            Share(shown.Count(r => r.Ruling == EvpReviewStatus.Confirmed), shown.Count),
            await db.EvpMisses.AsNoTracking().CountAsync(m => m.DateCreated >= since, ct),
            perDay,
            histograms));
    }

    /// <summary>Ten equal bins between <paramref name="lo"/> and <paramref name="hi"/>; values outside go in the end bins.</summary>
    internal static AdminEvpHistogram Histogram(string feature, string unit, double lo, double hi,
        IEnumerable<(bool Kept, double? Value)> values)
    {
        var kept = new int[Bins];
        var dismissed = new int[Bins];
        var measured = 0;
        foreach (var (isKept, value) in values)
        {
            if (value is not { } v || double.IsNaN(v)) continue;
            measured++;
            var bin = Math.Clamp((int)Math.Floor((v - lo) / (hi - lo) * Bins), 0, Bins - 1);
            if (isKept) kept[bin]++; else dismissed[bin]++;
        }
        var width = (hi - lo) / Bins;
        return new AdminEvpHistogram(feature, unit, measured,
            Enumerable.Range(0, Bins).Select(i => new AdminEvpBin(lo + i * width, lo + (i + 1) * width, kept[i], dismissed[i])).ToList());
    }
}
