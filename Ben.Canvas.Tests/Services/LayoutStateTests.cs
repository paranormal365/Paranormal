using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Editor.Components.Chrome;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Components;
using Ben.Canvas.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// The layout service: the media answers it acts on, the panel choices it keeps on the device, and what
/// happens to an open sheet when the screen stops being a phone.
/// </summary>
public sealed class CanvasLayoutStateTests
{
    private static (CanvasLayoutState Layout, FakeStorage Storage, FakeModule Dom) Rig(string? storedLayout = null)
    {
        var js = new FakeModuleJs();
        var storage = new FakeStorage(js.Module("js/storageInterop.js"));
        if (storedLayout is not null) storage.Items[LayoutSnapshot.StorageKey] = storedLayout;
        var dom = js.Module("js/domInterop.js").On("watchMedia", _ => js.Module("watch-handle")).On("unwatchMedia", _ => null);
        js.Module("watch-handle");
        var layout = new CanvasLayoutState(js, Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions()), NullLogger<CanvasLayoutState>.Instance);
        return (layout, storage, dom);
    }

    [Fact]
    public async Task The_queries_match_the_css_breakpoints()
    {
        var (layout, _, dom) = Rig();
        await layout.StartAsync();
        var queries = (string[])dom.Calls.Single(c => c.Name == "watchMedia").Args[0]!;
        Assert.Equal("(max-width: 767.98px)", queries[0]);
        Assert.Equal("(min-width: 768px) and (max-width: 1023.98px)", queries[1]);
    }

    [Fact]
    public async Task A_phone_answer_sets_phone_and_raises_changed()
    {
        var (layout, _, _) = Rig();
        await layout.StartAsync();
        var changed = 0;
        layout.Changed += () => changed++;

        layout.OnMediaChanged([true, false, true, false]);

        Assert.True(layout.IsPhone);
        Assert.True(layout.IsCoarsePointer);
        Assert.False(layout.IsTabletPortrait);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Closing_the_panel_is_kept_on_the_device()
    {
        var (layout, storage, _) = Rig();
        await layout.StartAsync();

        await layout.SetPropsOpenAsync(false);

        Assert.Contains("\"propsOpen\":false", storage.Items[LayoutSnapshot.StorageKey]);
        var (again, _, _) = Rig(storage.Items[LayoutSnapshot.StorageKey]);
        await again.StartAsync();
        Assert.False(again.PropsOpen);
    }

    [Fact]
    public async Task A_damaged_layout_entry_starts_with_the_panel_open_at_half_height()
    {
        var (layout, _, _) = Rig("{not json");
        await layout.StartAsync();
        Assert.True(layout.PropsOpen);
        Assert.Equal("half", layout.SheetSnap);
    }

    [Fact]
    public async Task A_sheet_never_reopens_collapsed()
    {
        var (layout, _, _) = Rig("""{"sheetSnap":"collapsed"}""");
        await layout.StartAsync();
        Assert.Equal("half", layout.SheetSnap);

        await layout.SetSnapAsync("collapsed");
        Assert.Equal("half", layout.SheetSnap);
    }

    [Fact]
    public async Task Leaving_phone_width_turns_the_properties_sheet_into_the_panel()
    {
        var (layout, _, _) = Rig("""{"propsOpen":false}""");
        await layout.StartAsync();
        layout.OnMediaChanged([true, false, true, false]);
        layout.Open(CanvasLayoutState.SheetProperties);

        layout.OnMediaChanged([false, true, true, false]);

        Assert.Null(layout.OpenSheet);
        Assert.True(layout.PropsOpen);
    }

    [Fact]
    public async Task A_grip_drag_down_closes_the_sheet_and_a_drag_up_keeps_it_full()
    {
        var (layout, storage, _) = Rig();
        await layout.StartAsync();
        layout.Open(CanvasLayoutState.SheetAdd);

        await layout.OnSheetSnap("full");
        Assert.Equal("full", layout.SheetSnap);
        Assert.Contains("\"sheetSnap\":\"full\"", storage.Items[LayoutSnapshot.StorageKey]);

        await layout.OnSheetSnap("closed");
        Assert.Null(layout.OpenSheet);
    }

    [Fact]
    public async Task The_grip_click_takes_half_and_full_in_turn()
    {
        var (layout, _, _) = Rig();
        await layout.StartAsync();
        await layout.CycleSnapAsync();
        Assert.Equal("full", layout.SheetSnap);
        await layout.CycleSnapAsync();
        Assert.Equal("half", layout.SheetSnap);
    }
}

/// <summary>The phone sheet: a non-modal dialog with a named grip and close button.</summary>
public sealed class BottomSheetTests
{
    [Fact]
    public async Task A_sheet_is_a_non_modal_dialog_named_by_its_label_with_named_controls()
    {
        var html = await RenderHelper.RenderAsync<BottomSheet>(new Dictionary<string, object?> { ["Label"] = "Properties" });
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"false\"", html);
        Assert.Contains("aria-label=\"Properties\"", html);
        Assert.Contains("aria-label=\"Resize panel\"", html);
        Assert.Contains("data-bc-action=\"sheet-close\"", html);
        Assert.Contains("bc-sheet--half", html);
    }

    /// <summary>The drag script writes --bc-sheet-dy on the sheet; a style attribute rendered by Blazor would wipe it.</summary>
    [Fact]
    public async Task The_sheet_renders_no_style_attribute_of_its_own()
    {
        var html = await RenderHelper.RenderAsync<BottomSheet>(new Dictionary<string, object?> { ["Label"] = "Add" });
        var section = System.Text.RegularExpressions.Regex.Match(html, "<section[^>]*>").Value;
        Assert.DoesNotContain("style=", section);
    }
}
