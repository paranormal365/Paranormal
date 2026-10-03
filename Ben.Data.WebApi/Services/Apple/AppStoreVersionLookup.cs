using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Data.WebApi.Services.Apple;

/// <summary>The version of the iPhone and iPad app the App Store is offering right now.</summary>
/// <param name="Version">"1.1.2" — the marketing version people see in the store.</param>
/// <param name="ReleasedUtc">When that version went live.</param>
/// <param name="ReleaseNotes">The "What's New" text, as written in App Store Connect.</param>
/// <param name="StoreUrl">The app's App Store page.</param>
/// <param name="MinimumOsVersion">The oldest iOS that version installs on, e.g. "18.0".</param>
public sealed record AppStoreRelease(
    string Version, DateTime? ReleasedUtc, string? ReleaseNotes, string StoreUrl, string? MinimumOsVersion);

/// <summary>Asks the App Store which version of the app is live.</summary>
public interface IAppStoreVersionLookup
{
    /// <summary>The live release; null when the App Store could not be reached or does not list the app.</summary>
    Task<AppStoreRelease?> LatestAsync(CancellationToken ct);
}

/// <summary>
/// Apple's public lookup service, cached (Ben, 10/03/2026: "have a check for updated version which
/// checks the webapi to see if a newer version has been released").
/// </summary>
/// <remarks>
/// <para><b>Apple's answer, not ours.</b> The version is read from
/// <c>itunes.apple.com/lookup</c> by bundle id rather than kept in a setting somebody updates after
/// each release. A setting would be right only when somebody remembered it; this is right the
/// moment Apple releases a version, including one released while nobody was looking.</para>
///
/// <para><b>Cached for an hour</b>, because every phone that opens Profile asks, and Apple's lookup
/// is a public service with rate limits of its own. A failure is cached for two minutes only, so a
/// moment's outage does not hide a release for an hour.</para>
///
/// <para>No key, no account and nothing personal goes to Apple: the request names only the app's
/// bundle id and the storefront country.</para>
/// </remarks>
public sealed class AppStoreVersionLookup : IAppStoreVersionLookup
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailedFresh = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AppStoreVersionLookup> _logger;
    private readonly string _bundleId;
    private readonly string _country;

    public AppStoreVersionLookup(
        HttpClient http, IMemoryCache cache, IConfiguration config, ILogger<AppStoreVersionLookup> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _bundleId = config["AppStore:BundleId"] is { Length: > 0 } b ? b : "com.ishaunted.ios";
        _country = config["AppStore:Country"] is { Length: > 0 } c ? c : "us";
    }

    public async Task<AppStoreRelease?> LatestAsync(CancellationToken ct)
    {
        var key = $"app-store-release:{_bundleId}:{_country}";
        if (_cache.TryGetValue<AppStoreRelease?>(key, out var cached)) return cached;

        AppStoreRelease? release = null;
        try
        {
            var url = $"https://itunes.apple.com/lookup?bundleId={Uri.EscapeDataString(_bundleId)}&country={Uri.EscapeDataString(_country)}";
            using var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
                release = Parse(await response.Content.ReadAsStringAsync(ct), _bundleId);
            else
                _logger.LogWarning("App Store lookup answered {Status} for {BundleId}.", (int)response.StatusCode, _bundleId);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(e, "App Store lookup failed for {BundleId}.", _bundleId);
        }

        _cache.Set(key, release, release is null ? FailedFresh : Fresh);
        return release;
    }

    /// <summary>
    /// The release in one lookup answer, or null when the answer does not name this app.
    /// </summary>
    /// <remarks>
    /// Matched on the bundle id as well as taken from the first result: the lookup can answer
    /// with an empty list (an app not yet released in this storefront) and must never be read as
    /// some other app's version.
    /// </remarks>
    public static AppStoreRelease? Parse(string json, string bundleId)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var app in results.EnumerateArray())
        {
            if (!string.Equals(Text(app, "bundleId"), bundleId, StringComparison.OrdinalIgnoreCase)) continue;
            if (Text(app, "version") is not { Length: > 0 } version || Text(app, "trackViewUrl") is not { Length: > 0 } url)
                return null;

            DateTime? released = DateTime.TryParse(Text(app, "currentVersionReleaseDate"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var when) ? when : null;

            return new AppStoreRelease(version, released, Text(app, "releaseNotes"), url, Text(app, "minimumOsVersion"));
        }
        return null;
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
