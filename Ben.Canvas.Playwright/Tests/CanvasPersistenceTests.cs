using System.Text.RegularExpressions;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Playwright.Support;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The board keeps itself on this device: it comes back after a reload, leaving with unsaved work asks, and a
/// board travels out and back in as an .ishcanvas file with its pictures.
/// </summary>
[Category("Persistence")]
[TestFixture(DeviceKind.Desktop)]
public sealed class CanvasPersistenceTests(DeviceKind device) : CanvasTestBase(device)
{
    private ILocator SaveState => Page.Locator(".bc-savestate");

    /// <summary>Counts every write of a board body, to IndexedDB or to localStorage.</summary>
    private const string CountBoardWrites = """
        (() => {
          window.__bcDocWrites = 0
          const put = IDBObjectStore.prototype.put
          IDBObjectStore.prototype.put = function (value, key) {
            if (typeof key === 'string' && key.startsWith('bc-doc-')) window.__bcDocWrites++
            return put.apply(this, arguments)
          }
          const set = Storage.prototype.setItem
          Storage.prototype.setItem = function (key, value) {
            if (key.startsWith('bc-doc-') && key !== 'bc-doc-index' && key !== 'bc-doc-active') window.__bcDocWrites++
            return set.apply(this, arguments)
          }
        })()
        """;

    private async Task<int> BoardWritesAsync() => await Page.EvaluateAsync<int>("() => window.__bcDocWrites || 0");

    private async Task WaitSavedAsync() =>
        await Expect(SaveState).ToContainTextAsync("Saved on this device", new() { Timeout = 6000 });

    [Test]
    public async Task The_board_and_its_view_come_back_after_a_reload()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        var note = await AddNodeAsync("text");
        await DragAsync(note, 160, 90);
        await Page.ClickAsync("[data-bc-action='zoom-in']");
        await EventuallyAsync(ZoomAsync, 1.25, 0.01);
        var before = await WorldRectAsync(note);
        await WaitSavedAsync();
        await Page.WaitForTimeoutAsync(500);

        await Page.ReloadAsync();
        await WaitReadyAsync();
        await Expect(Nodes).ToHaveCountAsync(2, new() { Timeout = 60_000 });

        var id = await note.GetAttributeAsync("data-bc-node");
        var after = await WorldRectAsync(Page.Locator($".bc-node[data-bc-node='{id}']"));
        Assert.That(after.X, Is.EqualTo(before.X).Within(1));
        Assert.That(after.Y, Is.EqualTo(before.Y).Within(1));
        await EventuallyAsync(ZoomAsync, 1.25, 0.01);
        await Expect(SaveState).ToContainTextAsync("Saved on this device");
    }

    [Test]
    public async Task Reopening_a_board_does_not_write_it_again()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        await WaitSavedAsync();

        await Page.AddInitScriptAsync(CountBoardWrites);
        await Page.ReloadAsync();
        await WaitReadyAsync();
        await Expect(Nodes).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        await Page.WaitForTimeoutAsync(3500);

        Assert.That(await BoardWritesAsync(), Is.EqualTo(0), "Restoring a board counted as an edit and rewrote it.");
    }

    [Test]
    public async Task Hiding_the_page_writes_unsaved_work_at_once()
    {
        await StartCleanAsync();
        await Page.EvaluateAsync(CountBoardWrites);
        await AddNodeAsync("card");
        await Page.EvaluateAsync("() => window.dispatchEvent(new Event('pagehide'))");
        await Page.WaitForFunctionAsync("() => window.__bcDocWrites > 0", null, new() { Timeout = 1000 });

        await Page.ReloadAsync();
        await WaitReadyAsync();
        await Expect(Nodes).ToHaveCountAsync(1, new() { Timeout = 60_000 });
    }

    [Test]
    public async Task Leaving_with_unsaved_work_asks_first()
    {
        await StartCleanAsync();
        var asked = new List<string>();
        Page.Dialog += async (_, dialog) =>
        {
            asked.Add(dialog.Type);
            await dialog.DismissAsync();
        };

        await AddNodeAsync("card");
        await Page.CloseAsync(new() { RunBeforeUnload = true });
        await Task.Delay(500);

        Assert.That(asked, Does.Contain("beforeunload"));
    }

    [Test]
    public async Task A_browser_that_refuses_storage_is_told_once()
    {
        await Page.AddInitScriptAsync("""
            IDBObjectStore.prototype.put = function () { throw new DOMException('full', 'QuotaExceededError') }
            const set = Storage.prototype.setItem
            Storage.prototype.setItem = function (key, value) {
              if (key.startsWith('bc-doc-') && key !== 'bc-doc-index' && key !== 'bc-doc-active') throw new DOMException('full', 'QuotaExceededError')
              return set.apply(this, arguments)
            }
            """);
        await StartCleanAsync();
        await AddNodeAsync("card");
        var warning = Page.Locator(".toast", new() { HasTextString = "not letting the board store" });
        await Expect(warning).ToHaveCountAsync(1, new() { Timeout = 6000 });

        await AddNodeAsync("text");
        await Page.WaitForTimeoutAsync(3000);
        await Expect(warning).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Export_then_import_brings_the_board_and_its_picture_back()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        await Page.PasteAsync(files: [new MadeFile("png", "shot.png", "image/png", Width: 64, Height: 48)]);
        var picture = Page.Locator(".bc-node--image img");
        await Expect(picture).ToHaveCountAsync(1);
        await Expect(picture).ToHaveJSPropertyAsync("complete", true);

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.ClickAsync("[data-bc-action='export']"));
        Assert.That(download.SuggestedFilename, Does.EndWith(".ishcanvas"));
        var path = Path.Combine(Path.GetTempPath(), $"canvas-{Guid.NewGuid():N}.ishcanvas");
        await download.SaveAsAsync(path);

        // The reference reader in Core reads what the browser wrote.
        await using (var file = File.OpenRead(path))
        {
            var read = CanvasPackage.Read(file, 10_000_000);
            Assert.That(read.Problem, Is.Null);
            Assert.That(read.Document!.Nodes, Has.Count.EqualTo(2));
            Assert.That(read.Assets, Has.Count.EqualTo(1));
            Assert.That(read.Assets[0].Bytes.Take(4), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        }

        await StartCleanAsync();
        await Page.SetInputFilesAsync("#bc-import", path);
        await Expect(Nodes).ToHaveCountAsync(2, new() { Timeout = 10_000 });
        await Expect(picture).ToHaveCountAsync(1);
        await Page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 64 }");

        File.Delete(path);
    }

    [Test]
    public async Task A_board_written_by_the_reference_format_imports_with_its_picture()
    {
        await StartCleanAsync();
        var pngBytes = (await Page.EvaluateAsync<int[]>(
            "async () => { const c = new OffscreenCanvas(2, 2); c.getContext('2d').fillRect(0, 0, 1, 1); return [...new Uint8Array(await (await c.convertToBlob()).arrayBuffer())] }"))
            .Select(b => (byte)b).ToArray();
        var assetId = Guid.NewGuid();
        var board = new CanvasDocument
        {
            Title = "From another computer",
            Nodes = [new CanvasNode { Type = CanvasNodeType.Image, X = 0, Y = 0, Width = 320, Height = 240, Data = new ImageData { AssetId = assetId, OpfsExt = ".png", Caption = "Porch" } }],
        };
        var path = Path.Combine(Path.GetTempPath(), $"reference-{Guid.NewGuid():N}.ishcanvas");
        await File.WriteAllBytesAsync(path, CanvasPackage.Write(board, [new PackageAsset(assetId, ".png", pngBytes)]));

        await Page.SetInputFilesAsync("#bc-import", path);

        await Expect(Page.Locator(".bc-node--image img[alt='Porch']")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await Page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 2 }");
        await Expect(Page.Locator(".bc-header__title")).ToHaveTextAsync("From another computer");
        File.Delete(path);
    }

    [Test]
    public async Task A_file_that_is_not_a_board_is_refused_and_the_board_stays()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        var path = Path.Combine(Path.GetTempPath(), $"fake-{Guid.NewGuid():N}.ishcanvas");
        await File.WriteAllTextAsync(path, "hello");

        await Page.SetInputFilesAsync("#bc-import", path);

        await Expect(Page.Locator(".toast", new() { HasTextString = "not a board" })).ToHaveCountAsync(1, new() { Timeout = 6000 });
        await Expect(Nodes).ToHaveCountAsync(1);
        File.Delete(path);
    }

    [Test]
    public async Task Pictures_still_keep_when_the_browser_cannot_write_files()
    {
        await Page.AddInitScriptAsync("if (window.FileSystemFileHandle) FileSystemFileHandle.prototype.createWritable = undefined");
        await StartCleanAsync();

        await Page.PasteAsync(files: [new MadeFile("png", "shot.png", "image/png", Width: 50, Height: 50)]);
        await Page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 50 }");
        await WaitSavedAsync();

        await Page.ReloadAsync();
        await WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 50 }", null, new() { Timeout = 60_000 });
        var inIndexedDb = await Page.EvaluateAsync<int>("""
            () => new Promise(done => {
              const open = indexedDB.open('bc-assets', 1)
              open.onsuccess = () => {
                const tx = open.result.transaction('assets', 'readonly')
                const count = tx.objectStore('assets').count()
                count.onsuccess = () => { done(count.result); open.result.close() }
              }
              open.onerror = () => done(-1)
            })
            """);
        Assert.That(inIndexedDb, Is.EqualTo(1), "The picture should have gone to the IndexedDB fallback.");
    }

    [Test]
    public async Task The_header_says_saved_on_this_device_after_an_edit()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        await Expect(SaveState).ToHaveTextAsync(new Regex("Saving|Not saved yet"));
        await WaitSavedAsync();
    }
}
