using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// An investigation may run longer than one night.
/// </summary>
/// <remarks>
/// Ben, 2026-09-09: a week-long visit has to be bookable, a weekend has to be bookable, and
/// single-day has to stay the norm — including the ordinary overnight, arriving at 3pm and
/// leaving at 8am the next morning. So the end is a clock time until somebody says otherwise.
/// </remarks>
[TestFixture]
[Category("MultiDayInvestigation")]
public class MultiDayInvestigationTests : BenTestBase
{
    private async Task<bool> OpenScheduleDialogAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;

        await OpenTabAsync("Investigations", Main.Locator("[data-testid='schedule-investigation']"));
        await SkipAnyTourAsync();

        await Main.Locator("[data-testid='schedule-investigation']").ClickAsync();
        await Expect(Page.Locator(".modal.show").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return true;
    }

    private ILocator MultiDayBox => Page.Locator("#investigationpanel-multi-day");
    private ILocator Dialog      => Page.Locator(".modal.show").First;

    [Test]
    public async Task The_end_is_a_time_until_multi_day_is_ticked()
    {
        if (!await OpenScheduleDialogAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        // Single day is the norm: a date and a time for the start, a clock time for the end.
        await Expect(MultiDayBox).Not.ToBeCheckedAsync();
        await Expect(Dialog.Locator(".k-datepicker")).ToHaveCountAsync(1);
        await Expect(Dialog.Locator(".k-timepicker")).ToHaveCountAsync(2);
        await Expect(Dialog.Locator(".k-datetimepicker")).ToHaveCountAsync(0);

        await MultiDayBox.CheckAsync();

        // Ticked, the end gains a date of its own and the start is untouched.
        await Expect(Dialog.Locator(".k-datetimepicker")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await Expect(Dialog.Locator(".k-datepicker")).ToHaveCountAsync(1);
        await Expect(Dialog.Locator(".k-timepicker")).ToHaveCountAsync(1);

        await MultiDayBox.UncheckAsync();

        await Expect(Dialog.Locator(".k-datetimepicker")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Expect(Dialog.Locator(".k-timepicker")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task The_dialog_says_an_earlier_end_time_is_the_next_morning()
    {
        if (!await OpenScheduleDialogAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        // The one sentence that stops somebody reaching for the checkbox for an ordinary
        // overnight — which is the shape most investigations actually have.
        await Expect(Dialog.GetByText("A time at or before the start is the next morning"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task Propose_dates_offers_the_same_choice()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        await OpenTabAsync("Investigations",
            Main.GetByText("Date Proposals to Client", new() { Exact = false }).First);
        await SkipAnyTourAsync();
        await Main.GetByText("Date Proposals to Client", new() { Exact = false }).First.ClickAsync();

        var propose = Main.GetByRole(AriaRole.Button, new() { Name = "Propose Dates" });
        await Expect(propose).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await propose.ClickAsync();
        await Expect(Dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var box = Page.Locator("#schedulingproposalpanel-multi-day");
        await Expect(box).Not.ToBeCheckedAsync();
        await Expect(Dialog.Locator(".k-datepicker")).ToHaveCountAsync(1);
        await Expect(Dialog.Locator(".k-timepicker")).ToHaveCountAsync(2);

        await box.CheckAsync();
        await Expect(Dialog.Locator(".k-datetimepicker")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await Expect(Dialog.Locator(".k-timepicker")).ToHaveCountAsync(1);
    }
}
