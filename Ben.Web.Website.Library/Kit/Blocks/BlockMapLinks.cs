using System.Globalization;
using Ben.Data.Common.Blocks;

namespace Ben.Web.Website.Library.Kit.Blocks;

/// <summary>Words and links for a map block's places and route.</summary>
public static class BlockMapLinks
{
    /// <summary>
    /// "Open in Maps": the place, or directions from the first place to the last. Apple's Maps address carries one
    /// destination, so a route of several places opens as its two ends; each place has its own link too.
    /// </summary>
    public static string? OpenInMaps(IReadOnlyList<BlockMapStop> stops, string route)
    {
        if (stops.Count == 0) return null;
        string Point(BlockMapStop s) => string.Create(CultureInfo.InvariantCulture, $"{s.Latitude},{s.Longitude}");

        if (stops.Count == 1)
            return $"https://maps.apple.com/?ll={Point(stops[0])}&q={Uri.EscapeDataString(stops[0].Label ?? "Place")}";

        var flag = route == BlockMapRoutes.Driving ? "d" : "w";
        return $"https://maps.apple.com/?saddr={Point(stops[0])}&daddr={Point(stops[^1])}&dirflg={flag}";
    }

    public static string OpenPlace(BlockMapStop stop) =>
        $"https://maps.apple.com/?ll={string.Create(CultureInfo.InvariantCulture, $"{stop.Latitude},{stop.Longitude}")}&q={Uri.EscapeDataString(stop.Label ?? "Place")}";

    /// <summary>"0.4 mi · 9 min walk", "0.3 mi straight line", "12 min drive".</summary>
    public static string LegText(Maps.BenMapLeg leg, string route)
    {
        var miles = leg.DistanceMiles < 0.1
            ? $"{Math.Round(leg.DistanceMeters * 3.28084 / 10) * 10:0} ft"
            : string.Create(CultureInfo.InvariantCulture, $"{leg.DistanceMiles:0.0} mi");
        if (!leg.FollowsPaths || leg.DurationSeconds is not { } seconds)
            return $"{miles} straight line";
        return $"{miles} · {Duration(seconds)} {(route == BlockMapRoutes.Driving ? "drive" : "walk")}";
    }

    public static string Duration(double seconds)
    {
        var minutes = Math.Max(1, (int)Math.Round(seconds / 60));
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} hr {minutes % 60} min";
    }
}
