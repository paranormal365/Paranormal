using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A date field keeps the date you are building, and refuses one that cannot exist.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-09: an empty Telerik date box rebuilt the whole date the first time a segment was stepped
/// (item 221). Item 224, measured 2026-09-15: a seeded one turned 0-9-3-1 into 09/01 and hour 13 PM into 03 PM,
/// silently, on Telerik 14.1 and 15.0.1 alike. Every date and time field is now <c>BenDateField</c>: typed text, read
/// by the site, with a sentence for anything impossible and the kept value back in the box.</para>
/// <para>Driven through the Propose Dates dialog, a field inside a modal — the hardest place for a field to behave.</para>
/// </remarks>
[TestFixture]
[Category("DateField")]
public class DateFieldTests : BenTestBase
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

    private ILocator FirstField(string mode) => Page.Locator($".modal.show .ben-date-field[data-mode='{mode}']").First;

    /// <summary>No date field is ever handed over empty.</summary>
    [Test]
    public async Task A_proposed_date_starts_from_a_real_date()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        await Expect(FirstField("date").Locator("input")).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex(@"^\d{2}/\d{2}/\d{4}$"), new() { Timeout = 10_000 });
    }

    /// <summary>The measured keystrokes: 09/31 is refused with the month's length, and the date already there stays.</summary>
    [Test]
    public async Task The_thirty_first_of_September_is_refused_and_the_date_already_there_stays()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var field = FirstField("date");
        var input = field.Locator("input");
        await Expect(input).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^\d{2}/\d{2}/\d{4}$"), new() { Timeout = 10_000 });
        var before = await input.InputValueAsync();

        await input.FillAsync("09/31/2026");
        await input.PressAsync("Tab");

        var said = field.Locator(".invalid-feedback");
        await Expect(said).ToContainTextAsync("September 2026 has 30 days", new() { Timeout = 10_000 });
        await Expect(said).ToContainTextAsync($"It is still {before}");
        await Expect(input).ToHaveValueAsync(before);
        await Expect(input).ToHaveAttributeAsync("aria-invalid", "true");

        // And a real date is taken, written the site's way, with the refusal gone.
        await input.FillAsync("9/30/26");
        await input.PressAsync("Tab");
        await Expect(input).ToHaveValueAsync("09/30/2026", new() { Timeout = 10_000 });
        await Expect(field.Locator(".invalid-feedback")).ToHaveCountAsync(0);
    }

    /// <summary>Hour 13 with PM was 03 PM on Telerik's time picker; here it is a sentence, and 8pm reads as 08:00 PM.</summary>
    [Test]
    public async Task An_impossible_hour_is_refused_and_a_casual_time_is_read()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var field = FirstField("time");
        var input = field.Locator("input");
        await Expect(input).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var before = await input.InputValueAsync();

        await input.FillAsync("13:00 PM");
        await input.PressAsync("Tab");
        await Expect(field.Locator(".invalid-feedback")).ToContainTextAsync("13 PM isn't a time", new() { Timeout = 10_000 });
        await Expect(input).ToHaveValueAsync(before);

        await input.FillAsync("8pm");
        await input.PressAsync("Tab");
        await Expect(input).ToHaveValueAsync("08:00 PM", new() { Timeout = 10_000 });
        await Expect(field.Locator(".invalid-feedback")).ToHaveCountAsync(0);
    }
}
