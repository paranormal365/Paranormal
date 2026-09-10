namespace Ben.Web.Website.Library.Kit.Maps;

/// <summary>Which library draws the site's maps.</summary>
public enum MapProvider
{
    /// <summary>Telerik's map over OpenStreetMap's public tile server. The path being retired (item 228).</summary>
    Telerik,

    /// <summary>Apple MapKit JS, authorised by a token the website signs.</summary>
    Apple,
}

/// <summary>
/// What every map component needs to know about the provider, resolved once at startup.
/// </summary>
/// <remarks>
/// <para>The switch exists so that item 228 can move one component at a time: a map that has
/// been rebuilt on MapKit JS falls back to the Telerik path when the provider says so, and the
/// old path is deleted only when the last component has crossed. It is a startup setting, not a
/// site setting — a map provider is a deployment decision, and a wrong one shows on every page.</para>
///
/// <para><see cref="Apple"/> is honoured only when the website can sign a token; an
/// unconfigured key silently means Telerik, because a map that cannot initialise is worse than
/// the old map.</para>
/// </remarks>
public sealed record MapsOptions(MapProvider Provider, bool AppleConfigured)
{
    /// <summary>The path MapKit JS fetches its token from, same origin as the page.</summary>
    public const string TokenPath = "/auth/mapkit-token";

    /// <summary>The provider components should actually use.</summary>
    public MapProvider Effective =>
        Provider == MapProvider.Apple && AppleConfigured ? MapProvider.Apple : MapProvider.Telerik;
}
