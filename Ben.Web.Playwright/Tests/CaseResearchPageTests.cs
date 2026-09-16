using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A research page: made from the Research tab, written in blocks, saved, kept private until published, and put in order.
/// </summary>
/// <remarks>
/// Beta feedback, 2026-09-14. Ben asked for research notes to become pages "like OneNote", with pictures, files, links and
/// places pasted in anywhere and moved about. The map block has its own fixture (CaseResearchMapBlockTests), and the page
/// at phone and tablet widths another (CaseResearchPageViewportTests).
/// </remarks>
[TestFixture]
[Category("CaseResearch")]
public class CaseResearchPageTests : ResearchPageTestBase
{
    [Test]
    public async Task NewPage_OpensThePage_AsADraftOnlyItsAuthorCanSee()
    {
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("County deed records"));

        await Expect(Page.Locator("[data-testid=research-page-draft-badge]")).ToContainTextAsync("only you can see it");
        await Expect(Page.Locator("[data-testid=block-page-empty]")).ToBeVisibleAsync();
        await Expect(Page.Locator("#research-page-save")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SaveNow_KeepsTheWords()
    {
        await OpenResearchTabAsync();
        var url = await CreatePageAsync(UniqueTitle("Save now"));

        await AddTextAsync("The deed was recorded in 1921.");
        await SaveNowAsync();

        // Still saved once the editor's own late report of the same words has arrived (it follows keystrokes by 100 ms):
        // the same words are not a new change.
        await Page.WaitForTimeoutAsync(1_000);
        await Expect(SaveStatus).ToHaveAttributeAsync("data-state", "Clean");

        await ReloadPageAsync(url);
        await Expect(Page.Locator("[data-testid=text-block]").First).ToContainTextAsync("The deed was recorded in 1921.");
    }

    [Test]
    public async Task Published_ShowsAMemberThePublishedWords_NotTheLaterDraft()
    {
        await OpenResearchTabAsync();
        var title = UniqueTitle("Obituaries");
        var url = await CreatePageAsync(title);

        await AddTextAsync("First version, published.");
        await PublishAsync();

        // A later change is a draft again, and stays the author's until they publish it.
        await AddTextAsync("Second version, still a draft.");
        await SaveNowAsync();
        await Expect(Page.Locator("[data-testid=research-page-differs]")).ToBeVisibleAsync();

        await LoginAsync(MemberEmail, MemberPassword);   // James — Paranormal365 member, reads cases
        await ReloadPageAsync(url);

        var reader = Page.Locator("[data-testid=block-reader]");
        await Expect(reader).ToContainTextAsync("First version, published.", new() { Timeout = 15_000 });
        await Expect(reader).Not.ToContainTextAsync("Second version");
        await Expect(Page.Locator("#research-page-save")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task UnpublishedPage_IsNotListedOrReadableForAnotherMember()
    {
        await OpenResearchTabAsync();
        var shownTitle = UniqueTitle("Published beside it");
        var shownUrl = await CreatePageAsync(shownTitle);
        await AddTextAsync("Everyone in the group may read this.");
        await PublishAsync();

        await Page.GotoAsync(shownUrl[..shownUrl.IndexOf("/research/", StringComparison.Ordinal)] + "?tab=research");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#research-new-page")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var hiddenTitle = UniqueTitle("Private draft");
        var url = await CreatePageAsync(hiddenTitle);
        await AddTextAsync("Not for anyone else yet.");
        await SaveNowAsync();
        var caseUrl = url[..url.IndexOf("/research/", StringComparison.Ordinal)];

        await LoginAsync(MemberEmail, MemberPassword);   // James — Paranormal365 member, reads cases
        await Page.GotoAsync(caseUrl + "?tab=research");
        await WaitForTheCircuitAsync();

        // The published page is the proof the list has loaded; only then does the draft's absence mean anything.
        await Expect(Main.GetByRole(AriaRole.Link, new() { Name = shownTitle, Exact = true })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Main.GetByRole(AriaRole.Link, new() { Name = hiddenTitle, Exact = true })).ToHaveCountAsync(0);

        await ReloadPageAsync(url);
        await Expect(Page.Locator("[data-testid=research-page]")).ToContainTextAsync("This research page hasn't been published yet.");
        await Expect(Page.Locator("[data-testid=block-reader], [data-testid=block-page]")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task PastedPicture_BecomesAPictureBlock_AndIsKeptInFilesAndLinks()
    {
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("Pasted picture"));

        await BlockPage.FocusAsync();
        await Page.EvaluateAsync(@"async () => {
            const canvas = document.createElement('canvas');
            canvas.width = 64; canvas.height = 48;
            const g = canvas.getContext('2d'); g.fillStyle = '#7a3'; g.fillRect(0, 0, 64, 48);
            const blob = await new Promise(r => canvas.toBlob(r, 'image/png'));
            const data = new DataTransfer();
            data.items.add(new File([blob], 'headstone.png', { type: 'image/png' }));
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");

        var picture = Page.Locator("[data-testid=image-block] img").First;
        await Expect(picture).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.WaitForFunctionAsync("img => img.complete && img.naturalWidth > 0", await picture.ElementHandleAsync(), new() { Timeout = 20_000 });
        await Expect(Page.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task PastedWebAddress_BecomesALinkBlock_AndIsKeptInFilesAndLinks()
    {
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("Pasted link"));

        await BlockPage.FocusAsync();
        await Page.EvaluateAsync(@"() => {
            const data = new DataTransfer();
            data.setData('text/plain', 'https://example.com/county-archive');
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");

        // Whether or not the other site answers, the block and the rail row both name the address.
        var link = Page.Locator("[data-testid=link-block]").First;
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(link.Locator("a[href='https://example.com/county-archive']").First).ToBeAttachedAsync();
        await Expect(Page.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Network")]
    public async Task PastedWebAddress_ShowsThePagesOwnTitle()
    {
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("Link card"));

        await BlockPage.FocusAsync();
        await Page.EvaluateAsync(@"() => {
            const data = new DataTransfer();
            data.setData('text/plain', 'https://example.com/');
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");

        await Expect(Page.Locator("[data-testid=link-block]").First).ToContainTextAsync("Example Domain", new() { Timeout = 30_000 });
    }

    [Test]
    public async Task MoveDown_FromTheBlockMenu_ChangesTheOrder_AndTheOrderIsSaved()
    {
        await OpenResearchTabAsync();
        var url = await CreatePageAsync(UniqueTitle("Menu move"));
        await AddTextAsync("Alpha");
        await AddTextAsync("Beta");

        // A click on the block's handle — not a drag — opens its options.
        var first = Page.Locator("section[data-kind=text]").First;
        await first.Locator("[data-testid=block-menu-toggle]").ClickAsync();
        await Page.Locator("[data-testid=block-menu]").GetByRole(AriaRole.Menuitem, new() { Name = "Move down" }).ClickAsync();
        await SaveNowAsync();

        await ReloadPageAsync(url);
        await Expect(Page.Locator("section[data-kind=text]")).ToHaveCountAsync(2);
        Assert.That(await TextBlockWordsAsync(), Is.EqualTo(new[] { "Beta", "Alpha" }));
    }

    [Test]
    public async Task DraggingAHandle_MovesTheBlock()
    {
        await OpenResearchTabAsync();
        var url = await CreatePageAsync(UniqueTitle("Drag move"));
        await AddTextAsync("Alpha");
        await AddTextAsync("Beta");

        // Measured once the page has finished moving: opening Beta closes Alpha's editor, which shrinks Alpha and lifts
        // Beta's handle — a drag aimed from the earlier layout presses on empty space.
        var blocks = Page.Locator("section[data-kind=text]");
        await Expect(blocks.First.Locator("[data-testid=text-block]")).ToBeVisibleAsync();
        await Expect(blocks.Nth(1).Locator("[data-testid=text-block-editor] .k-toolbar")).ToBeVisibleAsync();

        var betaHandle = blocks.Nth(1).Locator("[data-block-handle]");
        await betaHandle.HoverAsync();
        var handle = (await betaHandle.BoundingBoxAsync())!;
        var top = (await blocks.First.BoundingBoxAsync())!;

        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(handle.X + handle.Width / 2, top.Y + 4, new() { Steps = 8 });
        await Page.Mouse.UpAsync();

        await Page.WaitForFunctionAsync(@"() => {
            const words = [...document.querySelectorAll('section[data-kind=text]')]
                .map(s => (s.querySelector('[contenteditable]') ?? s.querySelector('[data-testid=text-block]'))?.innerText.trim());
            return words.join('|') === 'Beta|Alpha';
        }", null, new() { Timeout = 10_000 });
        await SaveNowAsync();
        await ReloadPageAsync(url);
        Assert.That(await TextBlockWordsAsync(), Is.EqualTo(new[] { "Beta", "Alpha" }));
    }

    [Test]
    public async Task InsertFromTheRail_PutsALinkOnThePage()
    {
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("Rail insert"));

        var linkBox = Page.Locator("#research-rail-link");
        if (!await linkBox.IsVisibleAsync()) await Page.Locator("[data-testid=rail-toggle]").ClickAsync();
        await linkBox.FillAsync("https://example.com/burial-register");
        await Page.Locator("[data-testid=research-rail-add-link]").ClickAsync();

        var row = Page.Locator("[data-testid=research-rail-item]");
        await Expect(row).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=link-block]")).ToHaveCountAsync(0);

        await row.First.GetByRole(AriaRole.Button, new() { Name = "on the page" }).ClickAsync();

        await Expect(Page.Locator("[data-testid=link-block] a[href='https://example.com/burial-register']").First)
            .ToBeAttachedAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task APublishedDatedPage_TakesItsPlaceOnTheTimeline_AndOpensFromThere()
    {
        await OpenResearchTabAsync();
        var title = UniqueTitle("Burial register");
        var url = await CreatePageAsync(title);
        await AddTextAsync("Both owners are buried at the county cemetery.");

        // Add a date opens a date box already holding one; the day is then chosen from its calendar, as with a mouse.
        await Page.Locator("#research-page-when-add").ClickAsync();
        var when = Page.Locator("#research-page-when");
        await Expect(when).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^\d{2}/\d{2}/\d{4}$"), new() { Timeout = 10_000 });
        await Page.Locator(".ben-date-field:has(#research-page-when) .ben-date-field__open").ClickAsync();
        var firstOfMonth = Page.Locator(".k-calendar .k-calendar-td:not(.k-other-month)").First;
        await Expect(firstOfMonth).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var day = DateTime.Parse((await firstOfMonth.GetAttributeAsync("title"))!, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        await firstOfMonth.ClickAsync();
        await Expect(when).ToHaveValueAsync(day.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture), new() { Timeout = 10_000 });

        await PublishAsync();

        await Page.GotoAsync(url[..url.IndexOf("/research/", StringComparison.Ordinal)] + "?tab=timeline");
        await WaitForTheCircuitAsync();
        var open = Main.Locator($"[data-testid=timeline-open-research-page][href$='{url[url.IndexOf("/research/", StringComparison.Ordinal)..]}']");
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Main.GetByText(title, new() { Exact = true }).First).ToBeVisibleAsync();

        await ClickUntilUrlAsync(open, PageUrl.ToString());
        await Expect(Page.Locator("[data-testid=research-page]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task TheDate_IsNeverAnEmptyBox_AndCanBeTakenAwayAgain()
    {
        // An empty Telerik date box rebuilds the whole date the first time a segment is stepped (2026-09-09). A new page
        // is undated, so it offers Add a date rather than an empty box; added, the box holds a whole date.
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("Dating"));

        await Expect(Page.Locator("#research-page-when")).ToHaveCountAsync(0);
        await Page.Locator("#research-page-when-add").ClickAsync();
        await Expect(Page.Locator("#research-page-when")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^\d{2}/\d{2}/\d{4}$"), new() { Timeout = 10_000 });
        await Expect(Page.Locator("#research-page-when-time")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^\d{2}:00 (AM|PM)$"));

        await Page.Locator("#research-page-when-remove").ClickAsync();
        await Expect(Page.Locator("#research-page-when")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#research-page-when-add")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Deleting_APage_FromTheResearchTab_AsksFirst()
    {
        await OpenResearchTabAsync();
        var title = UniqueTitle("To delete");
        var url = await CreatePageAsync(title);

        await Page.GotoAsync(url[..url.IndexOf("/research/", StringComparison.Ordinal)] + "?tab=research");
        await WaitForTheCircuitAsync();
        var link = Main.GetByRole(AriaRole.Link, new() { Name = title, Exact = true });
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var confirm = Page.Locator("[data-testid=research-delete-message]");
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = $"Delete {title}" }), confirm);
        await Expect(confirm).ToContainTextAsync(title);

        await Page.Locator(".modal.show").GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(link).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    /// <summary>The words of each text block in page order — from its editor when it is the one open, else as drawn.</summary>
    private Task<string[]> TextBlockWordsAsync() =>
        Page.EvaluateAsync<string[]>(@"() => [...document.querySelectorAll('section[data-kind=text]')]
            .map(s => ((s.querySelector('[contenteditable]') ?? s.querySelector('[data-testid=text-block]'))?.innerText ?? '').trim())");
}
