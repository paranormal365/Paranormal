using NUnit.Framework;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The two fills, in a real browser: a block filled with its colour, and a group drawn as a panel.
/// </summary>
/// <remarks>
/// These are what make the boards Ben sent look like the pictures — the moodboard's coloured sections
/// and the deck's slides. They are CSS on markup that already existed, so the thing worth driving here
/// is that the computed background actually changes: a class name proves nothing when a missing rule
/// leaves the old look wearing the new name.
/// </remarks>
[TestFixture]
[Category("Editing")]
public class CanvasFillTests : CanvasTestBase
{
    [Test]
    public async Task A_block_can_be_filled_with_its_colour()
    {
        await StartCleanAsync();
        var note = await AddNodeAsync("text");

        var before = await note.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");

        await Page.ClickAsync("#bc-prop-fill");
        await Expect(note).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-node--filled"));

        var after = await note.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        Assert.That(after, Is.Not.EqualTo(before), "filling a block has to change what is drawn");
    }

    /// <summary>And it goes back, because a fill is a choice rather than a one-way door.</summary>
    [Test]
    public async Task A_fill_can_be_taken_off_again()
    {
        await StartCleanAsync();
        var note = await AddNodeAsync("text");

        await Page.ClickAsync("#bc-prop-fill");
        await Expect(note).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-node--filled"));

        await Page.ClickAsync("#bc-prop-fill");
        await Expect(note).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-node--filled"));
    }

    /// <summary>
    /// One undo takes the fill back: it is a command like every other change, and the header says so.
    /// </summary>
    /// <remarks>
    /// The board is focused before the chord, which is the idiom every undo test here uses — the
    /// keyboard handler listens on the board, and a first version of this pressed Ctrl+Z while focus
    /// was still in the properties panel and the key never arrived.
    /// </remarks>
    [Test]
    public async Task Filling_is_undoable()
    {
        await StartCleanAsync();
        var note = await AddNodeAsync("text");

        await Page.ClickAsync("#bc-prop-fill");
        await Expect(note).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-node--filled"));

        // It is a named step in the history, not a silent property write.
        await Expect(Page.Locator(".bc-header [data-bc-action='undo']"))
            .ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex(
                "^Undo fill", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+z");
        await Expect(note).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-node--filled"));
    }

    /// <summary>
    /// A group drawn as a panel: solid border rather than dashed, and a background that is actually
    /// different from the 8% tint an outline has always had.
    /// </summary>
    [Test]
    public async Task A_group_can_be_drawn_as_a_panel()
    {
        await StartCleanAsync();
        var first = await AddNodeAsync("card");
        await AddNodeAsync("text");

        // Select both and group them: the panel is a property of the group, so one has to exist.
        await Page.Keyboard.PressAsync("Control+a");
        await Page.Keyboard.PressAsync("Control+g");

        var group = Page.Locator(".bc-group");
        await Expect(group).ToHaveCountAsync(1);
        await group.Locator(".bc-group__label").ClickAsync();

        // The background is what is asserted, not the border: a SELECTED group already draws a solid
        // edge, so the border says nothing about the fill while the group is the thing selected.
        // A first version of this test asserted "dashed" here and failed for that reason.
        var before = await group.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");

        await Page.ClickAsync("#bc-prop-group-panel");
        await Expect(group).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-group--panel"));

        var after = await group.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        Assert.That(after, Is.Not.EqualTo(before), "a panel is drawn with a solid tint, not the 8% wash");
        _ = first;
    }
}
