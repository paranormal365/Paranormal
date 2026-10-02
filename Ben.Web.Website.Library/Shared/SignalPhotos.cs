namespace Ben.Web.Website.Library.Shared;

/// <summary>
/// The site's licensed photographs (wwwroot/static/images/signal) and where each one's subject sits.
/// </summary>
/// <remarks>
/// <para>One list for every surface that lays one of them behind text — the section banners and
/// every page hero — so a photograph is framed the same way wherever it appears. It used to live
/// inside SectionBanner; a second component wanting it was the point at which a copy would have
/// started to drift.</para>
/// <para>Only the vertical number matters. A landscape photograph in a band this wide is scaled
/// to the band's WIDTH and cropped top-to-bottom, so a horizontal position does nothing. Chosen by
/// looking at every one cropped to the band's real proportions, not by guessing: the graveyard at
/// 45% was nearly all fog — its headstones are at 70%.</para>
/// <para>Unsplash License (free for commercial use, no attribution required), recorded in
/// ProjectNotes/FeatureHistory/README-hosted-events-235-media.md.</para>
/// </remarks>
public static class SignalPhotos
{
    private static readonly Dictionary<string, string> Focal = new()
    {
        ["i1-flashlight-silhouette"] = "40%",   // head, shoulders and the beam
        ["i2-dark-hallway"]          = "52%",   // the lit doorway, centred
        ["i3-walking-to-house"]      = "48%",   // the lit windows
        ["p1-phone-camera"]          = "40%",   // the phone, whole
        ["s1-candles"]               = "28%",   // the densest candlelight
        ["s2-ouija"]                 = "48%",   // the word OUIJA and the planchette
        ["s3-candle-chandelier"]     = "35%",   // the candles, not the curtain below
        ["t1-theatre-seats"]         = "45%",
        ["v1-venue-exterior"]        = "38%",   // the turrets and the lit facade
        ["v3-hotel-lobby"]           = "40%",   // the room's depth and its chandeliers
        ["v6-ballroom"]              = "35%",   // the chandeliers fill the band
        ["w2-cobblestone-night"]     = "50%",   // the lamp-lit doorway down the alley
        ["w3-foggy-graveyard"]       = "70%",   // the headstones, not the fog above them
        ["d1-candlelit-dinner"]      = "60%",   // faces and candles together
    };

    /// <summary>The vertical focus for a photograph, as a CSS percentage.</summary>
    public static string Focus(string photo) => Focal.TryGetValue(photo, out var y) ? y : "50%";

    /// <summary>The web address of a photograph by its key.</summary>
    public static string Url(string photo) => $"/static/images/signal/{photo}.jpg";

    /// <summary>A ready-made inline style laying the photograph in, framed on its subject.</summary>
    public static string BackgroundStyle(string photo)
        => $"background-image:url('{Url(photo)}');background-position:center {Focus(photo)}";
}
