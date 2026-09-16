using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Paste;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Where pasted things land, what they become, what is refused, and the board's own copy and cut.
/// </summary>
public sealed class PasteServiceTests
{
    private sealed class Rig
    {
        public FakeModuleJs Js { get; } = new();
        public FakeAssets Files { get; }
        public CanvasStore Store { get; } = TestBoards.Store();
        public SelectionState Selection { get; } = new();
        public CanvasViewportState Viewport { get; } = new();
        public AnnouncerService Announcer { get; } = new();
        public CanvasEditorOptions Options { get; } = new();
        public PasteService Paste { get; }
        public List<string> Problems { get; } = [];

        public Rig()
        {
            Files = new FakeAssets(Js.Module("js/opfsInterop.js"));
            var assets = new CanvasAssetStore(Js, NullLogger<CanvasAssetStore>.Instance);
            assets.StartAsync().GetAwaiter().GetResult();
            Viewport.SetBoardSize(1000, 800);
            Paste = new PasteService(Store, Selection, Viewport, assets, Announcer,
                Microsoft.Extensions.Options.Options.Create(Options), Js, NullLogger<PasteService>.Instance);
            Paste.Problems += p => Problems.AddRange(p);
        }

        public Task<int> PasteAsync(params PasteItem[] items) => Paste.PlaceAsync(new PasteInbound([.. items], "paste", null, null));
    }

    private static PasteItem Text(string text, string mime = "text/plain") => new("string", mime, Text: text);

    [Fact]
    public async Task A_photo_becomes_a_picture_block_the_shape_of_the_photo()
    {
        var rig = new Rig();
        var id = Guid.NewGuid();
        rig.Files.Add(id, ".png", DateTime.UtcNow);

        await rig.PasteAsync(new PasteItem("file", "image/png", FileName: "shot.png", Size: 2000, AssetId: id, Ext: ".png", Head: TestBoards.PngHead, Width: 1600, Height: 1200));

        var node = Assert.Single(rig.Store.Document.Nodes);
        Assert.Equal(CanvasNodeType.Image, node.Type);
        Assert.Equal(1600d / 1200d, node.Width / node.Height, 3);
        Assert.Equal(id, ((ImageData)node.Data).AssetId);
        Assert.True(rig.Selection.IsSelected(node.Id));
    }

    [Fact]
    public async Task A_heic_photo_is_refused_with_the_iphone_advice_and_nothing_is_kept()
    {
        var rig = new Rig();
        var id = Guid.NewGuid();
        rig.Files.Add(id, ".bin", DateTime.UtcNow);

        await rig.PasteAsync(new PasteItem("file", "image/heic", FileName: "IMG_0001.HEIC", Size: 3000, AssetId: id, Ext: ".bin", Head: TestBoards.HeicHead));

        Assert.Empty(rig.Store.Document.Nodes);
        Assert.Contains(rig.Problems, p => p.Contains("HEIC") && p.Contains("Share"));
        Assert.Contains(id.ToString("D") + ".bin", rig.Files.Deleted);
    }

    [Fact]
    public async Task A_lone_address_becomes_a_link_card_waiting_for_its_preview()
    {
        var rig = new Rig();
        await rig.PasteAsync(Text("https://www.youtube.com/watch?v=abc"));
        var link = (LinkData)Assert.Single(rig.Store.Document.Nodes).Data;
        Assert.Equal(LinkPreviewTier.None, link.Tier);
        Assert.Contains("youtube.com", link.Url);
    }

    [Fact]
    public async Task Formatted_text_keeps_bold_and_loses_scripts()
    {
        var rig = new Rig();
        await rig.PasteAsync(
            Text("Hi a x", "text/plain"),
            Text("<b>Hi</b><script>x()</script><span onclick=\"1\">a</span><a href=\"javascript:1\">x</a><img src=\"https://example.com/p.png\" onerror=\"alert(1)\">", "text/html"));

        var html = ((MessageData)Assert.Single(rig.Store.Document.Nodes).Data).Html;
        Assert.Contains("<b>Hi</b>", html);
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("javascript", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public async Task A_paste_is_centred_in_view_and_a_second_one_steps_away()
    {
        var rig = new Rig();
        await rig.PasteAsync(Text("one"));
        await rig.PasteAsync(Text("two"));
        var (first, second) = (rig.Store.Document.Nodes[0], rig.Store.Document.Nodes[1]);
        var centre = rig.Viewport.WorldCentre();

        Assert.Equal(centre.X, first.X + first.Width / 2, 1);
        Assert.Equal(centre.Y, first.Y + first.Height / 2, 1);
        Assert.Equal(CanvasStore.CascadeOffset, second.X - first.X, 1);
    }

    [Fact]
    public async Task A_drop_lands_where_it_was_dropped()
    {
        var rig = new Rig();
        rig.Viewport.Set(new CanvasViewport(100, 50, 2));

        await rig.Paste.PlaceAsync(new PasteInbound([Text("dropped note")], "drop", 300, 250));

        var node = Assert.Single(rig.Store.Document.Nodes);
        var world = ViewportMath.ClientToWorld(rig.Viewport.Current, 300, 250);
        Assert.Equal(world.X, node.X + node.Width / 2, 1);
        Assert.Equal(world.Y, node.Y + node.Height / 2, 1);
    }

    [Fact]
    public async Task Copy_then_paste_makes_one_undo_step_24px_from_the_original()
    {
        var rig = new Rig();
        var a = TestBoards.Node(CanvasNodeType.Card, 100, 100);
        var b = TestBoards.Node(CanvasNodeType.Text, 500, 100);
        rig.Store.AddNode(a);
        rig.Store.AddNode(b);
        rig.Store.Connect(a.Id, b.Id);
        rig.Viewport.Set(new CanvasViewport(0, 0, 1));
        rig.Selection.SelectMany([a.Id, b.Id]);

        var copied = rig.Paste.GetClipboardPayload()!;
        Assert.StartsWith("{\"$ishcanvas\"", copied.Json);
        Assert.StartsWith("<div data-ishcanvas>", copied.Html);

        var undoBefore = rig.Store.UndoDescription;
        await rig.PasteAsync(Text(copied.Json));

        Assert.Equal(4, rig.Store.Document.Nodes.Count);
        Assert.Equal(2, rig.Store.Document.Edges.Count);
        var pastedCard = rig.Store.Document.Nodes.Single(n => n.Type == CanvasNodeType.Card && n.Id != a.Id);
        Assert.Equal(a.X + CanvasStore.CascadeOffset, pastedCard.X, 1);
        Assert.Equal(a.Y + CanvasStore.CascadeOffset, pastedCard.Y, 1);

        rig.Store.Undo();
        Assert.Equal(2, rig.Store.Document.Nodes.Count);
        Assert.Equal(undoBefore, rig.Store.UndoDescription);
    }

    [Fact]
    public async Task Nothing_selected_means_nothing_to_copy()
    {
        var rig = new Rig();
        rig.Store.AddNode(TestBoards.Node());
        Assert.Null(rig.Paste.GetClipboardPayload());
        await Task.CompletedTask;
    }

    [Fact]
    public void A_cut_removes_the_selection_only_once_the_copy_has_happened()
    {
        var rig = new Rig();
        var node = TestBoards.Node();
        rig.Store.AddNode(node);
        rig.Selection.Select(node.Id);

        Assert.NotNull(rig.Paste.GetClipboardPayload());
        Assert.Single(rig.Store.Document.Nodes);

        rig.Paste.OnCutCopied();
        Assert.Empty(rig.Store.Document.Nodes);
    }

    [Fact]
    public async Task A_place_pasted_with_maps_switched_off_becomes_a_note_and_says_why()
    {
        var rig = new Rig();
        rig.Options.EnabledBlocks.Remove(CanvasNodeType.Map);

        await rig.PasteAsync(Text("36.1627, -86.7816"));

        Assert.Equal(CanvasNodeType.Text, Assert.Single(rig.Store.Document.Nodes).Type);
        Assert.Contains(rig.Problems, p => p.Contains("Maps are switched off"));
    }

    [Fact]
    public async Task A_file_the_device_could_not_keep_is_named()
    {
        var rig = new Rig();
        await rig.PasteAsync(new PasteItem("file", "application/pdf", FileName: "report.pdf", Size: 500, Head: TestBoards.PdfHead));
        Assert.Empty(rig.Store.Document.Nodes);
        Assert.Contains(rig.Problems, p => p.Contains("report.pdf"));
    }

    [Fact]
    public async Task A_full_board_pastes_nothing_and_deletes_the_stored_picture()
    {
        var rig = new Rig();
        var store = new CanvasStore { MaxNodes = 1 };
        var assets = new CanvasAssetStore(rig.Js, NullLogger<CanvasAssetStore>.Instance);
        await assets.StartAsync();
        var problems = new List<string>();
        var paste = new PasteService(store, rig.Selection, rig.Viewport, assets, rig.Announcer,
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions { MaxNodes = 1 }), rig.Js, NullLogger<PasteService>.Instance);
        paste.Problems += problems.AddRange;
        store.AddNode(TestBoards.Node());
        var id = Guid.NewGuid();
        rig.Files.Add(id, ".png", DateTime.UtcNow);

        await paste.PlaceAsync(new PasteInbound([new PasteItem("file", "image/png", Size: 10, AssetId: id, Ext: ".png", Head: TestBoards.PngHead, Width: 10, Height: 10)], "paste", null, null));

        Assert.Single(store.Document.Nodes);
        Assert.Contains(problems, p => p.Contains("at most 1 blocks"));
        Assert.Contains(id.ToString("D") + ".png", rig.Files.Deleted);
    }

    [Fact]
    public async Task A_paste_button_after_a_long_press_lands_at_the_finger_but_a_keyboard_paste_does_not()
    {
        var rig = new Rig();
        rig.Paste.SetMenuPoint(new CanvasPoint(2000, 1500));
        await rig.Paste.PlaceAsync(new PasteInbound([Text("keyboard")], "paste", null, null));
        var keyboard = rig.Store.Document.Nodes.Single();
        Assert.True(Math.Abs(keyboard.X + keyboard.Width / 2 - 2000) > 500, "A keyboard paste lands in view, not at an old menu point.");

        rig.Paste.SetMenuPoint(new CanvasPoint(2000, 1500));
        await rig.Paste.PlaceAsync(new PasteInbound([Text("button")], "button", null, null));
        var button = rig.Store.Document.Nodes.Single(n => n.Id != keyboard.Id);
        Assert.Equal(2000, button.X + button.Width / 2, 1);
        Assert.Equal(1500, button.Y + button.Height / 2, 1);
    }

    [Fact]
    public async Task A_refused_clipboard_goes_to_the_paste_box_when_the_editor_listens()
    {
        var rig = new Rig();
        var denied = 0;
        rig.Paste.PermissionDenied += () => denied++;
        await rig.Paste.OnPasteRefused("permission");
        Assert.Equal(1, denied);
        Assert.Empty(rig.Problems);
    }
    [Fact]
    public async Task A_denied_clipboard_read_is_explained_not_thrown()
    {
        var rig = new Rig();
        await rig.Paste.OnPasteRefused("permission");
        Assert.Contains(rig.Problems, p => p.Contains("Allow"));
    }

    [Fact]
    public async Task A_successful_paste_is_announced_not_complained_about()
    {
        var rig = new Rig();
        await rig.PasteAsync(Text("a note"));
        Assert.Empty(rig.Problems);
        Assert.Contains("Added 1 item", rig.Announcer.Text);
    }
}

/// <summary>What an export asks the browser to pack.</summary>
public sealed class CanvasPackageServiceTests
{
    [Fact]
    public void Pictures_and_files_are_packed_with_their_stored_extension_and_a_readable_name()
    {
        Guid picture = Guid.NewGuid(), file = Guid.NewGuid();
        var board = new CanvasDocument
        {
            Nodes =
            [
                new CanvasNode { Type = CanvasNodeType.Image, Data = new ImageData { AssetId = picture, OpfsExt = ".jpg", Caption = "Porch" } },
                new CanvasNode { Type = CanvasNodeType.File, Data = new FileData { AssetId = file, OpfsExt = ".pdf", FileName = "report.pdf" } },
                new CanvasNode { Type = CanvasNodeType.Image, Data = new ImageData { AssetId = picture, OpfsExt = ".jpg" } },
                new CanvasNode { Type = CanvasNodeType.Text, Data = new TextData { Text = "not stored" } },
            ],
        };

        var assets = CanvasPackageService.AssetsOf(board);

        Assert.Equal(2, assets.Count);
        Assert.Contains(assets, a => a.AssetId == picture.ToString("D") && a.Ext == ".jpg" && a.Name == "Porch");
        Assert.Contains(assets, a => a.AssetId == file.ToString("D") && a.Ext == ".pdf" && a.Name == "report.pdf");
    }

    [Fact]
    public async Task An_import_that_is_not_a_board_says_so_and_leaves_the_open_board()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        var open = rig.Store.Document;
        rig.Js.Module("js/packageInterop.js").On("importPackage", _ => new { problem = "not-a-board" });
        var service = new CanvasPackageService(rig.Store, rig.Documents,
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions()), rig.Js, NullLogger<CanvasPackageService>.Instance);

        var outcome = await service.ImportAsync(default);

        Assert.False(outcome.Imported);
        Assert.Contains("not a board", Assert.Single(outcome.Problems));
        Assert.Same(open, rig.Store.Document);
    }

    [Fact]
    public async Task An_imported_board_opens_cleaned_under_a_new_device_key()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        var board = new CanvasDocument
        {
            Title = "Imported",
            Nodes = [new CanvasNode { Type = CanvasNodeType.Message, Width = 320, Height = 160, Data = new MessageData { Html = "<p onmouseover=\"x\">Hello</p>" } }],
        };
        rig.Js.Module("js/packageInterop.js").On("importPackage", _ => new { problem = (string?)null, documentJson = CanvasSerializer.Serialize(board), stored = 0, tooLarge = new[] { "big.png" }, notStored = Array.Empty<string>() });
        var service = new CanvasPackageService(rig.Store, rig.Documents,
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions()), rig.Js, NullLogger<CanvasPackageService>.Instance);

        var outcome = await service.ImportAsync(default);

        Assert.True(outcome.Imported);
        Assert.Equal("Imported", rig.Store.Document.Title);
        Assert.DoesNotContain("onmouseover", ((MessageData)rig.Store.Document.Nodes[0].Data).Html);
        Assert.Contains(outcome.Problems, p => p.Contains("big.png"));
        Assert.Equal(1, rig.Storage.DocWrites);
    }
}
