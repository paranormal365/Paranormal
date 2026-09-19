using System.Security.Cryptography;
using System.Text;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Data.WebApi.Services.LinkPreviews;

/// <summary>
/// The preview card for a link somebody pasted or posted: fetched once, kept, and served from what was kept.
/// </summary>
/// <remarks>
/// <para>Beta feedback, 2026-09-14 — Ben: a pasted link should build a card like one pasted on X. Shared by research
/// pages, group messages, case messages and the feed, and keyed by the address, so a link posted in three places is
/// fetched once.</para>
/// <para><b>Who can make it fetch:</b> only a signed-in person, only by posting or pasting, at most
/// <see cref="LinkPreviewService.FetchesPerMinute"/> times a minute each. An anonymous reader of a card never causes a fetch. Our own
/// addresses are never fetched — the public link-preview endpoint describes those from our records.</para>
/// <para><b>What is kept:</b> the title, description and site name, and the page's picture copied small onto our own
/// storage (<see cref="LinkPreviewService.ThumbnailRelativeUrl(Guid)"/>) so that no reader's browser loads a stranger's image and so a card
/// still has its picture when the other site moves it. A page that could not be read leaves a row marked unfetched,
/// so the card shows the host and the same broken link is not tried again for a week.</para>
/// </remarks>
public interface ILinkPreviewService
{
    /// <summary>The kept preview for an address, without fetching.</summary>
    Task<StoredLinkPreview?> FindAsync(string url, CancellationToken ct);

    /// <summary>The kept preview while it is fresh, otherwise fetched now within the caller's budget.</summary>
    Task<StoredLinkPreview?> GetOrFetchAsync(string url, Guid userId, bool refresh, CancellationToken ct);
}

public class LinkPreviewService : ILinkPreviewService
{
    public const int FetchesPerMinute = 30;
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(7);
    public const int ThumbnailLongEdge = 600;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly OpenGraphFetcher _fetcher;
    private readonly IFileStorageService _storage;
    private readonly IMediaSanitizationService _images;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LinkPreviewService> _log;
    private readonly TimeProvider _clock;

    public LinkPreviewService(IDbContextFactory<BenDataContext> db, OpenGraphFetcher fetcher, IFileStorageService storage,
        IMediaSanitizationService images, IMemoryCache cache, IConfiguration configuration, ILogger<LinkPreviewService> log,
        TimeProvider? clock = null)
    {
        _db = db; _fetcher = fetcher; _storage = storage; _images = images; _cache = cache;
        _configuration = configuration; _log = log; _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Where a kept picture is served from, on the website: stable and un-ticketed, safe to store in a page.</summary>
    public static string ThumbnailRelativeUrl(Guid previewId) => $"/media/link-preview/{previewId}";

    public static string StoragePathFor(Guid previewId) => $"link-previews/{previewId}.jpg";

    /// <summary>The address as it is looked up: scheme and host lower-cased, no fragment.</summary>
    public static string? Normalise(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var builder = new UriBuilder(uri) { Fragment = "", Scheme = uri.Scheme.ToLowerInvariant(), Host = uri.Host.ToLowerInvariant() };
        if (uri.IsDefaultPort) builder.Port = -1;
        var normalised = builder.Uri.AbsoluteUri;
        return normalised.Length <= 2000 ? normalised : null;
    }

    public static string HashOf(string normalisedUrl) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalisedUrl))).ToLowerInvariant();

    /// <summary>The kept preview for an address, without fetching. For anonymous readers.</summary>
    public async Task<StoredLinkPreview?> FindAsync(string url, CancellationToken ct)
    {
        if (Normalise(url) is not { } normalised) return null;
        var hash = HashOf(normalised);
        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.LinkPreviews.AsNoTracking().FirstOrDefaultAsync(p => p.UrlHash == hash, ct);
    }

    /// <summary>
    /// The preview for <paramref name="url"/>: the kept one while it is fresh, otherwise fetched now. Null for an
    /// address that is not a web address, is one of ours, or when <paramref name="userId"/> is over the fetch budget
    /// with nothing kept.
    /// </summary>
    public async Task<StoredLinkPreview?> GetOrFetchAsync(string url, Guid userId, bool refresh, CancellationToken ct)
    {
        if (Normalise(url) is not { } normalised) return null;
        var target = new Uri(normalised);
        if (IsOurs(target)) return null;

        var hash = HashOf(normalised);
        await using var db = await _db.CreateDbContextAsync(ct);
        var kept = await db.LinkPreviews.FirstOrDefaultAsync(p => p.UrlHash == hash, ct);
        if (kept is not null && !refresh && kept.ExpiresUtc > _clock.GetUtcNow().UtcDateTime) return kept;

        if (!TakeFetchBudget(userId)) return kept;

        var row = kept ?? new StoredLinkPreview { Id = Guid.NewGuid(), Url = normalised, UrlHash = hash, Domain = target.Host };
        var now = _clock.GetUtcNow().UtcDateTime;
        row.FetchedUtc = now;
        row.ExpiresUtc = now.Add(KeptFor);
        row.FetchedByAppUserId = userId;

        try
        {
            var page = await _fetcher.FetchAsync(target, ct);
            row.Fetched = true;
            row.FailureReason = null;
            row.Title = page.Title;
            row.Description = page.Description;
            row.SiteName = page.SiteName;
            row.Domain = page.FinalUrl.Host;
            await StoreThumbnailAsync(row, page.ImageUrl, ct);
        }
        catch (PageFetchRefusedException ex)
        {
            row.Fetched = kept?.Fetched ?? false;
            row.FailureReason = Cap(ex.Message, 300);
            _log.LogInformation("Link preview for {Host} not made: {Reason}", target.Host, ex.Message);
        }

        if (kept is null) db.LinkPreviews.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (kept is null)
        {
            // Somebody else posted the same link at the same moment and their row landed first. Theirs is as good.
            await using var again = await _db.CreateDbContextAsync(ct);
            return await again.LinkPreviews.AsNoTracking().FirstOrDefaultAsync(p => p.UrlHash == hash, ct);
        }
        return row;
    }

    private async Task StoreThumbnailAsync(StoredLinkPreview row, Uri? imageUrl, CancellationToken ct)
    {
        if (imageUrl is null) return;
        try
        {
            var (bytes, contentType) = await _fetcher.FetchImageAsync(imageUrl, ct);
            if (!_images.CanSanitize(contentType)) return;
            // Re-encoded small: also what strips anything the original carried besides the picture.
            var jpeg = _images.Sanitize(bytes, ThumbnailLongEdge);
            var path = StoragePathFor(row.Id);
            await using var stream = new MemoryStream(jpeg);
            await _storage.WriteAsync(path, stream, ct);
            row.ThumbnailStoragePath = path;
            row.ThumbnailContentType = "image/jpeg";
        }
        catch (PageFetchRefusedException ex)
        {
            _log.LogInformation("Link preview picture for {Host} not copied: {Reason}", row.Domain, ex.Message);
        }
        catch (UnreadableImageException ex)
        {
            _log.LogInformation("Link preview picture for {Host} not copied: {Reason}", row.Domain, ex.Message);
        }
    }

    /// <summary>At most <see cref="FetchesPerMinute"/> fetches a minute for one person.</summary>
    private bool TakeFetchBudget(Guid userId)
    {
        var key = $"link-preview-budget:{userId:N}:{_clock.GetUtcNow():yyyyMMddHHmm}";
        var used = _cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2); return new int[1]; })!;
        return Interlocked.Increment(ref used[0]) <= FetchesPerMinute;
    }

    private bool IsOurs(Uri uri) =>
        Uri.TryCreate(_configuration["AppBaseUrl"], UriKind.Absolute, out var site)
        && string.Equals(uri.Host, site.Host, StringComparison.OrdinalIgnoreCase);

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];
}
