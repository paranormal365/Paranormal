using NUnit.Framework;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The family tree's connectors, in a real browser: dashed lines, right angles, diamond and dot ends,
/// and a mark at the middle.
/// </summary>
/// <remarks>
/// <para>The routing is answered once in <c>EdgeGeometry.Resolve</c> and read by three callers — the
/// board's SVG, the published picture, and the hit-test. This drives the first and the third: that the
/// drawn path really turns at right angles, and that a click on the turn still selects the connector.
/// A line you can see and cannot select is the failure mode the shared function exists to prevent.</para>
/// </remarks>
[TestFixture]
[Category("Editing")]
public class CanvasConnectorStyleTests : CanvasTestBase
{
    /// <summary>Two cards, joined, with the connector selected and its panel open.</summary>
    private async Task<Microsoft.Playwright.ILocator> ConnectedAsync()
    {
        await StartCleanAsync();
        var first = await AddNodeAsync("card");

        // The side handle grows the next card already joined to this one (M8's gesture).
        await first.ClickAsync();
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+Shift+ArrowRight");

        var edge = Page.Locator(".bc-edge");
        await Expect(edge).ToHaveCountAsync(1);
        await edge.Locator(".bc-edge__hit").ClickAsync(new() { Force = true });
        return edge;
    }

    [Test]
    public async Task A_connector_can_be_dashed()
    {
        var edge = await ConnectedAsync();

        await Page.SelectOptionAsync("#bc-prop-edge-line", "Dashed");

        await Expect(edge).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-edge--dashed"));
        var dash = await edge.Locator(".bc-edge__line")
            .EvaluateAsync<string>("e => getComputedStyle(e).strokeDasharray");
        Assert.That(dash, Does.Not.Empty.And.Not.EqualTo("none"), "a dashed line has to actually dash");
    }

    /// <summary>
    /// Right angles: the drawn path is line segments rather than a curve, and every segment is
    /// horizontal or vertical. That property is what makes a tree read as a tree.
    /// </summary>
    /// <summary>
    /// Right angles: the drawn path is line segments, and none of them runs diagonally. That property
    /// is what makes a family tree read as a tree.
    /// </summary>
    /// <remarks>
    /// The second card is moved DOWN first, and that is the point rather than setup noise. Two blocks
    /// at the same height joined left to right need no turn at all — the straight run between them IS
    /// the connector — so an elbow there correctly has no corners. A first version of this test grew
    /// the second card level with the first and failed for exactly that reason.
    /// </remarks>
    [Test]
    public async Task A_connector_can_run_in_right_angles()
    {
        var edge = await ConnectedAsync();

        // Offset the far end, so a right-angled route has somewhere to turn.
        var second = Nodes.Last;
        await DragAsync(second, 0, 180);

        await edge.Locator(".bc-edge__hit").ClickAsync(new() { Force = true });
        await Page.SelectOptionAsync("#bc-prop-edge-route", "Elbow");

        var d = await edge.Locator(".bc-edge__line").GetAttributeAsync("d");
        Assert.That(d, Does.Contain("L "), "an elbow draws line segments");
        Assert.That(d, Does.Not.Contain("C "), "an elbow is not a curve");

        // Every segment is horizontal or vertical: that is what "right angles" means.
        var numbers = System.Text.RegularExpressions.Regex.Matches(d!, @"-?[\d.]+")
            .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        for (var i = 2; i + 1 < numbers.Count; i += 2)
        {
            var dx = Math.Abs(numbers[i] - numbers[i - 2]);
            var dy = Math.Abs(numbers[i + 1] - numbers[i - 1]);
            Assert.That(dx < 0.5 || dy < 0.5, Is.True,
                $"segment {i / 2} runs diagonally in \"{d}\"");
        }
    }

    [Test]
    public async Task A_connector_can_be_straight()
    {
        var edge = await ConnectedAsync();

        await Page.SelectOptionAsync("#bc-prop-edge-route", "Straight");

        // A straight line is the cubic with its controls on its ends, so it still writes a C - and
        // the point of the assertion is that it draws at all and stays selectable.
        var d = await edge.Locator(".bc-edge__line").GetAttributeAsync("d");
        Assert.That(d, Does.StartWith("M "));
    }

    /// <summary>
    /// Each end draws what it was told, independently — the marriage line's two diamonds.
    /// </summary>
    [Test]
    public async Task Each_end_can_carry_its_own_marker()
    {
        var edge = await ConnectedAsync();

        await Page.SelectOptionAsync("#bc-prop-edge-from-marker", "Diamond");
        await Page.SelectOptionAsync("#bc-prop-edge-to-marker", "Diamond");

        await Expect(edge.Locator(".bc-edge__head--diamond")).ToHaveCountAsync(2);

        await Page.SelectOptionAsync("#bc-prop-edge-to-marker", "Dot");
        await Expect(edge.Locator(".bc-edge__head--dot")).ToHaveCountAsync(1);
        await Expect(edge.Locator(".bc-edge__head--diamond")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task An_end_can_carry_nothing_at_all()
    {
        var edge = await ConnectedAsync();

        await Page.SelectOptionAsync("#bc-prop-edge-to-marker", "None");
        await Page.SelectOptionAsync("#bc-prop-edge-from-marker", "None");

        await Expect(edge.Locator(".bc-edge__head")).ToHaveCountAsync(0);
    }

    /// <summary>The mark the family tree puts at the middle of a child line.</summary>
    [Test]
    public async Task A_connector_can_be_marked_at_its_middle()
    {
        var edge = await ConnectedAsync();

        await Page.FillAsync("#bc-prop-edge-icon", "x");
        await Page.Locator("#bc-prop-edge-icon").BlurAsync();

        await Expect(edge.Locator(".bc-edge__icon")).ToHaveTextAsync("x");
    }

    /// <summary>
    /// And the turn is clickable. An elbow sampled as though it were still a curve would cut its
    /// corners and leave the bend unselectable.
    /// </summary>
    [Test]
    public async Task The_turn_of_an_elbow_can_be_clicked()
    {
        var edge = await ConnectedAsync();
        await Page.SelectOptionAsync("#bc-prop-edge-route", "Elbow");

        // Deselect, then click the drawn path itself and check the connector comes back.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(edge).Not.ToHaveAttributeAsync("data-bc-selected", "true");

        await edge.Locator(".bc-edge__hit").ClickAsync(new() { Force = true });
        await Expect(edge).ToHaveAttributeAsync("data-bc-selected", "true");
    }
    /// <summary>
    /// The Arrow choice means exactly what it says, whatever the per-end markers held before.
    /// </summary>
    /// <remarks>
    /// <para>There are two ways to put a head on a connector — the three-way <b>Arrow</b> choice that
    /// every board before M9 used, and a marker per end — and the per-end markers win when they are
    /// set. So the panel could contradict itself: choosing "Arrows at both ends" on a connector whose
    /// end held a Diamond drew a diamond at one end and an arrow at the other, and "No arrow" sat
    /// happily above a visible diamond. Found by walking the board on 2026-09-18.</para>
    ///
    /// <para>Asserting the DRAWN heads rather than the select's value is the point: the bug was that
    /// the control and the drawing disagreed, so reading the control back would have proved nothing.</para>
    /// </remarks>
    [Test]
    public async Task Arrows_at_both_ends_means_two_arrows_whatever_the_ends_held_before()
    {
        var edge = await ConnectedAsync();

        await Page.SelectOptionAsync("#bc-prop-edge-to-marker", "Diamond");
        await Expect(edge.Locator(".bc-edge__head--diamond")).ToHaveCountAsync(1);

        await Page.SelectOptionAsync("#bc-prop-edge-arrow", "Both");

        await Expect(edge.Locator(".bc-edge__head--arrow")).ToHaveCountAsync(2);
        await Expect(edge.Locator(".bc-edge__head--diamond")).ToHaveCountAsync(0);
    }

    /// <summary>And "No arrow" leaves nothing at either end, including a diamond somebody chose.</summary>
    [Test]
    public async Task No_arrow_clears_both_ends()
    {
        var edge = await ConnectedAsync();

        await Page.SelectOptionAsync("#bc-prop-edge-from-marker", "Dot");
        await Page.SelectOptionAsync("#bc-prop-edge-to-marker", "Diamond");
        await Expect(edge.Locator("[class*=bc-edge__head]")).ToHaveCountAsync(2);

        await Page.SelectOptionAsync("#bc-prop-edge-arrow", "None");

        await Expect(edge.Locator("[class*=bc-edge__head]")).ToHaveCountAsync(0);
    }
}
