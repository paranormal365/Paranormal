using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Every function of the research page's block editor, each driven the way a person uses it and each checked after the
/// page has been saved and loaded again — what matters is what was kept, not what the screen showed for a moment.
/// </summary>
/// <remarks>
/// Ben, 2026-09-14: "The editor used by research needs to be solid. Validate and verify all functionality of the research
/// editor." CaseResearchPageTests covers making, saving, publishing, pasting a picture or an address and moving a block;
/// CaseResearchMapBlockTests the map's taps and routes; CaseResearchPageViewportTests the screen sizes. This fixture covers
/// the rest: the title, formatting, pasted and dropped text and files, the Add bar's pictures, files and links, captions,
/// the block menu and keyboard, touch dragging, leaving with unsaved work, autosave, two people and two tabs, the Files and
/// links rail, the size limit, the map's search and labels and its limit, and what a member reads afterwards.
/// </remarks>
[TestFixture]
[Category("CaseResearch")]
public class CaseResearchEditorTests : ResearchPageTestBase
{
    private ILocator TextBlocks => Page.Locator("section[data-kind=text]");
    private ILocator OpenEditor => Page.Locator("[data-testid=text-block-editor] .ProseMirror").First;
    private ILocator Rail => Page.Locator("[data-testid=research-rail]");

    /// <summary>The stored HTML of each text block, in page order, as the reader draws it after a reload.</summary>
    private Task<string[]> SavedTextHtmlAsync() =>
        Page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('section[data-kind=text] [data-testid=text-block]')].map(e => e.innerHTML.trim())");

    private async Task PasteAsync(string script)
    {
        await BlockPage.FocusAsync();
        await Page.EvaluateAsync(script);
    }

    /// <summary>A file made in the page, as a person's clipboard or drop would carry it.</summary>
    private const string MakeTextFile = "new File(['Deed book 1162, page 88.\\n'], 'deed-notes.txt', { type: 'text/plain' })";

    /// <summary>Clicks just past the last word of a text block, and does not wait for anything.</summary>
    private async Task ClickJustAfterTheWordsOnlyAsync(ILocator words, IPage? page = null)
    {
        var at = await words.EvaluateAsync<float[]>(@"el => {
            const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, { acceptNode: n => n.textContent.trim() ? 1 : 3 });
            let last = null;
            for (let n = walker.nextNode(); n; n = walker.nextNode()) last = n;
            const range = document.createRange();
            range.setStart(last, last.textContent.length);
            range.setEnd(last, last.textContent.length);
            const r = range.getBoundingClientRect();
            return [r.right + 3, r.top + r.height / 2];
        }");
        await (page ?? Page).Mouse.ClickAsync(at[0], at[1]);
    }

    /// <summary>
    /// Clicks just past the last word of a text block — drawn or open — and waits for its editor to hold the cursor there.
    /// </summary>
    /// <remarks>
    /// How a person adds to the end of what is written. The End key is not used: on macOS it does not move the cursor in
    /// the browser, so a test pressing it types at the start.
    /// </remarks>
    private async Task ClickJustAfterTheWordsAsync(IPage page, ILocator words)
    {
        await ClickJustAfterTheWordsOnlyAsync(words, page);
        await Expect(page.Locator("[data-testid=text-block-editor] .ProseMirror").First).ToBeFocusedAsync(new() { Timeout = 10_000 });
        await page.WaitForFunctionAsync(@"() => {
            const s = getSelection();
            if (!s.rangeCount || !s.isCollapsed) return false;
            const n = s.focusNode;
            const pm = (n.nodeType === 1 ? n : n.parentElement).closest('.ProseMirror');
            if (!pm) return false;
            const after = document.createRange();
            after.setStart(n, s.focusOffset);
            after.setEnd(pm, pm.childNodes.length);
            return after.toString().trim() === '';
        }", null, new() { Timeout = 10_000 });
    }

    private async Task<string> NewPageAsync(string what)
    {
        await OpenResearchTabAsync();
        return await CreatePageAsync(UniqueTitle(what));
    }

    // ── The title ──────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Renaming_ThePage_IsSaved_AndAnEmptyTitleIsRefusedInWords()
    {
        var url = await NewPageAsync("Before rename");
        var renamed = UniqueTitle("After rename");

        await Page.Locator("#research-page-title").FillAsync(renamed);
        await Page.Locator("#research-page-title").PressAsync("Tab");
        await SaveNowAsync();
        await ReloadPageAsync(url);
        await Expect(Page.Locator("#research-page-title")).ToHaveValueAsync(renamed);

        await Page.Locator("#research-page-title").FillAsync("");
        await Page.Locator("#research-page-title").PressAsync("Tab");
        await Page.Locator("#research-page-save").ClickAsync();
        await Expect(SaveStatus).ToHaveAttributeAsync("data-state", "Failed", new() { Timeout = 15_000 });
        await Expect(SaveStatus).ToContainTextAsync("Give the page a title to save it.");

        await ReloadPageAsync(url);
        await Expect(Page.Locator("#research-page-title")).ToHaveValueAsync(renamed);
    }

    // ── Words ──────────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Bold_AndAList_AreKept()
    {
        var url = await NewPageAsync("Formatting");
        await AddTextAsync("Owners ");
        await Page.Keyboard.PressAsync("ControlOrMeta+b");
        await Page.Keyboard.TypeAsync("in order");
        await Page.Keyboard.PressAsync("ControlOrMeta+b");
        await Page.Keyboard.PressAsync("Enter");
        // A toolbar tool is a trip to the server (4.5): a person sees the bullet appear before they type, and so does this.
        await Page.Locator("[data-testid=text-block-editor] .k-toolbar button[title='Insert unordered list']").ClickAsync();
        await Expect(OpenEditor.Locator("ul li")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await Page.Keyboard.TypeAsync("Harold Whitcomb");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("June Whitcomb");
        await SaveNowAsync();

        await ReloadPageAsync(url);
        var html = (await SavedTextHtmlAsync()).Single();
        Assert.That(html, Does.Match(@"<strong>in order</strong>"), html);
        Assert.That(html, Does.Match(@"<ul>\s*<li>(<p>)?Harold Whitcomb(</p>)?</li>\s*<li>(<p>)?June Whitcomb(</p>)?</li>\s*</ul>"), html);
    }

    [Test]
    public async Task Typing_InOneBlock_ThenAnother_KeepsBoth()
    {
        var url = await NewPageAsync("Two blocks");
        await AddTextAsync("First block's words");
        await AddTextAsync("Second block's words");

        // Straight back into the first: what was typed in the second goes into the page as its editor closes.
        await ClickJustAfterTheWordsAsync(Page, TextBlocks.First.Locator("[data-testid=text-block]"));
        await Page.Keyboard.TypeAsync(", and more");
        await SaveNowAsync();

        await ReloadPageAsync(url);
        await Expect(TextBlocks).ToHaveCountAsync(2);
        var html = await SavedTextHtmlAsync();
        Assert.That(html[0], Does.Contain("First block's words, and more"));
        Assert.That(html[1], Does.Contain("Second block's words"));
    }

    [Test]
    public async Task ClickingIntoASentence_PutsTheCursorWhereTheClickLanded()
    {
        var url = await NewPageAsync("Click into words");
        await AddTextAsync("Buried a year apart");
        await SaveNowAsync();
        await ReloadPageAsync(url);

        // Between "year" and " apart" on the drawn block. The editor draws the same words elsewhere — padding, a toolbar —
        // so a cursor placed by screen position landed at the start (UI test pass 5.3).
        var at = await TextBlocks.First.Locator("[data-testid=text-block]").EvaluateAsync<float[]>(@"el => {
            const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
            let node = walker.nextNode();
            while (node && !node.textContent.includes('year')) node = walker.nextNode();
            const range = document.createRange();
            const end = node.textContent.indexOf('year') + 4;
            range.setStart(node, end - 1);
            range.setEnd(node, end);
            const r = range.getBoundingClientRect();
            return [r.right - 1, r.top + r.height / 2];
        }");
        await Page.Mouse.ClickAsync(at[0], at[1]);
        await Expect(OpenEditor).ToBeFocusedAsync(new() { Timeout = 10_000 });
        await Page.Keyboard.TypeAsync(" and a day");
        await SaveNowAsync();

        await ReloadPageAsync(url);
        Assert.That((await SavedTextHtmlAsync()).Single(), Does.Contain("Buried a year and a day apart"));
    }

    [Test]
    public async Task WordsTypedTheMomentABlockIsClicked_AreKept()
    {
        var url = await NewPageAsync("Type at once");
        await AddTextAsync("Recorded");
        await SaveNowAsync();
        await ReloadPageAsync(url);

        // No waiting for the editor: a person clicks and types. The editor arrives after a trip to the server, and the
        // keys typed before it did were lost (UI test pass 5.14).
        await ClickJustAfterTheWordsOnlyAsync(TextBlocks.First.Locator("[data-testid=text-block]"));
        await Page.Keyboard.TypeAsync(" in 1921");
        await Expect(OpenEditor).ToHaveTextAsync("Recorded in 1921", new() { Timeout = 10_000 });
        await SaveNowAsync();

        await ReloadPageAsync(url);
        Assert.That((await SavedTextHtmlAsync()).Single(), Does.Contain("Recorded in 1921"));
    }

    [Test]
    public async Task PastedText_OnThePage_BecomesATextBlock_AndItsFormattingIsCleaned()
    {
        var url = await NewPageAsync("Pasted text");

        await PasteAsync(@"() => {
            const data = new DataTransfer();
            data.setData('text/plain', 'Plain first line\n\nPlain second paragraph');
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");
        await Expect(TextBlocks).ToHaveCountAsync(1, new() { Timeout = 10_000 });

        await BlockPage.FocusAsync();
        await PasteAsync(@"() => {
            const data = new DataTransfer();
            data.setData('text/plain', 'Kept bold, nothing else');
            data.setData('text/html', '<html><head><style>p.MsoNormal { margin: 0 }</style></head><body><p onclick=""alert(1)"">Kept <b>bold</b>, nothing else</p><script>alert(2)</script></body></html>');
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");
        await Expect(TextBlocks).ToHaveCountAsync(2, new() { Timeout = 10_000 });
        await Expect(OpenEditor).ToContainTextAsync("Kept bold, nothing else", new() { Timeout = 10_000 });
        await SaveNowAsync();

        await ReloadPageAsync(url);
        var html = await SavedTextHtmlAsync();
        Assert.That(html[0], Does.Match(@"<p>Plain first line</p>\s*<p>Plain second paragraph</p>"), html[0]);
        Assert.That(html[1], Does.Match(@"<(b|strong)>bold</\1>"), html[1]);
        // Neither the script nor the stylesheet Word puts on the clipboard — tags or what is inside them (5.13).
        Assert.That(string.Join("", html), Does.Not.Contain("<script").And.Not.Contain("onclick").And.Not.Contain("alert(")
            .And.Not.Contain("MsoNormal").And.Not.Contain("margin"));
    }

    [Test]
    public async Task PastedText_InsideATextBlock_StaysInThatBlock()
    {
        var url = await NewPageAsync("Paste inside");
        await AddTextAsync("Before ");
        await OpenEditor.EvaluateAsync(@"el => {
            const data = new DataTransfer();
            data.setData('text/plain', 'pasted words');
            el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");
        await Expect(OpenEditor).ToContainTextAsync("Before pasted words", new() { Timeout = 10_000 });
        await SaveNowAsync();

        await ReloadPageAsync(url);
        await Expect(TextBlocks).ToHaveCountAsync(1);
        Assert.That((await SavedTextHtmlAsync()).Single(), Does.Contain("Before pasted words"));
    }

    // ── Files ──────────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ADroppedFile_BecomesAFileBlock_ThatDownloads()
    {
        var url = await NewPageAsync("Dropped file");

        await Page.EvaluateAsync($@"() => {{
            const data = new DataTransfer();
            data.items.add({MakeTextFile});
            const page = document.querySelector('[data-testid=block-page]');
            page.dispatchEvent(new DragEvent('dragover', {{ dataTransfer: data, bubbles: true, cancelable: true }}));
            page.dispatchEvent(new DragEvent('drop', {{ dataTransfer: data, bubbles: true, cancelable: true }}));
        }}");

        var block = Page.Locator("[data-testid=file-block]").First;
        await Expect(block).ToContainTextAsync("deed-notes.txt", new() { Timeout = 30_000 });
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(1);
        await SaveNowAsync();

        await ReloadPageAsync(url);
        var href = await Page.Locator("[data-testid=file-block] a.ben-file-block__download").First.GetAttributeAsync("href");
        var status = await Page.EvaluateAsync<int>("async href => (await fetch(href)).status", href);
        Assert.That(status, Is.EqualTo(200), $"the dropped file's download answered {status}");
    }

    [Test]
    public async Task TheAddBar_TakesAPictureAndAFile_FromTheFilePicker()
    {
        var url = await NewPageAsync("Chosen files");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

        await Page.Locator("[data-testid=block-add-bar] input[type=file][accept='image/*']").SetInputFilesAsync(
            new FilePayload { Name = "porch.png", MimeType = "image/png", Buffer = png });
        await Expect(Page.Locator("[data-testid=image-block] img")).ToHaveCountAsync(1, new() { Timeout = 30_000 });

        await Page.Locator("[data-testid=block-add-bar] input[type=file]:not([accept])").SetInputFilesAsync(
            new FilePayload { Name = "census-1910.txt", MimeType = "text/plain", Buffer = "Census, 1910."u8.ToArray() });
        await Expect(Page.Locator("[data-testid=file-block]")).ToContainTextAsync("census-1910.txt", new() { Timeout = 30_000 });
        await SaveNowAsync();

        await ReloadPageAsync(url);
        await Expect(Page.Locator("section[data-kind=image]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("section[data-kind=file]")).ToHaveCountAsync(1);
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task APictureAndAFile_KeepTheirCaptions_AndAMemberReadsEveryKindOfBlock()
    {
        var url = await NewPageAsync("Captions");
        await AddTextAsync("Words for the reader.");
        await PasteAsync(@"async () => {
            const canvas = document.createElement('canvas'); canvas.width = 40; canvas.height = 30;
            canvas.getContext('2d').fillRect(0, 0, 40, 30);
            const blob = await new Promise(r => canvas.toBlob(r, 'image/png'));
            const data = new DataTransfer();
            data.items.add(new File([blob], 'front-door.png', { type: 'image/png' }));
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");
        var alt = Page.Locator("[data-testid=image-block] input[id$='-alt']");
        await Expect(alt).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await alt.FillAsync("The front door, painted red");
        await alt.PressAsync("Tab");
        await Page.Locator("[data-testid=image-block] input[id$='-caption']").FillAsync("Taken from the street");
        await Page.Locator("[data-testid=image-block] input[id$='-caption']").PressAsync("Tab");

        await PasteAsync($@"() => {{
            const data = new DataTransfer();
            data.items.add({MakeTextFile});
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', {{ clipboardData: data, bubbles: true, cancelable: true }}));
        }}");
        var fileCaption = Page.Locator("section[data-kind=file] input[id$='-caption']");
        await Expect(fileCaption).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await fileCaption.FillAsync("Transcribed at the county office");
        await fileCaption.PressAsync("Tab");

        await PasteAsync(@"() => {
            const data = new DataTransfer();
            data.setData('text/plain', 'https://example.com/county-records');
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");
        await Expect(Page.Locator("section[data-kind=link]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await PublishAsync();

        await LoginAsync(MemberEmail, MemberPassword);
        await ReloadPageAsync(url);
        var reader = Page.Locator("[data-testid=block-reader]");
        await Expect(reader.Locator("section[data-kind=text]")).ToContainTextAsync("Words for the reader.");
        await Expect(reader.Locator("[data-testid=image-block] img")).ToHaveAttributeAsync("alt", "The front door, painted red");
        await Expect(reader.Locator("[data-testid=image-block] figcaption")).ToHaveTextAsync("Taken from the street");
        await Page.WaitForFunctionAsync("() => { const i = document.querySelector('[data-testid=block-reader] [data-testid=image-block] img'); return i && i.complete && i.naturalWidth > 0; }", null, new() { Timeout = 20_000 });
        await Expect(reader.Locator("[data-testid=file-block]")).ToContainTextAsync("Transcribed at the county office");
        await Expect(reader.Locator("[data-testid=file-block] a.ben-file-block__download")).ToHaveCountAsync(1);
        await Expect(reader.Locator("[data-testid=link-block] a[href='https://example.com/county-records']").First).ToBeAttachedAsync();
        await Expect(reader.Locator("input, [data-block-handle], [data-testid=link-block-refresh]")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task AFileOverTheLimit_IsRefusedInWords_AndNothingIsAdded()
    {
        await NewPageAsync("Too large");
        await PasteAsync(@"() => {
            const big = new File([new Uint8Array(51 * 1024 * 1024)], 'survey-video.mov', { type: 'video/quicktime' });
            const data = new DataTransfer();
            data.items.add(big);
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");
        await Expect(Page.Locator("[data-testid=block-notice]")).ToContainTextAsync("\"survey-video.mov\" is 51 MB. Files added to a page can be up to 50 MB.", new() { Timeout = 15_000 });
        await Expect(Page.Locator("section[data-block-id]")).ToHaveCountAsync(0);
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(0);
    }

    // ── Links ──────────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheAddBarsLink_TakesATypedAddress_AndItsCardCanBeRefreshed()
    {
        var url = await NewPageAsync("Typed link");
        await Page.Locator("[data-testid=block-add-link]").ClickAsync();
        var box = Page.Locator("[data-testid=block-add-bar] ~ * input[type=url], .ben-blocks__link-entry input").First;
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await box.FillAsync("https://example.com/obituaries");
        await box.PressAsync("Enter");

        var link = Page.Locator("[data-testid=link-block]").First;
        await Expect(link.Locator("a[href='https://example.com/obituaries']").First).ToBeAttachedAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".ben-blocks__link-entry")).ToHaveCountAsync(0);

        var refresh = Page.Locator("[data-testid=link-block-refresh]");
        await refresh.ClickAsync();
        await Expect(refresh).ToContainTextAsync("Refresh card", new() { Timeout = 30_000 });
        await Expect(refresh).ToBeEnabledAsync();
        await SaveNowAsync();

        await ReloadPageAsync(url);
        await Expect(Page.Locator("section[data-kind=link] a[href='https://example.com/obituaries']").First).ToBeAttachedAsync();
    }

    [Test]
    public async Task ANonWebAddress_IsRefusedInWords()
    {
        await NewPageAsync("Bad link");
        await Page.Locator("[data-testid=block-add-link]").ClickAsync();
        var box = Page.Locator(".ben-blocks__link-entry input").First;
        await box.FillAsync("javascript:alert(1)");
        await box.PressAsync("Enter");
        await Expect(Page.Locator("[data-testid=block-notice]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page.Locator("section[data-kind=link]")).ToHaveCountAsync(0);
    }

    // ── Moving and removing ────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheBlockMenu_AddsTextBelow_AndDeletesABlock()
    {
        var url = await NewPageAsync("Menu");
        await AddTextAsync("One");
        await AddTextAsync("Three");

        var first = TextBlocks.First;
        await first.Locator("[data-testid=block-menu-toggle]").ClickAsync();
        await Page.Locator("[data-testid=block-menu]").GetByRole(AriaRole.Menuitem, new() { Name = "Add text below" }).ClickAsync();
        await Expect(TextBlocks).ToHaveCountAsync(3);
        await Expect(TextBlocks.Nth(1).Locator("[data-testid=text-block-editor]")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await OpenEditor.ClickAsync();
        await Page.Keyboard.TypeAsync("Two");

        var last = TextBlocks.Nth(2);
        await last.Locator("[data-testid=block-menu-toggle]").ClickAsync();
        await Page.Locator("[data-testid=block-menu]").GetByRole(AriaRole.Menuitem, new() { Name = "Delete block" }).ClickAsync();
        await Expect(TextBlocks).ToHaveCountAsync(2);
        await SaveNowAsync();

        await ReloadPageAsync(url);
        var html = await SavedTextHtmlAsync();
        Assert.That(html.Length, Is.EqualTo(2));
        Assert.That(html[0], Does.Contain("One"));
        Assert.That(html[1], Does.Contain("Two"));
    }

    [Test]
    public async Task AltAndTheArrowKeys_MoveTheBlockWhoseHandleHasFocus()
    {
        var url = await NewPageAsync("Keyboard move");
        await AddTextAsync("Alpha");
        await AddTextAsync("Beta");
        await AddTextAsync("Gamma");

        await TextBlocks.First.Locator("[data-block-handle]").FocusAsync();
        await Page.Keyboard.PressAsync("Alt+ArrowDown");
        await Expect(TextBlocks.Nth(1)).ToContainTextAsync("Alpha", new() { Timeout = 10_000 });
        // Focus follows the block, so a second press carries it further.
        await Page.Keyboard.PressAsync("Alt+ArrowDown");
        await Expect(TextBlocks.Nth(2)).ToContainTextAsync("Alpha", new() { Timeout = 10_000 });
        await SaveNowAsync();

        await ReloadPageAsync(url);
        var html = await SavedTextHtmlAsync();
        Assert.That(html.Select(h => Regex.Replace(h, "<[^>]+>", "")), Is.EqualTo(new[] { "Beta", "Gamma", "Alpha" }));
    }

    [Test]
    public async Task OnATouchScreen_AHeldHandleDrags_AndAQuickSwipeScrollsInstead()
    {
        var url = await NewPageAsync("Touch drag");
        await AddTextAsync("Alpha");
        await AddTextAsync("Beta");
        await Expect(TextBlocks.First.Locator("[data-testid=text-block]")).ToBeVisibleAsync();

        // Touch pointer events on Beta's handle, as a phone sends them. First a swipe that moves before the hold has
        // lasted: that is a scroll, and nothing moves.
        const string touch = @"async ([moveFirst, holdMs]) => {
            const blocks = [...document.querySelectorAll('section[data-kind=text]')];
            const handle = blocks[1].querySelector('[data-block-handle]');
            const h = handle.getBoundingClientRect(), top = blocks[0].getBoundingClientRect();
            const at = (x, y, type) => handle.dispatchEvent(new PointerEvent(type, { pointerId: 7, pointerType: 'touch', isPrimary: true, clientX: x, clientY: y, bubbles: true, cancelable: true, button: 0 }));
            const x = h.left + h.width / 2, y = h.top + h.height / 2;
            at(x, y, 'pointerdown');
            if (moveFirst) at(x, y - 30, 'pointermove');
            await new Promise(r => setTimeout(r, holdMs));
            for (let step = 1; step <= 6; step++) at(x, y + (top.top + 4 - y) * step / 6, 'pointermove');
            at(x, top.top + 4, 'pointerup');
        }";
        await Page.EvaluateAsync(touch, new object[] { true, 400 });
        await Expect(TextBlocks.First).ToContainTextAsync("Alpha");

        await Page.EvaluateAsync(touch, new object[] { false, 400 });
        await Expect(TextBlocks.First).ToContainTextAsync("Beta", new() { Timeout = 10_000 });
        await SaveNowAsync();

        await ReloadPageAsync(url);
        Assert.That((await SavedTextHtmlAsync()).Select(h => Regex.Replace(h, "<[^>]+>", "")), Is.EqualTo(new[] { "Beta", "Alpha" }));
    }

    // ── Saving without asking ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task LeavingWithUnsavedWords_SavesThemOnTheWayOut()
    {
        var url = await NewPageAsync("Leave");
        await AddTextAsync("Written just before leaving");
        await Expect(SaveStatus).ToHaveAttributeAsync("data-state", "Dirty", new() { Timeout = 10_000 });

        await Page.GetByRole(AriaRole.Link, new() { Name = "Research on this case" }).ClickAsync();
        await Expect(Page.Locator("#research-new-page")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await ReloadPageAsync(url);
        Assert.That((await SavedTextHtmlAsync()).Single(), Does.Contain("Written just before leaving"));
    }

    [Test]
    [Category("Slow")]
    public async Task AChange_SavesItselfAboutAMinuteLater()
    {
        var url = await NewPageAsync("Autosave");
        await AddTextAsync("Saved without pressing anything");
        await Expect(SaveStatus).ToHaveAttributeAsync("data-state", "Dirty", new() { Timeout = 10_000 });
        await Expect(SaveStatus).ToHaveAttributeAsync("data-state", "Clean", new() { Timeout = 90_000 });
        await Expect(SaveStatus).ToContainTextAsync("Saved");

        await ReloadPageAsync(url);
        Assert.That((await SavedTextHtmlAsync()).Single(), Does.Contain("Saved without pressing anything"));
    }

    // ── Two people, two tabs ───────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task APageSavedInAnotherTab_StopsThisTabFromOverwritingIt()
    {
        var url = await NewPageAsync("Two tabs");
        await AddTextAsync("From the first tab");
        await SaveNowAsync();

        var second = await Context.NewPageAsync();
        await second.GotoAsync(url);
        await Expect(second.Locator("[data-testid=block-page]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ClickJustAfterTheWordsAsync(second, second.Locator("[data-testid=text-block]").First);
        await second.Keyboard.TypeAsync(" and the second");
        await SaveNowAsync(second);
        await second.CloseAsync();

        // The first tab still holds the older copy. Its next save must not overwrite the second tab's work.
        await ClickJustAfterTheWordsAsync(Page, OpenEditor);
        await Page.Keyboard.TypeAsync(" (stale)");
        await Page.Locator("#research-page-save").ClickAsync();
        await Expect(SaveStatus).ToHaveAttributeAsync("data-state", "Conflict", new() { Timeout = 20_000 });
        await Expect(SaveStatus).ToContainTextAsync("saved somewhere else since you opened it");

        await ReloadPageAsync(url);
        var kept = (await SavedTextHtmlAsync()).Single();
        Assert.That(kept, Does.Contain("From the first tab and the second"));
        Assert.That(kept, Does.Not.Contain("stale"));
    }

    [Test]
    public async Task SomebodyElsesUnpublishedChanges_LeaveThePageToReadOnly()
    {
        var url = await NewPageAsync("Held");
        await AddTextAsync("Published words");
        await PublishAsync();
        await AddTextAsync("Sarah's unpublished words");
        await SaveNowAsync();

        // The SuperAdmin may edit any case, so what they meet is the hold, not a missing grant.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await ReloadPageAsync(url);
        await Expect(Page.Locator("[data-testid=research-page-held]")).ToContainTextAsync("has unpublished changes to it", new() { Timeout = 15_000 });
        await Expect(Page.Locator("[data-testid=block-reader]")).ToContainTextAsync("Published words");
        await Expect(Page.Locator("[data-testid=block-reader]")).Not.ToContainTextAsync("unpublished words");
        await Expect(Page.Locator("#research-page-save, #research-page-publish, [data-testid=block-page]")).ToHaveCountAsync(0);
    }

    // ── The Files and links rail ───────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Insert_PutsALinkAtTheCursor_AndAPictureAsABlock()
    {
        var url = await NewPageAsync("Rail insert");

        await Page.Locator("#research-rail-link").FillAsync("https://example.com/burials");
        await Page.Locator("[data-testid=research-rail-add-link]").ClickAsync();
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await Page.Locator("#research-rail-add-picture").SetInputFilesAsync(new FilePayload
        {
            Name = "headstone.png", MimeType = "image/png",
            Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg=="),
        });
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(2, new() { Timeout = 30_000 });
        await Expect(Page.Locator("section[data-block-id]")).ToHaveCountAsync(0);

        await AddTextAsync("See the register: ");
        await Rail.Locator("[data-testid=research-rail-item]").Nth(0).GetByRole(AriaRole.Button, new() { Name = "on the page" }).ClickAsync();
        await Expect(OpenEditor.Locator("a[href='https://example.com/burials']")).ToHaveCountAsync(1, new() { Timeout = 10_000 });

        await Rail.Locator("[data-testid=research-rail-item]").Nth(1).GetByRole(AriaRole.Button, new() { Name = "on the page" }).ClickAsync();
        await Expect(Page.Locator("section[data-kind=image]")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await SaveNowAsync();

        await ReloadPageAsync(url);
        var html = (await SavedTextHtmlAsync()).Single();
        Assert.That(html, Does.Match(@"See the register:\s*<a [^>]*href=""https://example.com/burials""[^>]*>"), html);
        await Expect(Page.Locator("section[data-kind=image]")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Remove_TakesAFileOffTheRail_ButNotOneStillOnThePage()
    {
        var url = await NewPageAsync("Rail remove");
        await PasteAsync($@"() => {{
            const data = new DataTransfer();
            data.items.add({MakeTextFile});
            document.activeElement.dispatchEvent(new ClipboardEvent('paste', {{ clipboardData: data, bubbles: true, cancelable: true }}));
        }}");
        await Expect(Page.Locator("section[data-kind=file]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await SaveNowAsync();

        var row = Rail.Locator("[data-testid=research-rail-item]").First;
        await row.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Expect(Rail.Locator(".alert")).ToContainTextAsync("This file is shown on the page", new() { Timeout = 15_000 });
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(1);

        // Off the page and saved, the same Remove works.
        var block = Page.Locator("section[data-kind=file]").First;
        await block.Locator("[data-testid=block-menu-toggle]").ClickAsync();
        await Page.Locator("[data-testid=block-menu]").GetByRole(AriaRole.Menuitem, new() { Name = "Delete block" }).ClickAsync();
        await SaveNowAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        await ReloadPageAsync(url);
        await Expect(Rail.Locator("[data-testid=research-rail-item]")).ToHaveCountAsync(0);
    }

    // ── The map, beyond taps and routes ────────────────────────────────────────────────────────────────────

    [Test]
    [Category("Maps")]
    public async Task AMapPlace_FoundBySearch_KeepsItsNameAndNote()
    {
        var url = await NewPageAsync("Map search");
        await ClickUntilAsync(Page.Locator("[data-testid=block-add-map]"), Page.Locator("[data-testid=map-block]"));
        var search = Page.Locator("[data-testid=map-block] input[id$='-search']");
        await search.FillAsync("Nashville, TN");
        await Page.Locator("[data-testid=map-block-add-place]").ClickAsync();

        var stops = Page.Locator("[data-testid=map-block] li.ben-map-block__stop");
        var refused = Page.Locator("[data-testid=map-block] .text-danger");
        await Expect(stops.Or(refused).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await stops.CountAsync() == 0)
            Assert.Ignore($"The geocoder is not answering on this host: {await refused.First.InnerTextAsync()}");

        await Page.Locator("[data-testid=map-block] input[id$='-stop-0-label']").FillAsync("Metro Archives");
        await Page.Locator("[data-testid=map-block] input[id$='-stop-0-label']").PressAsync("Tab");
        await Page.Locator("[data-testid=map-block] input[id$='-stop-0-note']").FillAsync("City directories on microfilm");
        await Page.Locator("[data-testid=map-block] input[id$='-stop-0-note']").PressAsync("Tab");
        await SaveNowAsync();

        await ReloadPageAsync(url);
        var saved = Page.Locator("[data-testid=map-block] li.ben-map-block__stop").First;
        await Expect(saved).ToContainTextAsync("Metro Archives");
        await Expect(saved).ToContainTextAsync("City directories on microfilm");
        await Expect(saved.Locator("a[href^='https://maps.apple.com/?ll=']")).ToHaveCountAsync(1);
    }

    [Test]
    [Category("Maps")]
    public async Task AMap_TakesTenPlacesAndNoMore_AndADrivingRouteReadsAsADrive()
    {
        var url = await NewPageAsync("Ten places");
        await ClickUntilAsync(Page.Locator("[data-testid=block-add-map]"), Page.Locator("[data-testid=map-block]"));

        // Ten towns found by search: taps would be quicker, but a tap that lands on a pin already there selects it, and
        // where the pins are depends on how the map framed the last ones.
        var towns = new[] { "Nashville, TN", "Franklin, TN", "Brentwood, TN", "Murfreesboro, TN", "Smyrna, TN",
                            "Lebanon, TN", "Gallatin, TN", "Hendersonville, TN", "Clarksville, TN", "Columbia, TN" };
        var stops = Page.Locator("[data-testid=map-block] li.ben-map-block__stop");
        for (var i = 0; i < towns.Length; i++)
        {
            var search = Page.Locator("[data-testid=map-block] input[id$='-search']");
            await search.FillAsync(towns[i]);
            await Page.Locator("[data-testid=map-block-add-place]").ClickAsync();
            var refused = Page.Locator("[data-testid=map-block] .text-danger");
            await Expect(stops.Nth(i).Or(refused).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
            if (await stops.CountAsync() == i)
                Assert.Ignore($"The geocoder did not find {towns[i]} on this host: {await refused.First.InnerTextAsync()}");
        }

        await Expect(stops).ToHaveCountAsync(10);
        await Expect(Page.Locator("[data-testid=map-block] input[id$='-search']")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=map-block]")).ToContainTextAsync("A map can show up to 10 places.");

        await Page.Locator("[data-testid=map-block] select[id$='-route']").SelectOptionAsync(new SelectOptionValue { Label = "Driving" });
        var legs = Page.Locator("[data-testid=map-block-leg]");
        await Expect(legs).ToHaveCountAsync(9, new() { Timeout = 60_000 });
        foreach (var leg in await legs.AllAsync())
            await Expect(leg).ToContainTextAsync(new Regex(@"\d.*(drive|straight line)"), new() { Timeout = 60_000 });
        await Expect(Page.Locator("[data-testid=map-block-total]")).ToContainTextAsync(new Regex(@"Total \d+(\.\d)? mi"));
        await SaveNowAsync();

        await ReloadPageAsync(url);
        await Expect(Page.Locator("[data-testid=map-block] li.ben-map-block__stop")).ToHaveCountAsync(10);
        await Expect(Page.Locator("[data-testid=map-block-open-in-maps]")).ToHaveAttributeAsync("href", new Regex("dirflg=d$"));
    }
}
