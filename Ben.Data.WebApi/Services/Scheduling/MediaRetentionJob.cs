using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Media;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Warns people what is about to go, then takes it (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "Evidence collected - unless marked to save - only lasts a week
/// for everything but photos. Photos stay a month… Unless they mark them to be saved, they stay
/// on the site for a week so the end user can save them or download them."</para>
///
/// <para><b>The warning is the point.</b> Somebody handed over the only copy of a photograph they
/// took; deleting it without telling them would be taking it. So the sweep writes first and
/// deletes second, and never deletes anything it has not already warned about.</para>
///
/// <para><b>The plan is re-read before anything is deleted.</b> A stamp is what the plan said at
/// upload; a business that has since moved to a plan with no retention keeps its files, expiry
/// column and all. Deleting on the strength of an old stamp would take files off somebody who is
/// now paying for them to stay.</para>
/// </remarks>
public sealed class MediaRetentionJob : IScheduledJob
{
    public string Name => "media-retention";

    /// <summary>How often this actually does anything.</summary>
    /// <remarks>
    /// The scheduler wakes every few minutes; nothing here changes that fast. Static because a job
    /// is resolved fresh from a scope every pass, and losing it on restart costs one wasted sweep.
    /// </remarks>
    public static readonly TimeSpan MinimumSweepAge = TimeSpan.FromHours(6);
    private static DateTime _lastSweptUtc = DateTime.MinValue;

    /// <summary>How long before it goes somebody is told.</summary>
    /// <remarks>
    /// Seven days for a photograph's month, and one day for a recording's week — far enough ahead
    /// to act on, close enough to be about this file rather than a diary note. Both are sent; a
    /// recording gets the second one only.
    /// </remarks>
    public static readonly TimeSpan FirstNotice = TimeSpan.FromDays(7);
    public static readonly TimeSpan LastNotice = TimeSpan.FromDays(1);

    /// <summary>How many files one pass will touch.</summary>
    private const int Batch = 200;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IEmailService _email;
    private readonly IFileStorageService _storage;
    private readonly IMediaIngestService _media;
    private readonly MediaRetentionPolicy _retention;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<MediaRetentionJob> _log;

    public MediaRetentionJob(
        IDbContextFactory<BenDataContext> dbFactory, IEmailService email,
        IFileStorageService storage, IMediaIngestService media,
        MediaRetentionPolicy retention,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site,
        ILogger<MediaRetentionJob> log)
    {
        _dbFactory = dbFactory; _email = email; _storage = storage; _media = media;
        _retention = retention; _site = site.Value; _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastSweptUtc < MinimumSweepAge) return;
        _lastSweptUtc = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await WarnAsync(db, ct);
        await SweepAsync(db, ct);
    }

    // ── the warning ──────────────────────────────────────────────────────────

    private async Task WarnAsync(BenDataContext db, CancellationToken ct)
    {
        if (!_email.IsConfigured) return;

        var now = DateTime.UtcNow;
        var horizon = now + FirstNotice;

        var due = await db.UploadFiles
            .Where(f => f.ExpiresAtUtc != null
                     && f.KeptAtUtc == null
                     && f.ExpiresAtUtc > now
                     && f.ExpiresAtUtc <= horizon
                     // Told once per window: a file warned a week out is warned again a day out,
                     // and never twice inside the same window.
                     && (f.ExpiryNoticeSentAtUtc == null
                         || (f.ExpiresAtUtc <= now + LastNotice
                             && f.ExpiryNoticeSentAtUtc < f.ExpiresAtUtc!.Value - LastNotice)))
            .OrderBy(f => f.ExpiresAtUtc)
            .Take(Batch)
            .ToListAsync(ct);

        var warned = 0;
        foreach (var file in due)
        {
            if (ct.IsCancellationRequested) break;

            var address = await db.AppUsers.AsNoTracking()
                .Where(u => u.Id == file.AppUserId)
                .Select(u => new { u.Email, u.DisplayName })
                .FirstOrDefaultAsync(ct);

            if (address?.Email is not { Length: > 0 } to)
            {
                // Nobody to write to: stamp it anyway so the sweep does not keep re-reading it.
                file.ExpiryNoticeSentAtUtc = now;
                continue;
            }

            try
            {
                await _email.SendAsync(to, SubjectFor(file), BodyFor(file, address.DisplayName), ct);
                file.ExpiryNoticeSentAtUtc = now;
                warned++;
            }
            catch (Exception ex)
            {
                // No stamp, so the next pass tries again — the same bargain the event reminder
                // makes, and the right one when the alternative is deleting something unannounced.
                _log.LogWarning(ex, "Could not warn {UserId} that file {FileId} is going.",
                    file.AppUserId, file.Id);
            }
        }

        if (due.Count > 0) await db.SaveChangesAsync(ct);
        if (warned > 0) _log.LogInformation("Warned {Count} people about expiring media.", warned);
    }

    private string SubjectFor(Ben.Data.Source.Entities.UploadFile file)
        => $"Your file \"{file.FileName}\" comes down on {file.ExpiresAtUtc:MM/dd/yyyy}";

    private string BodyFor(Ben.Data.Source.Entities.UploadFile file, string? name)
    {
        var greeting = string.IsNullOrWhiteSpace(name) ? "Hello" : $"Hello {NotificationText.Safe(name)}";
        var mine = _site.AbsoluteUrl("/my-evidence");

        return $"<p>{greeting},</p>"
             + $"<p><strong>{NotificationText.Safe(file.FileName)}</strong> is due to come off the "
             + $"site on {file.ExpiresAtUtc:MM/dd/yyyy}. Download it before then if you want to "
             + "keep a copy — it is yours.</p>"
             + $"<p><a href=\"{mine}\">Everything you have sent in</a></p>"
             + $"<p>— {NotificationText.Safe(_site.Name)}</p>";
    }

    // ── the sweep ────────────────────────────────────────────────────────────

    private async Task SweepAsync(BenDataContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var expired = await db.UploadFiles
            .Where(f => f.ExpiresAtUtc != null && f.KeptAtUtc == null && f.ExpiresAtUtc <= now)
            .OrderBy(f => f.ExpiresAtUtc)
            .Take(Batch)
            .ToListAsync(ct);

        var taken = 0;
        foreach (var file in expired)
        {
            if (ct.IsCancellationRequested) break;

            // The plan, now — not the plan that stamped it. A business that has left the tour
            // plan keeps its files.
            var orgId = await MediaRetentionPolicy.OrganizationForAsync(db, file.Id, ct);
            var rules = await _retention.RulesForAsync(orgId, ct);
            if (!rules.Any)
            {
                file.ExpiresAtUtc = null;
                continue;
            }

            // A picture kept on a tour's page is a picture the business decided to keep, whatever
            // the file's own column says. Belt and braces: the keep sets KeptAtUtc too.
            if (await db.TourGalleryImages.AnyAsync(g => g.UploadFileId == file.Id, ct))
            {
                file.KeptAtUtc = now;
                continue;
            }

            var path = file.StoragePath;

            // Everything pointing at it lets go first, or the row will not delete.
            await db.EventEvidenceSubmissions.Where(e => e.UploadFileId == file.Id).ExecuteDeleteAsync(ct);
            await db.FieldSessionUploadFiles.Where(f2 => f2.UploadFileId == file.Id).ExecuteDeleteAsync(ct);
            await db.CaseFiles.Where(c => c.UploadFileId == file.Id).ExecuteDeleteAsync(ct);
            await db.UploadFileMetadata.Where(m => m.UploadFileId == file.Id).ExecuteDeleteAsync(ct);

            if (!await Admin.UploadFileRows.TryDeleteAsync(db, file.Id, ct))
            {
                // Something still holds it — a citation, a publication. Leave it, drop the clock,
                // and say so: a file the database will not let go of must not be re-tried forever.
                _log.LogWarning(
                    "File {FileId} expired but is still held by something; its clock was cleared.",
                    file.Id);
                file.ExpiresAtUtc = null;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                try { await _media.DeleteAllAsync(path, ct); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Deleted the row for {FileId} but not its bytes at {Path}.",
                        file.Id, path);
                }
            }

            taken++;
        }

        await db.SaveChangesAsync(ct);
        if (taken > 0) _log.LogInformation("Retention removed {Count} expired file(s).", taken);
    }
}
