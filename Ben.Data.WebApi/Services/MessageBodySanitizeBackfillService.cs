using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Cleans message bodies that were stored before sending started sanitising them.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A message body is rendered as markup by every reader, and until
/// 2026-09-04 it was stored exactly as posted. An <c>&lt;img onerror&gt;</c> in a group broadcast
/// therefore ran in each recipient's session, on this site's own origin, with no script policy to
/// stop it. Sending is fixed; the rows written before that fix still hold whatever was posted, and
/// they are still rendered the same way. Cleaning the door without cleaning the room would leave
/// the exploit live on every database that has been running.</para>
///
/// <para><b>Both tables, for different reasons.</b> <c>OrgMessage</c> bodies are typed by people.
/// <c>UserMessage</c> bodies are composed by the platform, but several of them interpolated
/// user-typed text without encoding it — the other half of the same finding — so rows written
/// before that fix can carry the same payload.</para>
///
/// <para><b>Idempotent with no marker, deliberately.</b> It sanitises each body in memory and
/// writes only where the result differs, so the second run finds nothing to do and writes nothing.
/// A "done" flag would have to be kept in step with a re-run by hand, and a stale flag is how a
/// cleanup silently stops happening.</para>
///
/// <para><b>Legitimate formatting survives.</b> The sanitiser is the one the CMS, publications and
/// public events already use: it keeps the tags a rich-text editor produces and drops scripts,
/// event handlers and anything that can navigate or submit on its own.</para>
/// </remarks>
public sealed class MessageBodySanitizeBackfillService : BackgroundService
{
    /// <summary>Bodies read per round trip. Big enough to be one query on any real database,
    /// small enough that a very large table is never loaded into memory at once.</summary>
    private const int BatchSize = 500;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ICmsMarkupSanitizer _sanitizer;
    private readonly ILogger<MessageBodySanitizeBackfillService> _logger;

    public MessageBodySanitizeBackfillService(
        IDbContextFactory<BenDataContext> dbFactory,
        ICmsMarkupSanitizer sanitizer,
        ILogger<MessageBodySanitizeBackfillService> logger)
    {
        _dbFactory = dbFactory;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var orgMessages = await CleanOrgMessagesAsync(stoppingToken);
            var userMessages = await CleanUserMessagesAsync(stoppingToken);

            // Silent when there was nothing to do, which is every start after the first. A line
            // saying "cleaned 0" on every boot trains people to stop reading the log.
            if (orgMessages + userMessages > 0)
                _logger.LogWarning(
                    "Sanitised {OrgCount} stored group message body(ies) and {UserCount} notification "
                    + "body(ies) that predate sanitising on send.", orgMessages, userMessages);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-pass. Whatever is left is picked up on the next start, because
            // what still needs cleaning is decided by the content rather than by a marker.
        }
        catch (Exception ex)
        {
            // Loud, and not fatal. The site works with dirty bodies; it is just not safe, and the
            // person who can fix that needs to know rather than find out from a reader.
            _logger.LogError(ex, "Could not sanitise stored message bodies.");
        }
    }

    private async Task<int> CleanOrgMessagesAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // The ids first, then the bodies in batches. Keyset paging would read less, but it needs a
        // comparison on Guid that not every provider translates, and a cleanup that throws on the
        // one database it exists for is worse than one that reads a list of keys.
        var ids = await db.OrgMessages.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync(ct);

        var changed = 0;
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            if (ct.IsCancellationRequested) return changed;

            var rows = await db.OrgMessages
                .Where(m => chunk.Contains(m.Id))
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                var cleaned = _sanitizer.SanitizeHtml(row.Body);
                if (string.Equals(cleaned, row.Body, StringComparison.Ordinal)) continue;
                row.Body = cleaned;
                changed++;
            }

            // DateUpdated is deliberately not touched: nobody edited these messages, and moving
            // the timestamp would rewrite the history of a conversation to record our maintenance.
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return changed;
    }

    private async Task<int> CleanUserMessagesAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ids = await db.UserMessages.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync(ct);

        var changed = 0;
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            if (ct.IsCancellationRequested) return changed;

            var rows = await db.UserMessages
                .Where(m => chunk.Contains(m.Id))
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.MessageBody)) continue;

                var cleaned = _sanitizer.SanitizeHtml(row.MessageBody);
                if (string.Equals(cleaned, row.MessageBody, StringComparison.Ordinal)) continue;
                row.MessageBody = cleaned;
                changed++;
            }

            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return changed;
    }
}
