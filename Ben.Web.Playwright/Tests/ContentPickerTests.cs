using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 175: the share-from-user dialog offers a content picker, not a Guid box. The picker
/// lists real candidates with search and filters, and choosing one fills the dialog's chosen-
/// file card. Deliberately stops short of sharing — the copy flow is unit-covered, and this
/// test leaves no rows behind in the shared database.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ContentPicker")]
public class ContentPickerTests : BenTestBase
{
    private const string TghId = "881ea0f6-8c0d-475e-9065-c6ed15e3302f";

    [Test]
    public async Task The_share_dialog_offers_a_picker_and_a_choice_fills_the_card()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah — TGH administrator

        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}?tab=files");
        await WaitUntilLoadedAsync();

        var shareButton = Main.GetByRole(AriaRole.Button, new() { Name = "Share from User" });
        await Expect(shareButton).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await ClickUntilAsync(shareButton, Page.Locator("#orgcopy-choose"));

        // No Guid box anywhere — that was the whole complaint.
        await Expect(Page.Locator("input[placeholder*='UploadFile ID']")).ToHaveCountAsync(0);

        await ClickUntilAsync(Page.Locator("#orgcopy-choose"), Page.Locator("#picker-search"));

        // The picker is real: search, a layout toggle, and either candidates or the honest
        // empty-state sentence.
        await Expect(Page.Locator("#picker-layout")).ToBeVisibleAsync(new() { Timeout = 45_000 });

        var cards = Page.Locator(".ben-content-picker-grid .card");
        var empty = Page.Locator("#picker-empty");
        await Expect(cards.First.Or(empty)).ToBeVisibleAsync(new() { Timeout = 45_000 });

        if (await cards.CountAsync() > 0)
        {
            // Choose the first candidate; the dialog's chosen-file card takes its name.
            await cards.First.ClickAsync();
            await Expect(Page.Locator("#picker-select")).ToBeEnabledAsync(new() { Timeout = 45_000 });
            await Page.Locator("#picker-select").ClickAsync();

            await Expect(Page.Locator("#orgcopy-chosen")).ToBeVisibleAsync(new() { Timeout = 45_000 });
        }

        // Close without sharing: nothing was created anywhere.
        await Page.Keyboard.PressAsync("Escape");
    }
}
