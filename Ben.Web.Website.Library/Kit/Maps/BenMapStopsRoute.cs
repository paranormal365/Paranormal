namespace Ben.Web.Website.Library.Kit.Maps;

/// <summary>How a route through several stops is drawn (2026-09-14).</summary>
public enum BenMapRouteMode
{
    /// <summary>Straight lines from stop to stop — works where there are no paths, like across a cemetery.</summary>
    Straight,

    /// <summary>Walking directions from the map provider, leg by leg.</summary>
    Walking,

    /// <summary>Driving directions from the map provider, leg by leg.</summary>
    Driving,
}

/// <summary>One leg of a route, from one stop to the next.</summary>
/// <param name="DurationSeconds">Travel time for a walking or driving leg; null for a straight line.</param>
/// <param name="FollowsPaths">False when the leg is a straight line, chosen or fallen back to.</param>
/// <param name="Note">Why a walking or driving leg is a straight line instead, in words for the reader.</param>
public sealed record BenMapLeg(
    int FromIndex,
    int ToIndex,
    double DistanceMeters,
    double? DurationSeconds,
    bool FollowsPaths,
    string? Note = null)
{
    public double DistanceMiles => DistanceMeters / 1609.344;
}

/// <summary>What a drawn route came to.</summary>
/// <param name="Error">Why nothing could be drawn at all; null when the route is on the map.</param>
public sealed record BenMapRouteSummary(IReadOnlyList<BenMapLeg> Legs, string? Error = null)
{
    public double TotalMeters => Legs.Sum(l => l.DistanceMeters);

    /// <summary>Total travel time, only when every leg has one.</summary>
    public double? TotalSeconds => Legs.Count > 0 && Legs.All(l => l.DurationSeconds is not null)
        ? Legs.Sum(l => l.DurationSeconds!.Value)
        : null;

    public static BenMapRouteSummary Failed(string error) => new([], error);
}

/// <summary>Distance over the Earth's surface.</summary>
public static class GreatCircle
{
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>Haversine distance between two points, in metres.</summary>
    public static double Meters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>The straight legs through <paramref name="stops"/>, in order.</summary>
    public static IReadOnlyList<BenMapLeg> StraightLegs(IReadOnlyList<BenMapPoint> stops) =>
        Enumerable.Range(0, Math.Max(0, stops.Count - 1))
            .Select(i => new BenMapLeg(i, i + 1,
                Meters(stops[i].Latitude, stops[i].Longitude, stops[i + 1].Latitude, stops[i + 1].Longitude),
                DurationSeconds: null, FollowsPaths: false))
            .ToList();
}
