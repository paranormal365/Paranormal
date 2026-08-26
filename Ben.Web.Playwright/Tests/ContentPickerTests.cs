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
    // Resolved from the slug at run time — a hardcoded GUID dies with every database rebuild.
    private string TghId = null!;

    [SetUp]
    public async Task ResolveTghId() => TghId = await OrgIdBySlugAsync("paranormal365");

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

        // Audio/video cells load their player on demand — audio renders the WaveSurfer
        // .ws-player, video a plain <video>. This assertion is what exposed the lost
        // /js/wavesurfer assets: the folder died with Ben.Web.WebApp and every audio preview
        // on the site had said "Player init failed" since, so a working player must be
        // asserted, not just any rendering. The selection check is the other half: Preview
        // sits inside the selectable card, so without stopPropagation every preview click
        // would also toggle the selection.
        var previewButton = cards.GetByRole(AriaRole.Button, new() { Name = "Preview" }).First;
        if (await previewButton.CountAsync() > 0)
        {
            await previewButton.ClickAsync();
            await Expect(Page.Locator(".ben-content-picker-grid .ws-player, .ben-content-picker-grid video").First)
                .ToBeVisibleAsync(new() { Timeout = 45_000 });

            // The seed's 8-byte .wav stubs cannot decode, so a decode error is data, not product.
            // What must never appear again is the missing-module error — the whole
            // /js/wavesurfer folder was lost with Ben.Web.WebApp and nothing asserted it.
            var playerError = Page.Locator(".ben-content-picker-grid .ws-player-error");
            if (await playerError.CountAsync() > 0)
            {
                var text = await playerError.First.InnerTextAsync();
                TestContext.Out.WriteLine($"player reported: {text}");
                Assert.That(text, Does.Not.Contain("imported module"),
                    "the wavesurfer module itself failed to load — its assets are missing from the host");
            }

            await Expect(Page.Locator("#picker-select")).ToBeDisabledAsync();
        }

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

    // ── Item 175 sweep ────────────────────────────────────────────────────────

    /// <summary>
    /// The CMS ImageBanner editor was the second paste-a-GUID box on the site. This walks a
    /// throwaway page's Add Section dialog to the banner editor and asserts the box is gone and
    /// the picker opens in its place; the page is deleted again in finally (shared DB).
    /// </summary>
    [Test]
    public async Task The_cms_banner_editor_offers_a_picker_instead_of_a_guid_box()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var orgId = TghId;

        var stamp = Guid.NewGuid().ToString("N")[..8];
        var title = $"Playwright banner {stamp}";

        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/cms");
        await WaitUntilLoadedAsync();

        var dialog = Page.Locator(".modal.show");
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "New Page" }).First, dialog);
        await dialog.GetByLabel("Title", new() { Exact = false }).First.FillAsync(title);
        await dialog.GetByLabel("URL Slug", new() { Exact = false }).First.FillAsync($"playwright-banner-{stamp}");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).First.ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        try
        {
            // The row's FIRST button is Sections, which navigates to the page editor (item 169).
            var row = Main.Locator("tr", new() { HasTextString = title }).First;
            await ClickUntilUrlAsync(row.GetByRole(AriaRole.Button, new() { Name = "Sections" }), @"/cms/pages/");
            await WaitUntilLoadedAsync();

            // The section editor is an inline card, not a modal — HTML authoring gets full width.
            var sectionCard = Main.Locator(".card").Filter(new() { HasTextString = "New section" }).First;
            await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Add Section" }), sectionCard);
            await sectionCard.Locator("select").First.SelectOptionAsync(new SelectOptionValue { Label = "Image or banner" });

            // The whole point: no GUID box, a Choose button instead.
            await Expect(Page.Locator("#cms-banner-choose")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(Page.Locator("#cmssectioneditor-upload-file-id-8ad5")).ToHaveCountAsync(0);

            await Page.Locator("#cms-banner-choose").ClickAsync();
            await Expect(Page.Locator("#picker-search")).ToBeVisibleAsync(new() { Timeout = 10_000 });

            // Candidates or the honest empty sentence — never the load-failure one.
            var cards = Page.Locator(".ben-content-picker-grid .card");
            var empty = Page.Locator("#picker-empty");
            await Expect(cards.First.Or(empty)).ToBeVisibleAsync(new() { Timeout = 45_000 });

            if (await cards.CountAsync() > 0)
            {
                await cards.First.ClickAsync();
                await Page.Locator("#picker-select").ClickAsync();
                // The chosen-image card takes the place of the box.
                await Expect(Page.Locator("#cms-banner-chosen")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            }
            else
            {
                await Page.Keyboard.PressAsync("Escape");
            }

            // Close the section editor without saving: this test writes no section.
            await sectionCard.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = false }).First.ClickAsync();
        }
        finally
        {
            await DeleteCmsPageAsync(orgId, title);
        }
    }

    /// <summary>
    /// The clipart library was the third GUID box: SuperAdmin published editor artwork "by its
    /// file id". Now a picker over the caller's own media library; nothing is published here.
    /// </summary>
    [Test]
    public async Task The_clipart_library_offers_a_picker_instead_of_a_guid_box()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/video-assets");
        await WaitUntilLoadedAsync();

        var choose = Page.Locator("#asset-choose-file");
        await Expect(choose).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await Expect(Page.Locator("#adminvideoassets-upload-file-id-ace6")).ToHaveCountAsync(0);

        await ClickUntilAsync(choose, Page.Locator("#picker-search"));

        var cards = Page.Locator(".ben-content-picker-grid .card");
        var empty = Page.Locator("#picker-empty");
        await Expect(cards.First.Or(empty)).ToBeVisibleAsync(new() { Timeout = 45_000 });

        if (await cards.CountAsync() > 0)
        {
            await cards.First.ClickAsync();
            await Page.Locator("#picker-select").ClickAsync();

            // The choice fills the chosen-file label AND defaults the asset name from the stem.
            await Expect(Page.Locator("#asset-chosen-file")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            var name = await Page.Locator("#adminvideoassets-name-0e6a").InputValueAsync();
            Assert.That(name, Is.Not.Empty, "choosing a file should default the asset name");
        }
        else
        {
            await Page.Keyboard.PressAsync("Escape");
        }
        // Publish is never clicked: the clipart library is live data.
    }

    /// <summary>
    /// Cleanup goes through the API, not the UI: the first version drove the grid's
    /// More-actions dropdown and silently failed BOTH times it ran (the catch logged and the
    /// test stayed green), leaving orphan pages in the shared DB. A cleanup must be the most
    /// boring, least breakable path available — and it must be verified once, not trusted.
    /// </summary>
    /// <summary>
    /// The Add Logo dialog's From-Library tab was the last hand-rolled thumbnail grid — now the
    /// picker, same Visibility facet as the banner (the logo renders on the PUBLIC page through
    /// the same IsPublic-gated anonymous route). Cancelled without saving: no logo rows written.
    /// </summary>
    [Test]
    public async Task The_logo_dialog_offers_the_picker_instead_of_its_own_grid()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}/cms");
        await WaitUntilLoadedAsync();

        await OpenTabAsync("Logos", Main.GetByRole(AriaRole.Button, new() { Name = "Add Logo" }));

        var dialog = Page.Locator(".modal.show");
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Add Logo" }), dialog);

        var choose = Page.Locator("#logo-choose");
        await Expect(choose).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await ClickUntilAsync(choose, Page.Locator("#picker-search"));

        var cards = Page.Locator(".ben-content-picker-grid .card");
        var empty = Page.Locator("#picker-empty");
        await Expect(cards.First.Or(empty)).ToBeVisibleAsync(new() { Timeout = 45_000 });

        if (await cards.CountAsync() > 0)
        {
            await cards.First.ClickAsync();
            await Page.Locator("#picker-select").ClickAsync();
            await Expect(Page.Locator("#logo-chosen")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
        else
        {
            await Page.Keyboard.PressAsync("Escape");
        }

        // Cancel — the dialog's Save is never clicked, so no logo row is created.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).First.ClickAsync();
    }

    private async Task DeleteCmsPageAsync(string orgId, string title)
    {
        try
        {
            var login = await Page.APIRequest.PostAsync($"{ApiUrl}/login",
                new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
            var token = (await login.JsonAsync())?.GetProperty("accessToken").GetString() ?? "";
            var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

            var response = await Page.APIRequest.GetAsync(
                $"{ApiUrl}/api/organizations/{orgId}/pages", new() { Headers = auth });
            var pages = await response.JsonAsync();
            foreach (var p in pages!.Value.EnumerateArray())
            {
                if (p.GetProperty("pageTitle").GetString() != title) continue;
                var id = p.GetProperty("id").GetString();
                var del = await Page.APIRequest.DeleteAsync(
                    $"{ApiUrl}/api/organizations/{orgId}/pages/{id}", new() { Headers = auth });
                TestContext.Out.WriteLine($"cleanup: deleted \"{title}\" -> {del.Status}");
            }
        }
        catch (Exception ex)
        {
            // Best effort — a cleanup that throws would bury the real failure underneath it.
            TestContext.Out.WriteLine($"could not remove the test page \"{title}\": {ex.Message.Split('\n')[0]}");
        }
    }
}
