using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 156 Phase A: the per-tier role-areas checklist saves on toggle and survives a reload.
/// Restores what it changes — shared database.
/// </summary>
[TestFixture]
[Category("TierRoleAreas")]
public class TierRoleAreasTests : BenTestBase
{
    [Test]
    public async Task Unchecking_an_area_persists_and_rechecking_restores_it()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/subscription-tiers");
        await WaitUntilLoadedAsync();

        var freeRow = Main.Locator("tr", new() { HasTextString = "Free" }).First;
        var calendar = freeRow.Locator("input[type=checkbox][id$='-9']");   // Calendar = 9
        await Expect(calendar).ToBeVisibleAsync(new() { Timeout = 20_000 });
        Assert.That(await calendar.IsCheckedAsync(), Is.True,
            "Precondition: the seeded checklist is all-inclusive.");

        try
        {
            await calendar.UncheckAsync();
            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();

            freeRow = Main.Locator("tr", new() { HasTextString = "Free" }).First;
            calendar = freeRow.Locator("input[type=checkbox][id$='-9']");
            await Expect(calendar).Not.ToBeCheckedAsync(new() { Timeout = 20_000 });
        }
        finally
        {
            freeRow = Main.Locator("tr", new() { HasTextString = "Free" }).First;
            calendar = freeRow.Locator("input[type=checkbox][id$='-9']");
            if (!await calendar.IsCheckedAsync())
                await calendar.CheckAsync();
            await Expect(calendar).ToBeCheckedAsync(new() { Timeout = 15_000 });
        }
    }
}
