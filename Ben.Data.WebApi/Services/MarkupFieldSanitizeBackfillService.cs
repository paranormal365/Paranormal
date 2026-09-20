using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Cleans the six fields that were drawn as markup before their doors started sanitising
/// (2026-09-20).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Sweeping every field the website hands to <c>MarkupString</c>
/// found six stored with nothing but a <c>Trim()</c>. The doors are closed now, and the rows
/// written before that still hold whatever was typed — and are still rendered the same way.
/// Cleaning the door without cleaning the room leaves it live on every database that has been
/// running, which is the same reasoning
/// <see cref="MessageBodySanitizeBackfillService"/> was written under.</para>
///
/// <para><b>Two of these reach people who never signed in.</b> A case report's summary and
/// conclusion are drawn on <c>/o/{UrlName}/cases/{CaseRef}</c>, and a client request's description
/// is taken from a form that needs no account. The rest are group-facing.</para>
///
/// <para><b>Idempotent with no marker, following the service above.</b> Each value is sanitised in
/// memory and written only where the result differs, so a second run finds nothing and writes
/// nothing. A "done" flag has to be kept in step with a re-run by hand, and a stale flag is how a
/// cleanup silently stops happening.</para>
///
/// <para><b>Formatting survives.</b> The sanitiser is the one the CMS, publications and public
/// events already use: it keeps what a rich-text editor produces and drops scripts, event handlers
/// and anything that navigates or submits on its own.</para>
///
/// <para><b>Timestamps are not touched.</b> Nobody edited these rows. Moving <c>DateUpdated</c>
/// would rewrite a case's history to record our maintenance, and a group reading its own timeline
/// would see every entry change on the day we deployed.</para>
/// </remarks>
public sealed class MarkupFieldSanitizeBackfillService : BackgroundService
{
    /// <summary>Rows read per round trip, matching the service this follows.</summary>
    private const int BatchSize = 500;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ICmsMarkupSanitizer _sanitizer;
    private readonly ILogger<MarkupFieldSanitizeBackfillService> _logger;

    public MarkupFieldSanitizeBackfillService(
        IDbContextFactory<BenDataContext> dbFactory,
        ICmsMarkupSanitizer sanitizer,
        ILogger<MarkupFieldSanitizeBackfillService> logger)
    {
        _dbFactory = dbFactory;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var counts = new List<(string What, int Changed)>
            {
                ("case timeline entries",
                    await CleanAsync<Source.Entities.CaseTimelineEntry>(
                        d => d.CaseTimelineEntries, e => e.Body, (e, v) => e.Body = v, stoppingToken)),

                ("case report summaries",
                    await CleanAsync<Source.Entities.CaseReport>(
                        d => d.CaseReports, r => r.Summary, (r, v) => r.Summary = v, stoppingToken)),

                ("case report conclusions",
                    await CleanAsync<Source.Entities.CaseReport>(
                        d => d.CaseReports, r => r.Conclusion, (r, v) => r.Conclusion = v, stoppingToken)),

                ("case report sections",
                    await CleanAsync<Source.Entities.CaseReportSection>(
                        d => d.CaseReportSections, s => s.Body, (s, v) => s.Body = v, stoppingToken)),

                ("investigation notes",
                    await CleanAsync<Source.Entities.Investigation>(
                        d => d.Investigations, i => i.Notes, (i, v) => i.Notes = v, stoppingToken)),

                ("region notes",
                    await CleanAsync<Source.Entities.UploadFileRegionNote>(
                        d => d.UploadFileRegionNotes, n => n.NoteHtml, (n, v) => n.NoteHtml = v, stoppingToken)),

                ("client request descriptions",
                    await CleanAsync<Source.Entities.ClientRequest>(
                        d => d.ClientRequests, r => r.Description, (r, v) => r.Description = v, stoppingToken)),
            };

            var touched = counts.Where(c => c.Changed > 0).ToList();

            // Silent when there was nothing to do, which is every start after the first. A line
            // saying "cleaned 0" on every boot trains people to stop reading the log.
            if (touched.Count > 0)
                _logger.LogWarning(
                    "Sanitised stored values that predate sanitising on save: {Summary}.",
                    string.Join(", ", touched.Select(c => $"{c.Changed} {c.What}")));
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-pass. What is left is picked up on the next start, because what
            // still needs cleaning is decided by the content rather than by a marker.
        }
        catch (Exception ex)
        {
            // Loud, and not fatal. The site works with dirty values; it is just not safe, and the
            // person who can fix that needs to know rather than find out from a reader.
            _logger.LogError(ex, "Could not sanitise stored markup fields.");
        }
    }

    /// <summary>Cleans one text column of one table, writing only where the value changes.</summary>
    private async Task<int> CleanAsync<TEntity>(
        Func<BenDataContext, DbSet<TEntity>> set,
        Func<TEntity, string?> read,
        Action<TEntity, string?> write,
        CancellationToken ct)
        where TEntity : class
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // The keys first, then the rows in batches — the same shape as the message backfill, and
        // for its reason: keyset paging reads less but needs a Guid comparison that not every
        // provider translates, and a cleanup that throws on the one database it exists for is
        // worse than one that reads a list of keys.
        var ids = await set(db).AsNoTracking()
            .Select(e => EF.Property<Guid>(e, "Id"))
            .OrderBy(id => id)
            .ToListAsync(ct);

        var changed = 0;

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            if (ct.IsCancellationRequested) return changed;

            var rows = await set(db)
                .Where(e => chunk.Contains(EF.Property<Guid>(e, "Id")))
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                var current = read(row);
                if (string.IsNullOrEmpty(current)) continue;

                var cleaned = _sanitizer.SanitizeHtml(current);
                if (string.Equals(cleaned, current, StringComparison.Ordinal)) continue;

                write(row, cleaned);
                changed++;
            }

            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return changed;
    }
}
