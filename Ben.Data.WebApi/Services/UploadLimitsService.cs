using Ben.Data.Source.Context;

namespace Ben.Data.WebApi.Services;

/// <summary>The two sizes that govern uploads, read from site settings with built-in defaults.</summary>
/// <param name="MaxFileBytes">Largest file one upload may be.</param>
/// <param name="ChunkMaxBytes">Largest single chunk a chunked upload may send.</param>
public sealed record UploadLimits(long MaxFileBytes, long ChunkMaxBytes);

/// <summary>
/// Reads the upload size limits — <see cref="SiteSettingKeys.UploadMaxFileBytes"/> and
/// <see cref="SiteSettingKeys.UploadChunkMaxBytes"/> — with the defaults stated here.
/// </summary>
/// <remarks>
/// <para>The defaults follow the settings convention: stated at the read site, not seeded as
/// rows. 2 GiB is the product decision for the largest single file; 64 MiB keeps each chunk
/// comfortably under Cloudflare's 100 MB per-request ceiling, which is the whole reason chunked
/// uploads exist — see the seed descriptions in <see cref="SiteSettingKeys"/>.</para>
///
/// <para>An unparseable or non-positive stored value reads as unset, the same degrade-to-default
/// rule <see cref="SiteSettingsService.GetGuidAsync"/> documents: a bad value typed into an admin
/// box must weaken nothing.</para>
/// </remarks>
public static class UploadLimitsReader
{
    public const long DefaultMaxFileBytes  = 2L * 1024 * 1024 * 1024;   // 2 GiB
    public const long DefaultChunkMaxBytes = 64L * 1024 * 1024;         // 64 MiB

    public static async Task<UploadLimits> ReadAsync(BenDataContext db, CancellationToken ct = default)
    {
        var max   = await ReadPositiveLongAsync(db, SiteSettingKeys.UploadMaxFileBytes,  DefaultMaxFileBytes,  ct);
        var chunk = await ReadPositiveLongAsync(db, SiteSettingKeys.UploadChunkMaxBytes, DefaultChunkMaxBytes, ct);

        // A chunk larger than the whole-file limit is a configuration accident; the tighter of
        // the two governs so the pair can never contradict each other.
        return new UploadLimits(max, Math.Min(chunk, max));
    }

    private static async Task<long> ReadPositiveLongAsync(
        BenDataContext db, string key, long whenUnset, CancellationToken ct)
    {
        var raw = await SiteSettingsService.GetAsync(db, key, ct);
        return long.TryParse(raw, out var value) && value > 0 ? value : whenUnset;
    }
}
