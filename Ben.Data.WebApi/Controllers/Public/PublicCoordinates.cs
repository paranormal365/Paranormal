namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The one place that decides how precisely a case's location may be published.
/// </summary>
/// <remarks>
/// <para>A case's coordinates are somebody's home. Publishing them exactly turns a case page into
/// an address lookup, which is the opposite of what a client agreed to when they asked a group for
/// help.</para>
///
/// <para><b>This existed as a promise before it existed as code.</b> The public discovery payload
/// has carried fields named <c>ApproxLatitude</c>/<c>ApproxLongitude</c> since it was written, and
/// passed the exact stored values straight into them. The names described an approximation nothing
/// performed. Found while scoping backlog item #80 and fixed on its own, because it was a live
/// exposure rather than a design question.</para>
///
/// <para><b>Snapped to a grid, not jittered.</b> Random offsets look safer and are worse: a
/// different offset on every request lets anyone average many responses back to the true point.
/// Snapping is deterministic, so repeat requests are identical and there is nothing to average.
/// Every case inside one cell reports the same coordinate, which also means a marker cannot be
/// told apart from its neighbours.</para>
///
/// <para><b>The cell centre is published, not the true point.</b> A circle drawn around the exact
/// location still gives it away — the centre is the answer. Here the centre is a grid intersection
/// that has nothing to do with the property, and the true location is somewhere in the surrounding
/// cell. Any circle a client draws must be at least <see cref="RadiusMiles"/> across for that to
/// stay true.</para>
/// </remarks>
internal static class PublicCoordinates
{
    /// <summary>
    /// Cell height in degrees of latitude. One degree of latitude is ~69 miles everywhere, so this
    /// is a little under 7 miles — comfortably past Ben's "about five miles".
    /// </summary>
    private const decimal LatitudeStep = 0.1m;

    /// <summary>
    /// The radius a client must draw for its circle to honestly contain the true point: half the
    /// cell's diagonal, rounded up.
    /// </summary>
    internal const double RadiusMiles = 6.0;

    /// <summary>
    /// The published position for a stored coordinate pair, or nulls when there is nothing to
    /// publish.
    /// </summary>
    /// <remarks>
    /// Longitude uses a step scaled by 1/cos(latitude) so a cell stays roughly as wide on the
    /// ground as it is tall. A fixed longitude step would shrink towards the poles — at 60°N a
    /// tenth of a degree is barely three miles — and the obfuscation would quietly weaken the
    /// further north a case was, which is exactly the kind of failure nobody notices.
    /// </remarks>
    internal static (decimal? Latitude, decimal? Longitude) Approximate(decimal? latitude, decimal? longitude)
    {
        if (latitude is not decimal lat || longitude is not decimal lon) return (null, null);

        // Beyond the usable range, publish nothing rather than something wrong.
        if (lat is < -90m or > 90m || lon is < -180m or > 180m) return (null, null);

        var snappedLat = SnapToCellCentre(lat, LatitudeStep);

        // The longitude step is derived from the SNAPPED latitude, not the true one. Deriving it
        // from the true latitude made the published longitude a continuous function of the true
        // latitude — two neighbouring houses got very slightly different steps and so landed on
        // different points, which is precisely the leak the snapping exists to close. Caught by the
        // test asserting that neighbours publish identically.
        var cosLat = Math.Cos((double)snappedLat * Math.PI / 180.0);

        // Near the poles cos approaches zero and the step would explode; 0.05 caps the widening at
        // twenty-fold, which is far past anywhere a case will be reported from.
        var longitudeStep = (decimal)(0.1 / Math.Max(Math.Abs(cosLat), 0.05));

        return (snappedLat, SnapToCellCentre(lon, longitudeStep));
    }

    /// <summary>
    /// The centre of the grid cell containing <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// Floor rather than round, so a cell is a half-open interval and every input maps to exactly
    /// one cell. Rounding would make values near a boundary land in whichever cell they were
    /// nearer, splitting a single cell's occupants across two published points.
    /// </remarks>
    private static decimal SnapToCellCentre(decimal value, decimal step)
    {
        var cellIndex = Math.Floor(value / step);
        return Math.Round((cellIndex * step) + (step / 2m), 4);
    }
}
