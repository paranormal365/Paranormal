using Ben.Canvas.Editor.Components;
using Ben.Canvas.Editor.Components.Chrome;
using Ben.Canvas.Editor.Components.Kit;
using Microsoft.AspNetCore.Components;

namespace Ben.Canvas.Tests.Components;

/// <summary>
/// The copied confirm dialog keeps the site's defaults.
/// </summary>
/// <remarks>
/// People who use the site have learned what a red Delete button means. A copy that drifts to a blue OK
/// teaches the opposite habit in the one place a mistake deletes work.
/// </remarks>
public sealed class BcConfirmDialogTests
{
    [Fact]
    public async Task A_destructive_confirm_uses_the_danger_button_and_says_delete()
    {
        var html = await RenderHelper.RenderAsync<BcConfirmDialog>(new Dictionary<string, object?>
        {
            [nameof(BcConfirmDialog.Visible)] = true,
            [nameof(BcConfirmDialog.OnConfirm)] = EventCallback.Empty,
        });

        Assert.Contains("btn-danger", html);
        Assert.Contains(">Delete<", html.Replace("\n", "").Replace("  ", ""));
    }

    [Fact]
    public async Task A_hidden_dialog_renders_nothing()
    {
        var html = await RenderHelper.RenderAsync<BcConfirmDialog>(new Dictionary<string, object?>
        {
            [nameof(BcConfirmDialog.Visible)] = false,
            [nameof(BcConfirmDialog.OnConfirm)] = EventCallback.Empty,
        });

        Assert.DoesNotContain("modal", html);
    }
}

/// <summary>What the header bar draws, and where the host's pieces go.</summary>
public sealed class CanvasHeaderBarTests
{
    [Fact]
    public async Task An_untitled_board_says_untitled_board()
    {
        var html = await RenderHelper.RenderAsync<CanvasHeaderBar>();

        Assert.Contains("Untitled board", html);
        Assert.Contains("class=\"bc-header", html);
    }

    [Fact]
    public async Task The_back_link_and_host_status_render_in_their_slots()
    {
        RenderFragment back = b => b.AddMarkupContent(0, "<a id=\"back\">Back</a>");
        RenderFragment status = b => b.AddMarkupContent(0, "<span id=\"chip\">Sign in</span>");

        var html = await RenderHelper.RenderAsync<CanvasHeaderBar>(new Dictionary<string, object?>
        {
            [nameof(CanvasHeaderBar.BackContent)] = back,
            [nameof(CanvasHeaderBar.HostStatusContent)] = status,
        });

        var header = html.IndexOf("bc-header", StringComparison.Ordinal);
        var backAt = html.IndexOf("id=\"back\"", StringComparison.Ordinal);
        var titleAt = html.IndexOf("Untitled board", StringComparison.Ordinal);
        var chipAt = html.IndexOf("id=\"chip\"", StringComparison.Ordinal);

        Assert.True(header < backAt && backAt < titleAt && titleAt < chipAt, html);
    }

    [Fact]
    public async Task No_divider_without_a_back_link()
    {
        var html = await RenderHelper.RenderAsync<CanvasHeaderBar>();

        Assert.DoesNotContain("class=\"vr\"", html);
    }
}

/// <summary>The editor root's shape.</summary>
public sealed class CanvasEditorTests
{
    [Fact]
    public async Task The_root_is_bc_editor_with_a_header_and_body()
    {
        var html = await RenderHelper.RenderAsync<CanvasEditor>();

        Assert.Contains("class=\"bc-editor", html);
        Assert.Contains("bc-header", html);
        Assert.Contains("bc-editor__body", html);
        Assert.True(html.IndexOf("bc-header", StringComparison.Ordinal) < html.IndexOf("bc-editor__body", StringComparison.Ordinal),
            "The header must come before the board body.");
    }

    [Fact]
    public async Task The_desktop_grid_has_header_rail_board_and_props()
    {
        var html = await RenderHelper.RenderAsync<CanvasEditor>();

        Assert.Contains("class=\"bc-header", html);
        Assert.Contains("class=\"bc-rail", html);
        Assert.Contains("class=\"bc-board", html);
        Assert.Contains("id=\"bc-props\"", html);
        Assert.Contains("id=\"bc-live\"", html);
    }

    [Fact]
    public async Task An_empty_board_says_so_instead_of_placeholder_copy()
    {
        var html = await RenderHelper.RenderAsync<CanvasEditor>();

        Assert.Contains("This board is empty", html);
        Assert.DoesNotContain("Lorem", html);
        Assert.DoesNotContain("placeholder", html, StringComparison.OrdinalIgnoreCase);
    }
}
