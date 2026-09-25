using Ben.Data.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>Reads session bundles out of storage, and serves what is inside them.</summary>
public interface IBenBundleStore
{
    /// <summary>
    /// Indexes the bundle at <paramref name="storagePath"/>. Cached: the index is a few hundred
    /// bytes and never changes, while the file it describes may be gigabytes and is read once per
    /// player scrub without it.
    /// </summary>
    Task<BenBundle> IndexAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// One member of a bundle, as a seekable stream of exactly its bytes. Null when the bundle
    /// does not carry that entry.
    /// </summary>
    Task<BundleWindowStream?> OpenEntryAsync(
        string storagePath, string entryPath, CancellationToken ct = default);

    /// <summary>The whole bundle, for handing back to a phone.</summary>
    Task<Stream> OpenBundleAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Forgets a cached index — after a bundle at the same path is replaced.</summary>
    void Forget(string storagePath);
}

/// <inheritdoc />
public sealed class BenBundleStore(IFileStorageService storage, IMemoryCache cache) : IBenBundleStore
{
    /// <summary>
    /// Long enough that scrubbing a recording does not re-read the directory on every request,
    /// short enough that a bundle deleted and replaced is not described by a ghost for a day.
    /// </summary>
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(30);

    public async Task<BenBundle> IndexAsync(string storagePath, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(storagePath), out BenBundle? cached) && cached is not null)
            return cached;

        await using var stream = await storage.OpenReadAsync(storagePath, ct);
        var bundle = await BenBundle.ReadAsync(stream, ct);

        cache.Set(Key(storagePath), bundle, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = IndexLifetime,
            // A few hundred bytes each, and bounded by how many sessions are being watched at
            // once — but given a size so one busy day cannot crowd out the rest of the cache.
            Size = 1,
        });
        return bundle;
    }

    public async Task<BundleWindowStream?> OpenEntryAsync(
        string storagePath, string entryPath, CancellationToken ct = default)
    {
        var bundle = await IndexAsync(storagePath, ct);
        if (bundle.Find(entryPath) is not { } entry) return null;

        // A second handle rather than the one the index used: this one lives as long as the
        // response that streams from it.
        var stream = await storage.OpenReadAsync(storagePath, ct);
        return new BundleWindowStream(stream, entry.Offset, entry.Length);
    }

    public Task<Stream> OpenBundleAsync(string storagePath, CancellationToken ct = default)
        => storage.OpenReadAsync(storagePath, ct);

    public void Forget(string storagePath) => cache.Remove(Key(storagePath));

    private static string Key(string storagePath) => $"ben-bundle:{storagePath}";
}
