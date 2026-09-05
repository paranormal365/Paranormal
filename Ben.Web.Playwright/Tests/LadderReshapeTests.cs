using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Reshaping the whole price ladder from the price-bands screen.
/// </summary>
/// <remarks>
/// <para><b>Nothing here saves.</b> The ladder is global — every group is priced from it and every
/// other test in the suite runs against it — so a test that committed a reshape would change the
/// prices under its neighbours. What is driven is everything up to the save: the door, the form
/// loaded from the current bands, and the one piece of behaviour that is easy to get wrong when
/// adding a band. The rules themselves are covered by <c>LadderSaveTests</c> against the
/// endpoint.</para>
///
/// <para>Why the screen needed this at all: a band-at-a-time save is validated on the list as it
/// will be after that single edit, so a reshape whose every intermediate state is illegal cannot
/// be expressed. Splitting an unbounded top band is the example — bounding the top first leaves
/// the members above it unpriced, and adding the band above first overlaps the unbounded one
/// below (2026-09-05).</para>
/// </remarks>
[TestFixture]
[Category("Billing")]
public class LadderReshapeTests : BenTestBase
{
    private async Task OpenLadderAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/subscription-tiers");
        await WaitUntilLoadedAsync();

        var open = Page.Locator("#reshape-ladder");
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await open.ClickAsync();

        await Expect(Page.GetByText("Every band, saved together")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    [Description("The reshape form opens with a row for every band the ladder has now.")]
    public async Task It_opens_holding_the_ladder_as_it_stands()
    {
        await OpenLadderAsync();

        var rows = Page.Locator("#ladder-rows tr");
        var count = await rows.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "The reshape form offered no bands to edit.");

        // The first band is the bottom of the ladder and must start at one member — the rule that
        // makes every group priceable, and the one a reader should see is already satisfied.
        var from = rows.First.Locator("input[type='number']").First;
        Assert.That(await from.InputValueAsync(), Is.EqualTo("1"));
    }

    /// <summary>
    /// Adding a band takes the unbounded end away from the band it follows.
    /// </summary>
    /// <remarks>
    /// Without this the new row starts above nothing and the save is refused for a reason the
    /// person did not cause — the top band is still unbounded, so the two overlap. Doing it here
    /// means the form only ever offers a shape that can be saved.
    /// </remarks>
    [Test]
    [Description("Adding a band bounds the one above it, so the new row has somewhere to go.")]
    public async Task Adding_a_band_bounds_the_one_it_follows()
    {
        await OpenLadderAsync();

        var rows = Page.Locator("#ladder-rows tr");
        var before = await rows.CountAsync();

        // The last band's upper limit is empty, which is what "no upper limit" looks like.
        var lastTo = rows.Nth(before - 1).Locator("input[type='number']").Nth(1);
        Assert.That(await lastTo.InputValueAsync(), Is.Empty,
            "The highest band should have no upper limit before a band is added after it.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add a band" }).ClickAsync();

        await Expect(rows).ToHaveCountAsync(before + 1, new() { Timeout = 10_000 });
        Assert.That(await lastTo.InputValueAsync(), Is.Not.Empty,
            "Adding a band left the band above it unbounded, which cannot be saved.");

        // Nothing is committed: the ladder every other test prices against is untouched.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
    }
}
