namespace Ben.Data.Common.Enums;

/// <summary>
/// What each <see cref="SocialPlatform"/> is called, and where it is allowed to point.
/// </summary>
/// <remarks>
/// One list, used by the server that validates a link and by the screen that offers the choices,
/// so what a business is offered and what actually saves cannot drift apart.
/// </remarks>
public static class SocialPlatforms
{
    /// <summary>Every platform in the order a business is offered them.</summary>
    public static readonly IReadOnlyList<SocialPlatform> All =
    [
        SocialPlatform.Website,
        SocialPlatform.Instagram,
        SocialPlatform.Facebook,
        SocialPlatform.X,
        SocialPlatform.TikTok,
        SocialPlatform.YouTube,
        SocialPlatform.BlueSky,
        SocialPlatform.Rumble,
        SocialPlatform.Threads,
    ];

    /// <summary>What to call it on screen.</summary>
    public static string DisplayName(SocialPlatform platform) => platform switch
    {
        SocialPlatform.Website => "Website",
        SocialPlatform.Instagram => "Instagram",
        SocialPlatform.Facebook => "Facebook",
        SocialPlatform.X => "X",
        SocialPlatform.TikTok => "TikTok",
        SocialPlatform.YouTube => "YouTube",
        SocialPlatform.BlueSky => "Bluesky",
        SocialPlatform.Rumble => "Rumble",
        SocialPlatform.Threads => "Threads",
        _ => platform.ToString(),
    };

    /// <summary>
    /// The hosts a link may point at, or an empty list for <see cref="SocialPlatform.Website"/>,
    /// which may point anywhere.
    /// </summary>
    /// <remarks>
    /// Sub-domains of these are accepted too (<c>m.facebook.com</c>, <c>www.youtube.com</c>), and
    /// nothing else is. The short forms are here because they are what people copy off a phone.
    /// </remarks>
    public static IReadOnlyList<string> HostsFor(SocialPlatform platform) => platform switch
    {
        SocialPlatform.Website => [],
        SocialPlatform.Instagram => ["instagram.com", "instagr.am"],
        SocialPlatform.Facebook => ["facebook.com", "fb.com", "fb.me"],
        SocialPlatform.X => ["x.com", "twitter.com"],
        SocialPlatform.TikTok => ["tiktok.com"],
        SocialPlatform.YouTube => ["youtube.com", "youtu.be"],
        SocialPlatform.BlueSky => ["bsky.app", "bsky.social"],
        SocialPlatform.Rumble => ["rumble.com"],
        SocialPlatform.Threads => ["threads.net", "threads.com"],
        _ => [],
    };

    /// <summary>
    /// Whether <paramref name="url"/> is something this platform may be linked to.
    /// </summary>
    /// <remarks>
    /// <para>Absolute http or https only. A relative link would resolve against our own site, and
    /// a <c>javascript:</c> or <c>data:</c> URL on a page anyone can publish to is the oldest
    /// trick there is.</para>
    ///
    /// <para>Then the host, unless the platform is their own website. An icon that says Instagram
    /// and opens somewhere else is a link-laundering trick, and the whole point of the icon is
    /// that a reader trusts it without reading the address.</para>
    /// </remarks>
    public static bool IsAllowed(SocialPlatform platform, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var hosts = HostsFor(platform);
        if (hosts.Count == 0) return true;

        var host = uri.Host.ToLowerInvariant();
        return hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }
}
