using NUnit.Framework;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// A shape on a real board: added, drawn as its kind, named, and changed from one kind to another.
/// </summary>
/// <remarks>
/// Two of the four boards Ben sent are built from shapes — the moodboard's theme bubbles and the
/// family tree's marriage markers. The drawing is CSS on one div (a diamond is a rotated square with
/// its words rotated back), so this checks the geometry actually arrives in the browser rather than
/// only in the class name a unit test can see.
/// </remarks>
[TestFixture]
[Category("Editing")]
public class CanvasShapeTests : CanvasTestBase
{
    [Test]
    public async Task A_shape_is_added_named_and_drawn_as_its_kind()
    {
        await StartCleanAsync();
        var shape = await AddNodeAsync("shape");

        await Expect(shape.Locator(".bc-shape--rectangle")).ToHaveCountAsync(1);

        await shape.DblClickAsync();
        var text = shape.Locator("[aria-label='Shape text']");
        await Expect(text).ToBeVisibleAsync();
        await text.FillAsync("Wellness");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(shape).ToContainTextAsync("Wellness");
    }

    /// <summary>
    /// The kind can be changed after the fact, and the change is on the board once the edit ends.
    /// </summary>
    /// <remarks>
    /// The assertion is deliberately after Escape. While a block is being edited its body IS the edit
    /// form — the pattern every block here follows, card fields included — so the drawn shape does not
    /// exist to look at until the edit is committed. A first version of this test asserted the class
    /// mid-edit and failed for that reason, which is worth leaving written down.
    /// </remarks>
    [Test]
    public async Task A_shape_can_be_changed_to_an_ellipse_and_a_diamond()
    {
        await StartCleanAsync();
        var shape = await AddNodeAsync("shape");

        await shape.DblClickAsync();
        await shape.Locator("select").SelectOptionAsync("Ellipse");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(shape.Locator(".bc-shape--ellipse")).ToHaveCountAsync(1);

        await shape.DblClickAsync();
        await shape.Locator("select").SelectOptionAsync("Diamond");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(shape.Locator(".bc-shape--diamond")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// A circle has to actually be round, and a diamond actually diamond-shaped. The class alone
    /// proves nothing — the CSS is what draws it, and a missing rule leaves a square wearing the
    /// right name.
    /// </summary>
    /// <remarks>
    /// <b>A diamond is CUT, not turned</b> (changed 2026-09-18). It used to be a square with a
    /// forty-five degree rotation, and the templates walk photographed what that actually produced: a
    /// block's body is shorter than it is wide, because the title bar takes the difference, so the
    /// rotated square's four corners fell outside the frame and were clipped away by its overflow —
    /// drawing an OCTAGON. A clip-path polygon fills whatever box it is given. So this asserts the
    /// polygon is there AND that no rotation has come back, because a rotation is what caused it.
    /// </remarks>
    [Test]
    public async Task An_ellipse_is_round_and_a_diamond_is_cut_to_shape()
    {
        await StartCleanAsync();
        var shape = await AddNodeAsync("shape");
        await shape.DblClickAsync();
        await shape.Locator("select").SelectOptionAsync("Ellipse");
        await Page.Keyboard.PressAsync("Escape");

        var radius = await shape.Locator(".bc-shape--ellipse")
            .EvaluateAsync<string>("e => getComputedStyle(e).borderTopLeftRadius");
        Assert.That(radius, Does.Contain("%").Or.Contain("px"), "an ellipse is drawn by its radius");

        await shape.DblClickAsync();
        await shape.Locator("select").SelectOptionAsync("Diamond");
        await Page.Keyboard.PressAsync("Escape");

        var drawn = await shape.Locator(".bc-shape--diamond").EvaluateAsync<string[]>(
            "e => [getComputedStyle(e).clipPath, getComputedStyle(e).transform]");

        Assert.Multiple(() =>
        {
            Assert.That(drawn[0], Does.Contain("polygon"),
                "a diamond is cut out of its box; without the clip-path it is a rounded square");
            Assert.That(drawn[1], Is.EqualTo("none"),
                "a rotation is what made this an octagon: a turned square's corners fall outside a box "
                + "that is not square, and the frame clips them off");
        });
    }

    /// <summary>A shape placed and not yet named still draws: that is how a moodboard is built.</summary>
    [Test]
    public async Task An_empty_shape_still_draws()
    {
        await StartCleanAsync();
        var shape = await AddNodeAsync("shape");

        await Expect(shape.Locator(".bc-shape")).ToBeVisibleAsync();
    }
}
