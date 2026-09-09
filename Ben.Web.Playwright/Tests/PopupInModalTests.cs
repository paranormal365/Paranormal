using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A Telerik popup opened from inside a dialog has to stay on the screen.
/// </summary>
/// <remarks>
/// <para>
/// Ben reported it on the case Investigations tab, 2026-09-09: <em>Propose Dates</em> → a
/// date/time field → the calendar runs off the bottom of the window and its own buttons cannot
/// be reached. Measured at 1280x720 before the fix: the popup is 417px tall and opens at y=411,
/// so 108px of it — the whole footer — is below the window.
/// </para>
/// <para>
/// Telerik flips a popup above its anchor when there is no room below, and does nothing at all
/// when there is room on neither side. Eleven dialogs in the library put a picker, dropdown,
/// combo box or multi-select inside a <c>BenModal</c>, so this is a test of the shape rather
/// than of the one screen. The fix is <c>wwwroot/js/popup-fit.js</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("PopupInModal")]
public class PopupInModalTests : BenTestBase
{
    /// <summary>Signs in and opens the Propose Dates dialog on the seeded case.</summary>
    private async Task<bool> OpenProposeDatesAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;

        await OpenTabAsync("Investigations",
            Main.GetByText("Date Proposals to Client", new() { Exact = false }).First);
        await SkipAnyTourAsync();

        // The panel lives inside a collapsed <details>.
        await Main.GetByText("Date Proposals to Client", new() { Exact = false }).First.ClickAsync();

        var propose = Main.GetByRole(AriaRole.Button, new() { Name = "Propose Dates" });
        await Expect(propose).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await propose.ClickAsync();

        await Expect(Page.Locator(".modal.show").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return true;
    }

    /// <summary>
    /// Opens a date-and-time calendar inside the dialog and returns its container.
    /// </summary>
    /// <remarks>
    /// Through the multi-day checkbox, because that is now where the combined picker lives: the
    /// start was split into a date box and a time box on 2026-09-09, and their popups are short
    /// enough to fit. The tall one — calendar, time list and a footer — is the shape this test
    /// exists for.
    /// </remarks>
    private async Task<ILocator> OpenTheCalendarAsync()
    {
        var multiDay = Page.Locator(".modal.show input[type=checkbox]").First;
        await Expect(multiDay).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await multiDay.CheckAsync();

        var toggle = Page.Locator(".modal.show .k-datetimepicker button.k-input-button").First;
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await toggle.ClickAsync();

        var popup = Page.Locator(".k-animation-container:visible").First;
        await Expect(popup).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The popup is placed as soon as it is shown and grows afterwards, as its content
        // arrives over the circuit. Measuring before it has settled measures the wrong thing.
        await Expect(popup.Locator(".k-time-accept")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.WaitForTimeoutAsync(500);
        return popup;
    }

    [Test]
    public async Task DateTimePicker_popup_stays_inside_the_window()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var popup = await OpenTheCalendarAsync();

        var viewport = Page.ViewportSize!;
        var box = await popup.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, "The popup has no box.");

        TestContext.Out.WriteLine(
            $"viewport {viewport.Width}x{viewport.Height}; " +
            $"popup y={box!.Y:F0} h={box.Height:F0} bottom={box.Y + box.Height:F0}");

        Assert.That(box.Y, Is.GreaterThanOrEqualTo(0), "The popup starts above the window.");
        Assert.That(box.Y + box.Height, Is.LessThanOrEqualTo(viewport.Height),
            "The popup runs off the bottom of the window.");
    }

    /// <summary>
    /// The complaint itself: the buttons that commit the choice have to be clickable. Geometry
    /// alone would not catch a popup that is on screen but under the dialog's own backdrop.
    /// </summary>
    [Test]
    public async Task The_popups_own_Set_button_commits_a_date()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var popup = await OpenTheCalendarAsync();

        // In the viewport, not merely in the DOM. Playwright will happily click a button below
        // the fold by scrolling the document at it, which is precisely what a person on this
        // screen could not do — so the assertion has to be about where the button is.
        var set = popup.Locator(".k-time-accept");
        await Expect(set).ToBeInViewportAsync(new() { Timeout = 5_000 });

        await popup.Locator(".k-calendar-nav-today").ClickAsync(new() { Timeout = 5_000 });
        await set.ClickAsync(new() { Timeout = 5_000 });

        var field = Page.Locator(".modal.show .k-datetimepicker input.k-input-inner").First;
        await Expect(field).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"\d{2}/\d{2}/\d{4}"),
            new() { Timeout = 10_000 });
    }

    /// <summary>
    /// The guard on the other side: a popup with room below it must be left exactly where
    /// Telerik put it. A fix that moved every popup would be a different bug.
    /// </summary>
    [Test]
    public async Task A_popup_with_room_below_it_is_not_moved()
    {
        await Page.SetViewportSizeAsync(1280, 1400);

        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var popup = await OpenTheCalendarAsync();

        var anchor = await Page.Locator(".modal.show .k-datetimepicker").First.BoundingBoxAsync();
        var box = await popup.BoundingBoxAsync();
        Assert.That(anchor, Is.Not.Null);
        Assert.That(box, Is.Not.Null);

        TestContext.Out.WriteLine(
            $"anchor bottom={anchor!.Y + anchor.Height:F0}; popup y={box!.Y:F0} h={box.Height:F0}");

        Assert.That(box.Y, Is.GreaterThanOrEqualTo(anchor.Y + anchor.Height - 2),
            "The popup was moved up over its own field on a window with room to spare.");
        Assert.That(box.Y + box.Height, Is.LessThanOrEqualTo(1400));
    }
}
