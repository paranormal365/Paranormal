using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Persistence;

/// <summary>Leaving asks only when something would be lost.</summary>
public sealed class UnloadGuardPolicyTests
{
    [Fact]
    public void A_clean_board_never_guards() => Assert.False(UnloadGuardPolicy.ShouldGuard(false, false, false));

    [Fact]
    public void A_pending_autosave_guards() => Assert.True(UnloadGuardPolicy.ShouldGuard(false, true, false));

    [Fact]
    public void Unsaved_changes_guard_and_say_so()
    {
        Assert.True(UnloadGuardPolicy.ShouldGuard(true, false, false));
        Assert.Contains("not saved", UnloadGuardPolicy.Reason(true, false));
    }

    [Fact]
    public void A_running_publish_guards_and_says_so()
    {
        Assert.True(UnloadGuardPolicy.ShouldGuard(false, false, true));
        Assert.Contains("publish", UnloadGuardPolicy.Reason(true, true), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Stored pictures are deleted only when nothing at all could still need them.</summary>
public sealed class AssetGarbageCollectorTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    [Fact]
    public void A_file_no_board_mentions_is_an_orphan() => Assert.Equal([B], AssetGarbageCollector.FindOrphans([A, B], [A]));

    [Fact]
    public void A_file_a_board_still_uses_is_left_alone() => Assert.Empty(AssetGarbageCollector.FindOrphans([A], [A, B]));

    [Fact]
    public void A_file_shared_by_two_boards_survives_one_of_them_being_deleted() =>
        Assert.Empty(AssetGarbageCollector.FindOrphans([A], new[] { A }.Concat([A])));

    [Fact]
    public void Storage_with_nothing_in_it_yields_nothing_to_do() => Assert.Empty(AssetGarbageCollector.FindOrphans([], [A]));

    [Fact]
    public void The_same_file_listed_twice_is_reported_once() => Assert.Equal([B], AssetGarbageCollector.FindOrphans([B, B], []));

    [Fact]
    public void An_unreadable_index_refuses_to_sweep() => Assert.False(AssetGarbageCollector.CanSweep(false, 3, 10));

    [Fact]
    public void Nothing_is_swept_when_the_numbers_do_not_add_up() => Assert.False(AssetGarbageCollector.CanSweep(true, 0, 10));

    [Fact]
    public void Sweeping_is_allowed_once_there_is_a_board_to_reconcile_against() => Assert.True(AssetGarbageCollector.CanSweep(true, 1, 10));

    [Fact]
    public void An_empty_editor_is_not_treated_as_suspicious() => Assert.True(AssetGarbageCollector.CanSweep(true, 0, 0));

    [Fact]
    public void Referenced_ids_cover_image_file_and_link_images()
    {
        var document = new CanvasDocument();
        var image = TestBoards.Node(CanvasNodeType.Image);
        var file = TestBoards.Node(CanvasNodeType.File);
        var link = TestBoards.Node(CanvasNodeType.Link);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        ((ImageData)image.Data).AssetId = ids[0];
        ((FileData)file.Data).AssetId = ids[1];
        ((LinkData)link.Data).ImageAssetId = ids[2];
        document.Nodes.AddRange([image, file, link, TestBoards.Node(CanvasNodeType.Text)]);

        Assert.Equal(ids.OrderBy(i => i), document.ReferencedAssetIds().OrderBy(i => i));
    }

    [Fact]
    public void An_asset_referenced_only_by_the_undo_stack_is_kept()
    {
        var node = TestBoards.Node(CanvasNodeType.Image);
        var asset = Guid.NewGuid();
        ((ImageData)node.Data).AssetId = asset;
        var store = TestBoards.StoreWith(node);
        store.RemoveNodes([node.Id]);

        Assert.DoesNotContain(asset, store.Document.ReferencedAssetIds());
        var referenced = store.Document.ReferencedAssetIds().Concat(store.AssetIdsHeldByHistory());
        Assert.Empty(AssetGarbageCollector.FindOrphans([asset], referenced));
    }
}

/// <summary>The board list keeps device and server identities apart.</summary>
public sealed class DocumentSummaryTests
{
    [Fact]
    public void Formatted_size_uses_a_dot_under_fr_FR() => TestBoards.InCulture("fr-FR", () =>
        Assert.Equal("1.5 KB", new DocumentSummary { SizeBytes = 1536 }.FormattedSize));

    [Fact]
    public void Local_and_server_ids_are_separate()
    {
        var names = typeof(DocumentSummary).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("LocalId", names);
        Assert.Contains("ServerId", names);
        Assert.Contains("DocumentId", names);
        var summary = new DocumentSummary { LocalId = Guid.NewGuid(), ServerId = Guid.NewGuid() };
        Assert.NotEqual(summary.LocalId, summary.ServerId);
    }
}

/// <summary>The header's save phrase for every state.</summary>
public sealed class SaveStateTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Saved_to_case_reads_relative_time() =>
        Assert.Equal("Saved to case 2 min ago", new SaveState(SaveStateKind.SavedServer, Now.AddMinutes(-2)).Text(Now));

    [Fact]
    public void Saved_to_case_a_while_ago_reads_the_date() =>
        Assert.Equal("Saved to case Aug 1, 2026", new SaveState(SaveStateKind.SavedServer, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)).Text(Now));

    [Fact]
    public void Every_kind_has_text()
    {
        foreach (var kind in Enum.GetValues<SaveStateKind>())
            Assert.False(string.IsNullOrWhiteSpace(new SaveState(kind, Now).Text(Now)), $"{kind} has no text.");
    }
}

/// <summary>A stored layout that was hand-edited or partly written still opens sensibly.</summary>
public sealed class LayoutSnapshotTests
{
    [Fact]
    public void A_hand_edited_sheet_snap_falls_back_to_half() =>
        Assert.Equal("half", LayoutSnapshot.Deserialise("{\"sheetSnap\":\"sideways\"}")!.Apply(new CanvasEditorOptions()).SheetSnap);

    [Fact]
    public void Missing_fields_keep_defaults()
    {
        var applied = LayoutSnapshot.Deserialise("{}")!.Apply(new CanvasEditorOptions { ShowMinimap = false });
        Assert.True(applied.PropsOpen);
        Assert.False(applied.ShowMinimap);
    }

    [Fact]
    public void A_stored_choice_survives()
    {
        var json = new LayoutSnapshot { PropsOpen = false, SheetSnap = "full" }.Serialise();
        var applied = LayoutSnapshot.Deserialise(json)!.Apply(new CanvasEditorOptions());
        Assert.Equal((false, "full"), (applied.PropsOpen, applied.SheetSnap));
    }

    [Fact]
    public void Unreadable_json_gives_null() => Assert.Null(LayoutSnapshot.Deserialise("{nope"));
}
