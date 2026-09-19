using System.Security.Cryptography;
using System.Text;
using Ben.Service.Models.Support;

namespace Ben.Data.WebApi.Services.LinkUnfurl;

/// <summary>How an unfurl request ended.</summary>
public enum LinkUnfurlStatus
{
    /// <summary>The page answered and said something about itself.</summary>
    Found,

    /// <summary>The page could not be read, answered an error, or its name resolved privately.</summary>
    NotFound,

    /// <summary>The address itself is not one this server will fetch (shape, scheme, port, IP literal).</summary>
    Refused,
}

/// <summary>An unfurl's answer: a record when found, a sentence when refused.</summary>
public sealed record LinkUnfurlOutcome(LinkUnfurlStatus Status, LinkUnfurlRecord? Record, string? Refusal);

/// <summary>
/// Turns a pasted https link into a preview card, fetching each page at most once a week.
/// </summary>
/// <remarks>
/// <para><b>The cache is the rate limit that protects other people.</b> The endpoint's per-person
/// limit stops one caller hammering us; this stops everybody together hammering a stranger. A
/// working link is kept 7 days, a broken one 1 day — so a board with forty link cards, opened by a
/// whole team every day, costs each linked site one request a week.</para>
///
/// <para><b>What is stored.</b> The row is keyed by SHA-256 of the whole normalised URL (query
/// included, because two queries can be two pages), but the <c>Url</c> column keeps only scheme,
/// host and path: a pasted link can carry a signature or a one-time code, and a cache table is read
/// by people debugging it, backed up nightly, and kept for a week. The caller gets their own link
/// back in the record; only the database forgets the query. (Canvas plan review R21.)</para>
///
/// <para><b>No background job.</b> Rows expired for more than a day are swept whenever a row is
/// written, which is exactly as often as the table grows.</para>
/// </remarks>
public sealed class LinkUnfurlService
{
    /// <summary>The largest page read, in bytes.</summary>
    public const long MaxPageBytes = 1_048_576;

    /// <summary>How long a working link's answer is kept.</summary>
    public static readonly TimeSpan FoundLifetime = TimeSpan.FromDays(7);

    /// <summary>How long a broken link's answer is kept.</summary>
    public static readonly TimeSpan FailedLifetime = TimeSpan.FromDays(1);

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly ISafeUrlFetcher _fetcher;
    private readonly TimeProvider _time;

    public LinkUnfurlService(IDbContextFactory<BenDataContext> db, ISafeUrlFetcher fetcher, TimeProvider time)
    {
        _db = db;
        _fetcher = fetcher;
        _time = time;
    }

    /// <summary>The preview for <paramref name="rawUrl"/>, from the cache or one guarded fetch.</summary>
    public async Task<LinkUnfurlOutcome> GetAsync(string rawUrl, CancellationToken ct)
    {
        if (Normalise(rawUrl) is not { } url)
            return new LinkUnfurlOutcome(LinkUnfurlStatus.Refused, null, "Only a full https address can be previewed.");
        if (SafeUrlPolicy.Refuse(url) is { } refusal)
            return new LinkUnfurlOutcome(LinkUnfurlStatus.Refused, null, refusal);

        var now = _time.GetUtcNow().UtcDateTime;
        var hash = HashOf(url);

        await using (var read = await _db.CreateDbContextAsync(ct))
        {
            var cached = await read.LinkUnfurlCache.AsNoTracking()
                .FirstOrDefaultAsync(r => r.UrlHash == hash && r.ExpiresAtUtc > now, ct);
            if (cached is not null) return ToOutcome(url, cached);
        }

        var fetched = await _fetcher.FetchAsync(url, "text/html", MaxPageBytes, ct);

        var row = new LinkUnfurlCache
        {
            Id = Guid.NewGuid(),
            UrlHash = hash,
            Url = StoredUrlOf(url),
            StatusCode = fetched.Refusal is null ? fetched.StatusCode : 0,
            FetchedAtUtc = now,
        };

        if (fetched.Refusal is null && fetched.StatusCode == 200 && fetched.Body is { } body)
        {
            var data = OpenGraphParser.Parse(Encoding.UTF8.GetString(body), fetched.FinalUrl);
            row.StatusCode = 200;
            row.Title = data.Title;
            row.Description = data.Description;
            row.ImageSourceUrl = data.ImageSourceUrl;
            row.SiteName = data.SiteName;
            row.ExpiresAtUtc = now + FoundLifetime;
        }
        else
        {
            if (row.StatusCode == 200) row.StatusCode = 0;   // a 200 we refused to read is not a success
            row.ExpiresAtUtc = now + FailedLifetime;
        }

        await SaveAsync(row, now, ct);
        return ToOutcome(url, row);
    }

    /// <summary>
    /// Trimmed, absolute, fragment removed; scheme and host lower-cased by <see cref="Uri"/> itself.
    /// Null when it is not an absolute URL at all.
    /// </summary>
    public static Uri? Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var url)) return null;
        return string.IsNullOrEmpty(url.Fragment) ? url : new UriBuilder(url) { Fragment = string.Empty }.Uri;
    }

    /// <summary>Lower-case hex SHA-256 of the normalised URL, query included.</summary>
    public static string HashOf(Uri url)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)));

    /// <summary>The URL as the cache table keeps it: scheme, host and path, no query, no fragment.</summary>
    public static string StoredUrlOf(Uri url)
    {
        var stored = url.GetLeftPart(UriPartial.Path);
        return stored.Length <= SafeUrlPolicy.MaxUrlLength ? stored : stored[..SafeUrlPolicy.MaxUrlLength];
    }

    private static LinkUnfurlOutcome ToOutcome(Uri url, LinkUnfurlCache row)
        => row.StatusCode == 200
            ? new LinkUnfurlOutcome(LinkUnfurlStatus.Found,
                new LinkUnfurlRecord(url.AbsoluteUri, url.Host, row.Title, row.Description, row.ImageSourceUrl, row.SiteName),
                null)
            : new LinkUnfurlOutcome(LinkUnfurlStatus.NotFound, null, null);

    /// <summary>Upserts by hash; a racing insert of the same link is absorbed by updating the winner.</summary>
    private async Task SaveAsync(LinkUnfurlCache row, DateTime now, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = await _db.CreateDbContextAsync(ct);
            var existing = await db.LinkUnfurlCache.FirstOrDefaultAsync(r => r.UrlHash == row.UrlHash, ct);
            if (existing is null)
            {
                db.LinkUnfurlCache.Add(row);
            }
            else
            {
                existing.Url = row.Url;
                existing.StatusCode = row.StatusCode;
                existing.Title = row.Title;
                existing.Description = row.Description;
                existing.ImageSourceUrl = row.ImageSourceUrl;
                existing.SiteName = row.SiteName;
                existing.FetchedAtUtc = row.FetchedAtUtc;
                existing.ExpiresAtUtc = row.ExpiresAtUtc;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                continue;   // another request inserted the same hash first; update theirs
            }

            var staleBefore = now - FailedLifetime;
            await db.LinkUnfurlCache.Where(r => r.ExpiresAtUtc < staleBefore).ExecuteDeleteAsync(ct);
            return;
        }
    }
}
