using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// How precisely a case's location may be published.
/// </summary>
/// <remarks>
/// <para>A case's coordinates are somebody's home, and the public discovery payload carried the
/// exact stored values under field names that promised an approximation. This class tests the
/// control that closed that.</para>
///
/// <para>The properties worth asserting are not "the number equals X" — a step change would break
/// that without breaking anything real. They are: the true point never appears, neighbours cannot
/// be told apart, and the same input always gives the same output. The last one is the subtle one:
/// a random offset per request looks safer and is worse, because many responses can be averaged
/// back to the truth.</para>
/// </remarks>
public sealed class PublicCoordinatesTests
{
    /// <summary>The internal helper, reached the way the WebApi's other internals are in these tests.</summary>
    private static (decimal? Lat, decimal? Lon) Approximate(decimal? lat, decimal? lon)
    {
        var type = typeof(Ben.Data.WebApi.Controllers.Public.PublicCaseDiscoveryController).Assembly
            .GetType("Ben.Data.WebApi.Controllers.Public.PublicCoordinates")!;
        var method = type.GetMethod("Approximate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = method.Invoke(null, [lat, lon])!;
        var t = result.GetType();
        return ((decimal?)t.GetField("Item1")!.GetValue(result),
                (decimal?)t.GetField("Item2")!.GetValue(result));
    }

    [Fact]
    public void Nothing_stored_publishes_nothing()
    {
        Assert.Equal((null, null), Approximate(null, null));
        Assert.Equal((null, null), Approximate(36.16m, null));
        Assert.Equal((null, null), Approximate(null, -86.78m));
    }

    [Fact]
    public void An_impossible_coordinate_publishes_nothing_rather_than_something_wrong()
    {
        Assert.Equal((null, null), Approximate(91m, 0m));
        Assert.Equal((null, null), Approximate(0m, 181m));
    }

    [Fact]
    public void The_published_point_is_never_the_stored_one()
    {
        // Sampled across hemispheres and latitudes — the guarantee must not hold only near
        // Nashville, which is where every fixture in this codebase happens to live.
        decimal[][] places =
        [
            [36.1627m, -86.7816m],   // Nashville
            [51.5072m, -0.1276m],    // London
            [-33.8688m, 151.2093m],  // Sydney
            [64.1466m, -21.9426m],   // Reykjavík
            [0.0m, 0.0m],
        ];

        foreach (var place in places)
        {
            var (lat, lon) = Approximate(place[0], place[1]);
            Assert.NotNull(lat);
            Assert.NotNull(lon);
            Assert.False(lat == place[0] && lon == place[1],
                $"({place[0]}, {place[1]}) was published unchanged.");
        }
    }

    [Fact]
    public void The_published_point_stays_close_enough_to_be_useful()
    {
        var (lat, lon) = Approximate(36.1627m, -86.7816m);

        // A tenth of a degree of latitude is about seven miles; the answer must be inside the cell
        // the point fell in, not somewhere else entirely.
        Assert.True(Math.Abs(lat!.Value - 36.1627m) <= 0.1m);
        Assert.True(Math.Abs(lon!.Value - -86.7816m) <= 0.2m);
    }

    [Fact]
    public void The_same_input_always_gives_the_same_output()
    {
        var first  = Approximate(36.1627m, -86.7816m);
        var second = Approximate(36.1627m, -86.7816m);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Two houses on the same street publish identically. This is the assertion that caught a real
    /// flaw during development: the longitude cell size was first derived from the caller's *true*
    /// latitude, which made the published longitude a continuous function of the true latitude, so
    /// two neighbours landed a few metres apart — defeating the snapping entirely. It is now derived
    /// from the snapped latitude.
    /// </summary>
    [Fact]
    public void Neighbours_cannot_be_told_apart()
    {
        var a = Approximate(36.16010m, -86.78010m);
        var b = Approximate(36.16040m, -86.77990m);

        Assert.Equal(a, b);
    }

    /// <summary>
    /// A cell must not shrink towards the poles. A fixed longitude step would: at 64°N a tenth of a
    /// degree is under three miles, and the obfuscation would quietly weaken the further north a
    /// case was — the kind of failure nobody notices because it still looks like it is working.
    /// </summary>
    [Fact]
    public void A_cell_stays_roughly_as_wide_on_the_ground_at_high_latitude()
    {
        // Counted across a whole degree rather than compared at two chosen points: any two points
        // can happen to straddle a boundary, so a pair proves nothing either way. How many distinct
        // cells a degree of longitude is cut into is the actual property.
        int CellsAcrossOneDegree(decimal latitude)
            => Enumerable.Range(0, 200)
                .Select(step => Approximate(latitude, step * 0.005m).Lon)
                .Distinct()
                .Count();

        var atEquator = CellsAcrossOneDegree(0.05m);
        var atIceland = CellsAcrossOneDegree(64.15m);

        // A degree of longitude is ~69 miles at the equator and ~30 at 64°N. Cells that were a
        // fixed number of degrees wide would divide both into the same count, and each northern
        // cell would cover less than half the ground — the obfuscation quietly weakening the
        // further north a case was, which is the kind of failure nobody notices.
        Assert.True(atIceland < atEquator,
            $"A degree of longitude was cut into {atIceland} cells at 64°N and {atEquator} at the "
            + "equator; cells are not being widened for latitude.");
        Assert.True(atIceland <= atEquator / 2 + 1);
    }
}
