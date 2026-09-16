using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Converts case notes written as plain text into the HTML every note is now stored as.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Case notes became formatted text on 2026-09-14 (beta feedback) and the website draws them as
/// markup. A note written before that is plain text whose line breaks would run together, and it was never
/// sanitized, because it was never drawn as markup. <see cref="CaseNoteBodies.ToHtml"/> turns either kind into safe
/// HTML; this applies it to the stored rows once, so the database holds one kind of body.</para>
///
/// <para><b>Idempotent with no marker</b>, the same shape as <see cref="MessageBodySanitizeBackfillService"/>: each
/// body is converted in memory and written only when the result differs, so the second pass writes nothing.</para>
///
/// <para><b>Reading does not wait for it.</b> The notes endpoint applies the same conversion as it reads, so a row this
/// pass has not reached yet is still drawn with its line breaks and without anything that could run.</para>
/// </remarks>
public sealed class CaseNoteBodyHtmlBackfillService : BackgroundService
{
    private const int BatchSize = 500;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ICmsMarkupSanitizer _sanitizer;
    private readonly ILogger<CaseNoteBodyHtmlBackfillService> _logger;

    public CaseNoteBodyHtmlBackfillService(
        IDbContextFactory<BenDataContext> dbFactory,
        ICmsMarkupSanitizer sanitizer,
        ILogger<CaseNoteBodyHtmlBackfillService> logger)
    {
        _dbFactory = dbFactory;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var converted = await ConvertAsync(stoppingToken);
            if (converted > 0)
                _logger.LogInformation("Converted {Count} case note body(ies) from plain text to HTML.", converted);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-pass; what is left is decided by content and picked up on the next start.
        }
        catch (Exception ex)
        {
            // Not fatal: notes are converted as they are read. Loud, so it gets looked at.
            _logger.LogError(ex, "Could not convert stored case note bodies to HTML.");
        }
    }

    private async Task<int> ConvertAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ids = await db.CaseNotes.OrderBy(n => n.Id).Select(n => n.Id).ToListAsync(ct);

        var changed = 0;
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            if (ct.IsCancellationRequested) return changed;

            var rows = await db.CaseNotes.Where(n => chunk.Contains(n.Id)).ToListAsync(ct);
            foreach (var row in rows)
            {
                var html = CaseNoteBodies.ToHtml(row.Body, _sanitizer);
                if (string.Equals(html, row.Body, StringComparison.Ordinal)) continue;
                row.Body = html;
                changed++;
            }

            // DateUpdated is not touched: nobody edited these notes.
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return changed;
    }
}
