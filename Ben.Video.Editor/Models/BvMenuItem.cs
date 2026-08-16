using Telerik.SvgIcons;

namespace Ben.Video.Editor.Models;

/// <summary>
/// A single item in a Ben.Video editor context menu.
/// Shared across all <c>TelerikContextMenu&lt;BvMenuItem&gt;</c> instances in the editor.
/// </summary>
public sealed class BvMenuItem
{
    /// <summary>Display text shown in the menu.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Telerik SVG icon shown beside the text. Null = no icon.</summary>
    public ISvgIcon? Icon { get; set; }

    /// <summary>When true this item renders as a horizontal separator; Text and Icon are ignored.</summary>
    public bool IsSeparator { get; set; }

    /// <summary>When true the item is rendered greyed-out and cannot be clicked.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>Callback invoked when the user clicks this item.</summary>
    public Func<Task>? Action { get; set; }
}
