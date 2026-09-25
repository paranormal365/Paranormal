using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A calendar opened from a field inside a dialog has to be reachable and has to set the date.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-09: <em>Propose Dates</em> → a date/time field → Telerik's popup calendar ran off the bottom of
/// the window and its own buttons could not be reached (fixed then with <c>popup-fit.js</c>, which still places the
/// dropdowns and combo boxes).</para>
/// <para>Since item 224 (2026-09-15) date fields are <c>BenDateField</c>, whose calendar opens inline beneath the field,
/// inside the dialog's own scrolling body — so there is no popup layer to fall off the window. These tests hold that:
/// it opens under its field, it can be brought into view, and a click on a day sets the field.</para>
/// </remarks>
[TestFixture]
[Category("PopupInModal")]
public class PopupInModalTests : BenTestBase
{
    private async Task<bool> OpenProposeDatesAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;

        await OpenTabAsync("Investigations",
            Main.GetByText("Date Proposals to Client", new() { Exact = false }).First);
        await SkipAnyTourAsync();
        await Main.GetByText("Date Proposals to Client", new() { Exact = false }).First.ClickAsync();

        var propose = Main.GetByRole(AriaRole.Button, new() { Name = "Propose Dates" });
        await Expect(propose).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await propose.ClickAsync();
        await Expect(Page.Locator(".modal.show").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return true;
    }

    private ILocator Field => Page.Locator(".modal.show .ben-date-field[data-mode='date']").First;

    private async Task<ILocator> OpenTheCalendarAsync()
    {
        await Field.Locator(".ben-date-field__open").ClickAsync();
        var calendar = Field.Locator(".ben-date-field__calendar .k-calendar");
        await Expect(calendar).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return calendar;
    }

    [Test]
    public async Task The_calendar_opens_beneath_its_field_and_can_be_brought_into_view()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var calendar = await OpenTheCalendarAsync();

        var input = await Field.Locator("input").BoundingBoxAsync();
        var box = await calendar.BoundingBoxAsync();
        Assert.That(input, Is.Not.Null);
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Y, Is.GreaterThanOrEqualTo(input!.Y + input.Height - 2), "The calendar is not beneath its field.");

        // Inside the dialog's scrolling body, so scrolling reaches all of it — what a popup layer could not promise.
        await calendar.ScrollIntoViewIfNeededAsync();
        await Expect(calendar.Locator(".k-calendar-td").Last).ToBeInViewportAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task A_day_clicked_in_the_calendar_sets_the_field_and_closes_it()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var calendar = await OpenTheCalendarAsync();
        var day = calendar.Locator(".k-calendar-td:not(.k-other-month)").Nth(9);
        var picked = DateTime.Parse((await day.GetAttributeAsync("title"))!, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        await day.ClickAsync();

        await Expect(Field.Locator("input")).ToHaveValueAsync(picked.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture), new() { Timeout = 10_000 });
        await Expect(Field.Locator(".ben-date-field__calendar")).ToHaveCountAsync(0);
    }
}
