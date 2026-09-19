using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ben.Canvas.Tests.Services;

/// <summary>Everything the device store needs, wired to in-memory browser modules.</summary>
internal sealed class DeviceRig
{
    public FakeModuleJs Js { get; } = new();
    public FakeStorage Storage { get; }
    public FakeAssets Files { get; }
    public CanvasStore Store { get; } = TestBoards.Store();
    public CanvasViewportState Viewport { get; } = new();
    public CanvasAssetStore Assets { get; }
    public CanvasDocumentStore Documents { get; }

    public DeviceRig(FakeStorage? shareWith = null)
    {
        Storage = new FakeStorage(Js.Module("js/storageInterop.js"));
        if (shareWith is not null)
        {
            foreach (var (k, v) in shareWith.Items) Storage.Items[k] = v;
            foreach (var (k, v) in shareWith.Docs) Storage.Docs[k] = v;
        }

        Files = new FakeAssets(Js.Module("js/opfsInterop.js"));
        Assets = new CanvasAssetStore(Js, NullLogger<CanvasAssetStore>.Instance);
        Documents = new CanvasDocumentStore(Store, Viewport, Assets, Js, NullLogger<CanvasDocumentStore>.Instance)
        {
            AutosaveIdle = TimeSpan.FromMilliseconds(30),
            ViewIdle = TimeSpan.FromMilliseconds(10),
        };
    }

    public async Task<DeviceRig> StartedAsync()
    {
        await Assets.StartAsync();
        await Documents.StartAsync();
        return this;
    }

    public static async Task Eventually(Func<bool> condition, int milliseconds = 2000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > until) Assert.Fail("The condition did not become true in time.");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// The device store: boards come back after a reload, a refused write is said once, and autosave neither
/// fires on a restore nor mid-drag.
/// </summary>
public sealed class CanvasDocumentStoreTests
{
    [Fact]
    public async Task A_saved_board_is_reopened_by_a_fresh_editor()
    {
        var first = await new DeviceRig().StartedAsync();
        first.Documents.New(caseId: null);
        first.Store.AddNode(TestBoards.Node(CanvasNodeType.Text, 40, 60));
        Assert.True(await first.Documents.SaveAsync());

        var second = await new DeviceRig(first.Storage).StartedAsync();
        var (restored, problem) = await second.Documents.RestoreLastActiveAsync();

        Assert.True(restored);
        Assert.Null(problem);
        Assert.Equal(first.Documents.CurrentLocalId, second.Documents.CurrentLocalId);
        Assert.Equal(40, Assert.Single(second.Store.Document.Nodes).X);
        Assert.Equal(SaveStateKind.SavedLocal, second.Documents.State.Kind);
    }

    [Fact]
    public async Task Board_bodies_go_to_the_document_store_not_local_storage()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        rig.Store.AddNode(TestBoards.Node());
        await rig.Documents.SaveAsync();

        var key = DocumentIndex.EntryKey(rig.Documents.CurrentLocalId!.Value);
        Assert.True(rig.Storage.Docs.ContainsKey(key));
        Assert.False(rig.Storage.Items.ContainsKey(key));
        Assert.True(rig.Storage.Items.ContainsKey(DocumentIndex.IndexKey));
    }

    [Fact]
    public async Task A_refused_write_is_not_reported_as_saved_and_is_said_once()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Storage.RefuseDocWrites = true;
        var said = 0;
        rig.Documents.StorageRefused += () => said++;
        rig.Documents.New(null);
        rig.Store.AddNode(TestBoards.Node());

        Assert.False(await rig.Documents.SaveAsync());
        Assert.False(await rig.Documents.SaveAsync());

        Assert.Equal(1, said);
        Assert.True(rig.Documents.IsDirty);
        Assert.NotEqual(SaveStateKind.SavedLocal, rig.Documents.State.Kind);
    }

    [Fact]
    public async Task A_pointer_to_a_board_that_is_gone_restores_nothing_and_is_cleared()
    {
        var rig = new DeviceRig();
        rig.Storage.Items[DocumentIndex.ActiveKey] = Guid.NewGuid().ToString("D");
        await rig.StartedAsync();

        var (restored, problem) = await rig.Documents.RestoreLastActiveAsync();

        Assert.False(restored);
        Assert.Null(problem);
        Assert.False(rig.Storage.Items.ContainsKey(DocumentIndex.ActiveKey));
    }

    [Fact]
    public async Task An_unreadable_board_says_why_and_is_not_overwritten()
    {
        var rig = new DeviceRig();
        var id = Guid.NewGuid();
        rig.Storage.Items[DocumentIndex.ActiveKey] = id.ToString("D");
        rig.Storage.Docs[DocumentIndex.EntryKey(id)] = """{"hello":"world"}""";
        await rig.StartedAsync();

        var (restored, problem) = await rig.Documents.RestoreLastActiveAsync();

        Assert.False(restored);
        Assert.NotNull(problem);
        Assert.Equal("""{"hello":"world"}""", rig.Storage.Docs[DocumentIndex.EntryKey(id)]);
    }

    [Fact]
    public async Task Restoring_a_board_is_not_an_edit_and_schedules_no_autosave()
    {
        var first = await new DeviceRig().StartedAsync();
        first.Documents.New(null);
        first.Store.AddNode(TestBoards.Node());
        await first.Documents.SaveAsync();

        var second = await new DeviceRig(first.Storage).StartedAsync();
        second.Documents.EnableAutosave();
        await second.Documents.RestoreLastActiveAsync();
        var writes = second.Storage.DocWrites;
        await Task.Delay(150);

        Assert.False(second.Documents.IsDirty);
        Assert.False(second.Documents.AutosavePending);
        Assert.Equal(writes, second.Storage.DocWrites);
    }

    [Fact]
    public async Task A_burst_of_edits_settles_into_one_write()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        rig.Documents.EnableAutosave();

        for (var i = 0; i < 5; i++) rig.Store.AddNode(TestBoards.Node(x: i * 10));
        await DeviceRig.Eventually(() => rig.Storage.DocWrites > 0);
        await Task.Delay(120);

        Assert.Equal(1, rig.Storage.DocWrites);
        Assert.Equal(SaveStateKind.SavedLocal, rig.Documents.State.Kind);
    }

    [Fact]
    public async Task Autosave_waits_until_the_drag_is_over()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        rig.Documents.EnableAutosave();
        rig.Viewport.SetGestureActive(true);
        rig.Store.AddNode(TestBoards.Node());

        await Task.Delay(200);
        Assert.Equal(0, rig.Storage.DocWrites);

        rig.Viewport.SetGestureActive(false);
        await DeviceRig.Eventually(() => rig.Storage.DocWrites == 1);
    }

    [Fact]
    public async Task Nothing_is_written_before_autosave_is_switched_on()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        rig.Store.AddNode(TestBoards.Node());
        await Task.Delay(150);
        Assert.Equal(0, rig.Storage.DocWrites);
        Assert.True(rig.Documents.IsDirty);
    }

    [Fact]
    public async Task An_imported_board_gets_a_new_device_key_and_keeps_its_own_id()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        var before = rig.Documents.CurrentLocalId;
        var imported = new CanvasDocument { Title = "From Ruth" };

        Assert.True(await rig.Documents.AdoptImportedAsync(imported));

        Assert.NotEqual(before, rig.Documents.CurrentLocalId);
        Assert.Equal(imported.Id, Assert.Single(rig.Documents.Documents).DocumentId);
    }

    [Fact]
    public async Task The_view_is_kept_per_board()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Documents.New(null);
        rig.Documents.SaveViewSoon(new Core.Geometry.CanvasViewport(120, -40, 1.5));
        await DeviceRig.Eventually(() => rig.Storage.Items.Keys.Any(k => k.StartsWith(Core.Geometry.ViewSnapshot.StorageKeyPrefix)));

        var view = await rig.Documents.LoadViewAsync();
        Assert.Equal(1.5, view!.Value.Zoom);
        Assert.Equal(120, view.Value.PanX);
    }

    [Fact]
    public async Task The_sweep_keeps_pictures_any_board_or_undo_names_and_anything_recent()
    {
        var rig = new DeviceRig();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-2);
        Guid onOtherBoard = Guid.NewGuid(), onThisBoard = Guid.NewGuid(), inUndo = Guid.NewGuid(), orphan = Guid.NewGuid(), recent = Guid.NewGuid();
        foreach (var id in new[] { onOtherBoard, onThisBoard, inUndo, orphan }) rig.Files.Add(id, ".png", old);
        rig.Files.Add(recent, ".png", now.AddMinutes(-5));
        rig.Storage.Items[DocumentIndex.IndexKey] = DocumentIndex.Serialise([new DocumentSummary { LocalId = Guid.NewGuid(), AssetIds = [onOtherBoard] }]);
        await rig.StartedAsync();
        rig.Documents.UtcNow = () => now;

        rig.Documents.New(null);
        rig.Store.AddNode(Image(onThisBoard));
        var deleted = Image(inUndo);
        rig.Store.AddNode(deleted);
        rig.Store.RemoveNodes([deleted.Id]);

        var count = await rig.Documents.SweepUnusedAssetsAsync();

        Assert.Equal(1, count);
        Assert.Equal(orphan.ToString("D") + ".png", Assert.Single(rig.Files.Deleted));
    }

    [Fact]
    public async Task An_unreadable_board_list_sweeps_nothing()
    {
        var rig = new DeviceRig();
        rig.Files.Add(Guid.NewGuid(), ".png", DateTime.UtcNow.AddDays(-3));
        rig.Storage.Items[DocumentIndex.IndexKey] = "{broken";
        await rig.StartedAsync();

        Assert.Equal(0, await rig.Documents.SweepUnusedAssetsAsync());
        Assert.Empty(rig.Files.Deleted);
    }

    [Fact]
    public async Task A_browser_that_will_not_keep_storage_is_mentioned_once_per_device()
    {
        var rig = new DeviceRig();
        rig.Storage.Persisted = false;
        var said = 0;
        rig.Documents.PersistenceNotGranted += () => said++;
        await rig.StartedAsync();

        var again = new DeviceRig(rig.Storage);
        again.Storage.Persisted = false;
        again.Documents.PersistenceNotGranted += () => said++;
        await again.StartedAsync();

        Assert.Equal(1, said);
    }

    private static CanvasNode Image(Guid assetId)
    {
        var node = TestBoards.Node(CanvasNodeType.Image);
        node.Data = new ImageData { AssetId = assetId, OpfsExt = ".png" };
        return node;
    }
}

/// <summary>The leave-page guard follows unsaved work, and hiding the page writes it.</summary>
public sealed class UnloadGuardServiceTests
{
    [Fact]
    public async Task The_guard_goes_on_with_an_edit_and_off_after_the_save_telling_the_browser_only_on_change()
    {
        var rig = await new DeviceRig().StartedAsync();
        var dom = rig.Js.Module("js/domInterop.js").On("flushOnPageHide", _ => null).On("setUnloadGuard", _ => null);
        var guard = new UnloadGuardService(rig.Documents, rig.Js);
        rig.Documents.New(null);
        await guard.StartAsync();

        rig.Store.AddNode(TestBoards.Node());
        rig.Store.AddNode(TestBoards.Node());
        await DeviceRig.Eventually(() => dom.CountOf("setUnloadGuard") == 1);
        Assert.True((bool)dom.Calls.Last(c => c.Name == "setUnloadGuard").Args[0]!);

        await rig.Documents.SaveAsync();
        await DeviceRig.Eventually(() => dom.CountOf("setUnloadGuard") == 2);
        Assert.False((bool)dom.Calls.Last(c => c.Name == "setUnloadGuard").Args[0]!);
    }

    [Fact]
    public async Task Hiding_the_page_writes_unsaved_work_straight_away()
    {
        var rig = await new DeviceRig().StartedAsync();
        rig.Js.Module("js/domInterop.js").On("flushOnPageHide", _ => null).On("setUnloadGuard", _ => null);
        var guard = new UnloadGuardService(rig.Documents, rig.Js);
        rig.Documents.New(null);
        await guard.StartAsync();
        rig.Store.AddNode(TestBoards.Node());

        await guard.OnPageHiding();

        Assert.Equal(1, rig.Storage.DocWrites);
    }
}
