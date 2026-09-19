using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public class ArrowHeadSizeTests
{
    [Fact]
    public void A_longer_arrow_gets_a_bigger_head()
    {
        var shortArrow = ArrowHeadSize.For(strokeWidth: 2, arrowLength: 100);
        var longArrow  = ArrowHeadSize.For(strokeWidth: 2, arrowLength: 900);

        Assert.True(longArrow > shortArrow,
            $"a 900px arrow got the same {longArrow}px head as a 100px one — the fault this fixes.");
    }

    [Fact]
    public void The_default_arrow_on_a_720p_canvas_is_big_enough_to_see()
    {
        // A callout added with the toolbar button is 20% of the canvas wide: 256px at 1280.
        var head = ArrowHeadSize.For(strokeWidth: 2, arrowLength: 256);

        Assert.True(head >= 24, $"a head of {head}px on a 1280-wide frame is not something you can point with.");
    }

    [Fact]
    public void A_heavy_stroke_keeps_exactly_the_head_it_had_before()
    {
        // Nothing an author has already drawn gets smaller: the old rule is the floor.
        foreach (var stroke in new double[] { 8, 12, 20 })
            Assert.Equal(ArrowHeadSize.FromStroke(stroke), ArrowHeadSize.For(stroke, arrowLength: 256));
    }

    [Fact]
    public void No_arrow_ever_gets_a_smaller_head_than_the_old_rule_gave_it()
    {
        foreach (var stroke in new double[] { 0, 1, 2, 5, 20 })
            foreach (var length in new double[] { 0, 1, 40, 256, 1920 })
                Assert.True(ArrowHeadSize.For(stroke, length) >= ArrowHeadSize.FromStroke(stroke));
    }

    [Fact]
    public void A_stubby_arrow_does_not_turn_into_a_triangle()
    {
        // 30px long with a hairline stroke: the length rule would happily eat the whole thing.
        var head = ArrowHeadSize.For(strokeWidth: 0.1, arrowLength: 30);

        Assert.True(head <= Math.Max(ArrowHeadSize.FromStroke(0.1), 15), $"head {head} on a 30px arrow");
    }
}
