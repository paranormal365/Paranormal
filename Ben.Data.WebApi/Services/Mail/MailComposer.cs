using Ben.Data.Common;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Mail;

/// <summary>
/// Uses the letter somebody wrote, or the one the code writes (item 246).
/// </summary>
/// <remarks>
/// <para><b>The fallback is the point.</b> Every mailer keeps composing exactly what it composes
/// today and hands it here; a published template replaces it, and anything else — no row, an
/// unpublished draft, a template that throws, a database that is down — leaves the built-in letter
/// untouched. A feature for editing letters must not become a way to stop them going.</para>
///
/// <para><b>Cached for a minute.</b> One announcement can be hundreds of letters, and reading the
/// same row hundreds of times to render the same words is the sort of cost that only shows up on
/// the day it matters. A minute is short enough that publishing feels immediate.</para>
/// </remarks>
public sealed class MailComposer
{
    /// <summary>How long a looked-up template is reused before being read again.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(1);

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMemoryCache _cache;
    private readonly SiteIdentity _site;
    private readonly ILogger<MailComposer> _logger;

    public MailComposer(IDbContextFactory<BenDataContext> db, IMemoryCache cache,
                        IOptions<SiteIdentity> site, ILogger<MailComposer> logger)
    {
        _db = db;
        _cache = cache;
        _site = site.Value;
        _logger = logger;
    }

    /// <summary>What a letter should say.</summary>
    /// <param name="kind">Which letter this is, and therefore which tables its tokens may read.</param>
    /// <param name="tables">The rows this letter is carrying, by table name.</param>
    /// <param name="zone">The reader's zone; the site's when they have not said.</param>
    /// <param name="builtInSubject">What the code would have sent.</param>
    /// <param name="builtInHtml">Ditto.</param>
    public async Task<(string Subject, string Html)> ComposeAsync(
        MailKindInfo kind,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> tables,
        TimeZoneInfo zone,
        string builtInSubject,
        string builtInHtml,
        DateTime nowUtc,
        CancellationToken ct)
    {
        try
        {
            if (await LiveAsync(kind.Key, ct) is not { } live) return (builtInSubject, builtInHtml);

            var context = new MailTokens.Context(
                tables, zone, nowUtc, _site.Name, _site.AbsoluteUrl("/"));

            var subject = MailTokens.Render(live.Subject, context);
            var html = MailTokens.Render(live.Html, context);

            // A template that renders to nothing is a mistake, not an instruction. An empty letter
            // is worse than the built-in one, and worse silently.
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(html))
            {
                _logger.LogWarning(
                    "The {Kind} template rendered empty; the built-in letter was sent instead.", kind.Key);
                return (builtInSubject, builtInHtml);
            }

            return (subject, html);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Never fatal. A letter that does not go is the failure this whole area exists to
            // prevent — item 239's own reason for being.
            _logger.LogError(e, "Could not apply the {Kind} template; the built-in letter was sent.", kind.Key);
            return (builtInSubject, builtInHtml);
        }
    }

    private async Task<(string Subject, string Html)?> LiveAsync(string kindKey, CancellationToken ct)
    {
        var key = $"mail-template:{kindKey}";
        if (_cache.TryGetValue<(string, string)?>(key, out var cached)) return cached;

        await using var db = await _db.CreateDbContextAsync(ct);

        var row = await db.EmailTemplates.AsNoTracking()
            .Where(t => t.Kind == kindKey && t.PublishedUtc != null
                     && t.Subject != null && t.BodyHtml != null)
            .Select(t => new { t.Subject, t.BodyHtml })
            .FirstOrDefaultAsync(ct);

        (string, string)? live = row is null ? null : (row.Subject!, row.BodyHtml!);

        // The ABSENCE is cached too, which is the common case by far: almost every letter has no
        // template, and a miss that went to the database each time would be a query per letter.
        _cache.Set(key, live, Freshness);
        return live;
    }

    /// <summary>Forgets what is cached for one kind, so publishing is felt at once.</summary>
    public void Forget(string kindKey) => _cache.Remove($"mail-template:{kindKey}");
}
