using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Places;
using Ben.Data.WebApi.Services.Venues;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Tells a place's custodian when a room's scan turns a year old (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// <para>From that day anybody who may scan there may replace it (<see cref="PlaceScans.RefreshAfter"/>),
/// so the custodian hears it before a stranger's scan does it for them: "Bedroom 1's scan is a year
/// old; anyone who may scan here can now replace it."</para>
///
/// <para><b>Once per scan</b>, marked by <c>PlaceScan.YearNoticeSentUtc</c> after the send, so a
/// failure is tried again next pass and the worst case is a second note rather than none.</para>
/// </remarks>
public sealed class PlaceScanAgeJob(
    IDbContextFactory<BenDataContext> dbFactory,
    PlatformMessageService messages,
    ILogger<PlaceScanAgeJob> logger,
    TimeProvider? clock = null) : IScheduledJob
{
    public string Name => "place-scan-age";

    /// <summary>How many year-old scans one pass deals with.</summary>
    internal const int BatchSize = 50;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var cutoff = now - PlaceScans.RefreshAfter;

        var due = await db.PlaceScans
            .Include(s => s.Place)
            .Where(s => s.State == PlaceScanState.Current && s.YearNoticeSentUtc == null && s.ScannedUtc <= cutoff)
            .OrderBy(s => s.ScannedUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var scan in due)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var custodian = await PlaceScans.CustodianAsync(db, scan.Place, scan, ct);
                var recipients = custodian is null ? [] : await PlaceScans.CustodianRecipientsAsync(db, custodian, ct);

                if (recipients.Count > 0)
                {
                    await messages.SendAsync(
                        $"{scan.RoomName}'s scan is a year old",
                        $"<p>{VenueNotices.Safe(scan.RoomName)}'s scan at {VenueNotices.Safe(scan.Place.Name)} is a year old "
                        + $"(made {scan.ScannedUtc:MM/dd/yyyy}); anyone who may scan here can now replace it. "
                        + "Scan it again yourself if you would rather keep it in your hands.</p>",
                        recipients, scan.ScannedByAppUserId, ct);
                }

                // Stamped even with nobody to tell: asking the same unanswerable question every pass
                // helps nobody.
                scan.YearNoticeSentUtc = now;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not send the year-old note for room scan {ScanId}.", scan.Id);
            }
        }
    }
}
