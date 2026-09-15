using System.Globalization;
using Ben.Data.Common.Blocks;
using Ben.Web.Website.Library.Kit.Blocks;
using Ben.Web.Website.Library.Kit.Maps;
using Xunit;

namespace Ben.Web.Tests.Blocks;

public sealed class BlockMapTests
{
    [Fact]
    public void A_new_map_block_has_no_places_no_route_and_no_region()
    {
        var block = NewBlock.Map();
        Assert.Equal(BlockKinds.Map, block.Kind);
        Assert.Empty(block.MapStops!);
        Assert.Equal(BlockMapRoutes.None, block.MapRoute);
        Assert.Null(block.Zoom);
    }

    [Fact]
    public void Great_circle_distance_is_right_to_within_half_a_percent()
    {
        // Nashville (BNA) to Memphis (MEM) is about 322 km.
        var meters = GreatCircle.Meters(36.1245, -86.6782, 35.0424, -89.9767);
        Assert.InRange(meters, 322_000 * 0.995, 322_000 * 1.005);
        Assert.Equal(0, GreatCircle.Meters(35, -86, 35, -86), 3);
    }

    [Fact]
    public void Totals_add_the_legs_and_give_a_time_only_when_every_leg_has_one()
    {
        var walked = new BenMapRouteSummary([new BenMapLeg(0, 1, 400, 300, true), new BenMapLeg(1, 2, 600, 450, true)]);
        Assert.Equal(1000, walked.TotalMeters);
        Assert.Equal(750, walked.TotalSeconds);

        var partly = new BenMapRouteSummary([new BenMapLeg(0, 1, 400, 300, true), new BenMapLeg(1, 2, 600, null, false, "No walking path found")]);
        Assert.Null(partly.TotalSeconds);
    }

    [Fact]
    public void Open_in_maps_is_the_place_or_the_route_ends_with_the_right_travel_mode()
    {
        var cemetery = new BlockMapStop(35.925123, -86.868912, "Rest Haven Cemetery");
        var house = new BlockMapStop(35.9265, -86.87, "The house");

        Assert.Equal("https://maps.apple.com/?ll=35.925123,-86.868912&q=Rest%20Haven%20Cemetery",
            BlockMapLinks.OpenInMaps([cemetery], BlockMapRoutes.None));
        Assert.Equal("https://maps.apple.com/?saddr=35.925123,-86.868912&daddr=35.9265,-86.87&dirflg=w",
            BlockMapLinks.OpenInMaps([cemetery, house], BlockMapRoutes.Walking));
        Assert.EndsWith("dirflg=d", BlockMapLinks.OpenInMaps([cemetery, house], BlockMapRoutes.Driving));
        Assert.Null(BlockMapLinks.OpenInMaps([], BlockMapRoutes.None));
    }

    [Fact]
    public void Coordinates_are_written_with_a_point_whatever_the_servers_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Contains("ll=35.5,-86.25", BlockMapLinks.OpenPlace(new BlockMapStop(35.5, -86.25)));
            Assert.Equal("0.3 mi straight line", BlockMapLinks.LegText(new BenMapLeg(0, 1, 500, null, false), BlockMapRoutes.Straight));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Leg_words_say_how_far_and_how_long()
    {
        Assert.Equal("0.4 mi · 9 min walk", BlockMapLinks.LegText(new BenMapLeg(0, 1, 650, 540, true), BlockMapRoutes.Walking));
        Assert.Equal("12.0 mi · 18 min drive", BlockMapLinks.LegText(new BenMapLeg(0, 1, 19_312, 1080, true), BlockMapRoutes.Driving));
        Assert.Equal("1 hr 5 min", BlockMapLinks.Duration(3900));
    }
}
