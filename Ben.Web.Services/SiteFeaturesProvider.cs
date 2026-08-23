using Ben.Service.Models.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ben.Web.Services;

/// <summary>
/// Which sections of the site are switched on, as the website sees them.
/// </summary>
/// <remarks>
/// <para>Reads are synchronous and never block, because the callers are the navigation menu and
/// every gated page's first render — a place an <c>await</c> on an HTTP call would be paid on
/// every page view, by every visitor. Values come from a snapshot refreshed behind whoever
/// notices it has gone stale, the same shape as the API's <c>RateLimitSettingsProvider</c>.</para>
///
/// <para>Singleton, not scoped: feature switches are a property of the site, not of the person
/// looking at it. That also means one refresh serves every circuit rather than one per visitor.
/// It follows that nothing user-specific may ever be added to this snapshot.</para>
///
/// <para><b>What happens when the API is unreachable</b> decides how the site fails, so it is
/// chosen rather than inherited: the first snapshot is each feature's declared default —
/// established sections on, unbuilt features off. A website that cannot reach the API therefore
/// looks normal instead of appearing to have lost half its features, and an unfinished feature
/// still cannot appear by accident. After a successful read, a later failure keeps the last good
/// answer for the same reason.</para>
/// </remarks>
public sealed class SiteFeaturesProvider
{
    /// <summary>How long a snapshot is used before a refresh starts behind the next reader.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The defaults the website falls back to. Duplicated from the API's declaration rather than
    /// shared, because the website cannot reference the API project — a guard test asserts the two
    /// lists agree, so the duplication cannot drift silently.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, bool> Defaults =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [SiteFeatures.VideoEditor]  = true,
            [SiteFeatures.Equipment]    = true,
            [SiteFeatures.Events]       = true,
            [SiteFeatures.Discovery]    = true,
            [SiteFeatures.CmsPages]     = true,
            [SiteFeatures.MediaLibrary] = true,
            [SiteFeatures.OrgMessaging] = true,
            [SiteFeatures.Voting]       = true,
            [SiteFeatures.PublicFeed]   = false,
            [SiteFeatures.Publications] = false,
        };

    // A scope factory rather than the client itself: this is a singleton and IBenAdminClient is
    // scoped, so holding one would capture the first circuit's client for the lifetime of the
    // process — the classic captive dependency, which the container refuses outright when scope
    // validation is on. Each refresh opens its own scope instead.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SiteFeaturesProvider> _logger;

    private volatile IReadOnlyDictionary<string, bool> _snapshot = Defaults;
    private volatile string? _announcement;
    private volatile bool _allowOrgSelfRegistration = true;
    private long _nextRefreshTicks;
    private int _refreshing;

    public SiteFeaturesProvider(IServiceScopeFactory scopeFactory, ILogger<SiteFeaturesProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _nextRefreshTicks = DateTime.UtcNow.Ticks;   // refresh on first use
    }

    /// <summary>Whether a section is on. Unknown keys read as off.</summary>
    public bool IsOn(string featureKey)
    {
        EnsureFresh();
        return _snapshot.TryGetValue(featureKey, out var on) && on;
    }

    /// <summary>The site-wide announcement, or null when none is set. Same snapshot, same
    /// freshness rules as the feature flags; when the API is unreachable it starts as nothing
    /// rather than a stale warning.</summary>
    public string? Announcement
    {
        get { EnsureFresh(); return _announcement; }
    }

    /// <summary>Whether an ordinary signed-in user may found a group. True until told otherwise,
    /// so an unreachable API leaves the product working the way it always has.</summary>
    public bool AllowOrganizationSelfRegistration
    {
        get { EnsureFresh(); return _allowOrgSelfRegistration; }
    }

    /// <summary>
    /// Forces the next read to refetch. Called after a SuperAdmin saves a setting so they see
    /// their own change immediately rather than up to <see cref="RefreshInterval"/> later.
    /// </summary>
    public void Invalidate() => Interlocked.Exchange(ref _nextRefreshTicks, DateTime.UtcNow.Ticks);

    /// <summary>Fetches now and waits for the answer. For startup and for tests.</summary>
    public async Task PrimeAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        Interlocked.Exchange(ref _nextRefreshTicks, DateTime.UtcNow.Add(RefreshInterval).Ticks);
    }

    private void EnsureFresh()
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _nextRefreshTicks)) return;

        // One refresh at a time; everyone else keeps reading the snapshot already in hand.
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;

        Interlocked.Exchange(ref _nextRefreshTicks, DateTime.UtcNow.Add(RefreshInterval).Ticks);
        _ = Task.Run(() => RefreshAsync(CancellationToken.None));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IBenAdminClient>();
            var info = await client.GetSiteFeaturesAsync(ct);

            // An empty or absent answer is not an instruction to switch the site off. Only a
            // response that actually names features replaces the snapshot.
            if (info is { Features.Count: > 0 })
            {
                _snapshot = new Dictionary<string, bool>(info.Features, StringComparer.Ordinal);
                // Inside the same validity check: only a response that names features may also
                // set OR CLEAR the announcement, so a failed fetch cannot wipe a live notice.
                _announcement = string.IsNullOrWhiteSpace(info.Announcement) ? null : info.Announcement;
                _allowOrgSelfRegistration = info.AllowOrganizationSelfRegistration;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not refresh site features; keeping the previous values.");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }
}

/// <summary>
/// The feature keys, as the website names them.
/// </summary>
/// <remarks>
/// These strings are the wire contract with the API's <c>SiteSettingKeys</c>. They are repeated
/// here because the Blazor library cannot reference the API project; a test compares the two sets
/// so a key renamed on one side fails the build rather than silently gating nothing.
/// </remarks>
public static class SiteFeatures
{
    public const string VideoEditor  = "features.video-editor";
    public const string Equipment    = "features.equipment";
    public const string Events       = "features.events";
    public const string Discovery    = "features.discovery";
    public const string CmsPages     = "features.cms-pages";
    public const string MediaLibrary = "features.media-library";
    public const string OrgMessaging = "features.org-messaging";
    public const string Voting       = "features.voting";
    public const string PublicFeed   = "features.public-feed";
    public const string Publications = "features.publications";

    /// <summary>Every declared key, for the guard test and the admin help text.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        VideoEditor, Equipment, Events, Discovery, CmsPages,
        MediaLibrary, OrgMessaging, Voting, PublicFeed, Publications,
    ];
}
