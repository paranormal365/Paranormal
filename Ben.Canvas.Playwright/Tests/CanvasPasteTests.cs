using System.Text.RegularExpressions;
using Ben.Canvas.Playwright.Support;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// Paste and drop, the way people do them: a copied address, a screenshot, formatted text from a web page,
/// files dragged in from the desktop, and the board's own Ctrl+C, Ctrl+X and Ctrl+V.
/// </summary>
[Category("Paste")]
[TestFixture(DeviceKind.Desktop)]
public sealed class CanvasPasteTests(DeviceKind device) : CanvasTestBase(device)
{
    private ILocator Toast(string text) => Page.Locator(".toast", new() { HasTextString = text });

    private async Task GrantClipboardAsync() =>
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = CanvasUrl });

    [Test]
    public async Task A_pasted_address_becomes_a_link_card_naming_its_site()
    {
        await StartCleanAsync();
        // Only our own API may be asked about the link (M6 previews); the page itself is never fetched by the browser.
        var foreign = new List<string>();
        Page.Request += (_, r) => { var host = new Uri(r.Url).Host; if (host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase)) foreign.Add(r.Url); };

        await Page.PasteAsync([new Flavour("text/plain", "https://www.youtube.com/watch?v=abc")]);

        var link = Page.Locator(".bc-node--link");
        await Expect(link).ToHaveCountAsync(1);
        await Expect(link).ToContainTextAsync("youtube.com");
        Assert.That(foreign, Is.Empty, "A link card must never make the browser contact the linked site.");
    }

    [Test]
    public async Task A_pasted_screenshot_becomes_a_picture_the_shape_of_the_screenshot()
    {
        await StartCleanAsync();
        await Page.PasteAsync(files: [new MadeFile("png", "image.png", "image/png", Width: 400, Height: 200)]);

        var node = Page.Locator(".bc-node--image");
        await Expect(node).ToHaveCountAsync(1);
        await Page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 400 }");
        var rect = await WorldRectAsync(node);
        Assert.That(rect.W / rect.H, Is.EqualTo(2).Within(0.05));
    }

    [Test]
    public async Task A_screenshot_with_placeholder_html_beside_it_makes_one_picture()
    {
        await StartCleanAsync();
        await Page.PasteAsync(
            [new Flavour("text/html", "<img src=\"https://example.com/a.png\">")],
            [new MadeFile("png", "image.png", "image/png")]);

        await Expect(Page.Locator(".bc-node--image")).ToHaveCountAsync(1);
        await Page.WaitForTimeoutAsync(300);
        await Expect(Nodes).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Formatted_text_becomes_a_message_that_keeps_bold_and_drops_scripts()
    {
        await StartCleanAsync();
        var dialogs = 0;
        Page.Dialog += async (_, d) => { dialogs++; await d.DismissAsync(); };
        var thirdParty = new List<string>();
        Page.Request += (_, r) => { if (r.Url.Contains("example.com", StringComparison.Ordinal)) thirdParty.Add(r.Url); };

        await Page.PasteAsync(
        [
            new Flavour("text/plain", "Hi there, see a note"),
            new Flavour("text/html", "<b>Hi</b> there, <script>alert(1)</script><span onclick=\"alert(2)\">see</span> <a href=\"javascript:alert(3)\">a note</a><img src=\"https://example.com/track.png\" onerror=\"alert(4)\">"),
        ]);

        var body = Page.Locator(".bc-node--message .bc-msg__body");
        await Expect(body).ToHaveCountAsync(1);
        var html = await body.InnerHTMLAsync();
        Assert.That(html, Does.Contain("<b>Hi</b>"));
        Assert.That(html, Does.Not.Contain("script").And.Not.Contain("onclick").And.Not.Contain("javascript").And.Not.Contain("<img"));
        await Page.WaitForTimeoutAsync(500);
        Assert.That(dialogs, Is.EqualTo(0));
        Assert.That(thirdParty, Is.Empty, "No request may reach a third-party host from pasted HTML.");
    }

    [Test]
    public async Task Two_paragraphs_become_a_note()
    {
        await StartCleanAsync();
        await Page.PasteAsync([new Flavour("text/plain", "Footsteps upstairs at 3am.\n\nRecorder on the landing.")]);
        await Expect(Page.Locator(".bc-node--text")).ToContainTextAsync("Recorder on the landing");
    }

    [Test]
    public async Task Coordinates_become_a_map_box()
    {
        await StartCleanAsync();
        await Page.PasteAsync([new Flavour("text/plain", "36.1627, -86.7816")]);
        await Expect(Page.Locator(".bc-node--map")).ToContainTextAsync("36.16270");
    }

    [Test]
    public async Task A_heic_photo_is_refused_with_advice_and_nothing_is_stored()
    {
        await StartCleanAsync();
        await Page.PasteAsync(files: [new MadeFile("heic", "IMG_0042.HEIC", "image/heic")]);

        await Expect(Toast("HEIC")).ToContainTextAsync("Share");
        await Expect(Nodes).ToHaveCountAsync(0);
        var stored = await Page.EvaluateAsync<int>("""
            async () => {
              try {
                const dir = await (await navigator.storage.getDirectory()).getDirectoryHandle('bc-assets')
                let n = 0
                for await (const name of dir.keys()) if (name !== 'probe.txt') n++
                return n
              } catch { return 0 }
            }
            """);
        Assert.That(stored, Is.EqualTo(0));
    }

    [Test]
    public async Task A_phone_photo_loses_its_location_before_it_is_kept()
    {
        await StartCleanAsync();
        await Page.PasteAsync(files: [new MadeFile("jpeg-with-exif", "IMG_0001.jpg", "image/jpeg", Width: 60, Height: 40)]);

        var image = Page.Locator(".bc-node--image img");
        await Expect(image).ToHaveCountAsync(1);
        var bytes = await image.PictureBytesAsync();
        var text = new string(bytes.Select(b => (char)b).ToArray());
        Assert.That(bytes.Take(3), Is.EqualTo(new[] { 0xFF, 0xD8, 0xFF }), "Still a JPEG.");
        Assert.That(text, Does.Not.Contain("Exif"), "The EXIF block (where a phone writes GPS) must not be kept.");
    }

    [Test]
    public async Task A_dropped_file_shows_the_drop_ring_and_lands_where_it_was_dropped()
    {
        await StartCleanAsync();
        var box = (await Board.BoundingBoxAsync())!;
        var (x, y) = (box.X + 300, box.Y + 250);

        await Page.DropAsync(x, y, files: [new MadeFile("bytes", "notes.pdf", "application/pdf", "%PDF-1.4\n%%EOF")], stopBeforeDrop: true);
        await Expect(Board).ToHaveClassAsync(new Regex("bc-board--dropping"));

        await Page.DropAsync(x, y, files: [new MadeFile("bytes", "notes.pdf", "application/pdf", "%PDF-1.4\n%%EOF")]);
        await Expect(Board).Not.ToHaveClassAsync(new Regex("bc-board--dropping"));
        var file = Page.Locator(".bc-node--file");
        await Expect(file).ToContainTextAsync("notes.pdf");

        var nodeBox = (await file.BoundingBoxAsync())!;
        Assert.That(nodeBox.X + nodeBox.Width / 2, Is.EqualTo(x).Within(4));
        Assert.That(nodeBox.Y + nodeBox.Height / 2, Is.EqualTo(y).Within(4));
        await Expect(file.Locator("a.bc-file__download")).ToHaveAttributeAsync("href", new Regex("^blob:"));
    }

    [Test]
    public async Task Two_dropped_files_both_arrive()
    {
        await StartCleanAsync();
        var box = (await Board.BoundingBoxAsync())!;
        await Page.DropAsync(box.X + 400, box.Y + 300,
            [new Flavour("text/uri-list", "https://example.com/case.pdf")],
            [new MadeFile("bytes", "one.pdf", "application/pdf", "%PDF-1.4 one"), new MadeFile("bytes", "two.pdf", "application/pdf", "%PDF-1.4 two")]);

        await Expect(Page.Locator(".bc-node--file")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task Pasting_while_writing_a_note_types_into_the_note_instead()
    {
        await StartCleanAsync();
        var note = await AddNodeAsync("text");
        var box = (await note.BoundingBoxAsync())!;
        await Page.Mouse.DblClickAsync(box.X + box.Width / 2, box.Y + 14);
        await Expect(note.Locator("textarea")).ToBeFocusedAsync();

        await Page.EvaluateAsync("""
            () => {
              const dt = new DataTransfer()
              dt.setData('text/plain', 'https://example.com/')
              document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }))
            }
            """);
        await Page.WaitForTimeoutAsync(400);
        await Expect(Nodes).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Copy_and_paste_on_the_board_makes_a_copy_24px_down_and_right()
    {
        await GrantClipboardAsync();
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        var before = await WorldRectAsync(card);
        await Board.FocusAsync();

        await Page.Keyboard.PressAsync("Control+c");
        var clip = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.That(clip, Does.StartWith("{\"$ishcanvas\""));
        await Expect(Board).ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("Control+v");
        await Expect(Nodes).ToHaveCountAsync(2);
        var copy = Page.Locator(".bc-node[data-bc-selected]");
        var after = await WorldRectAsync(copy);
        Assert.That(after.X - before.X, Is.EqualTo(24).Within(1));
        Assert.That(after.Y - before.Y, Is.EqualTo(24).Within(1));

        await Page.Keyboard.PressAsync("Control+z");
        await Expect(Nodes).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Cut_removes_the_block_and_paste_brings_it_back()
    {
        await GrantClipboardAsync();
        await StartCleanAsync();
        await AddNodeAsync("text");
        await Board.FocusAsync();

        await Page.Keyboard.PressAsync("Control+x");
        await Expect(Nodes).ToHaveCountAsync(0);

        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+v");
        await Expect(Page.Locator(".bc-node--text")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task The_paste_button_reads_the_clipboard()
    {
        await GrantClipboardAsync();
        await StartCleanAsync();
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('https://ishaunted.com/cases')");

        await Page.ClickAsync(".bc-rail [data-bc-action='paste']");

        await Expect(Page.Locator(".bc-node--link")).ToContainTextAsync("ishaunted.com");
    }

    [Test]
    public async Task An_empty_clipboard_says_there_is_nothing_to_paste()
    {
        await GrantClipboardAsync();
        await StartCleanAsync();
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('')");

        await Page.ClickAsync(".bc-rail [data-bc-action='paste']");

        await Expect(Toast("nothing on the clipboard")).ToHaveCountAsync(1);
        await Expect(Nodes).ToHaveCountAsync(0);
    }
}
