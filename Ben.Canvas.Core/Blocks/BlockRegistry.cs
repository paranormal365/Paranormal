using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;

namespace Ben.Canvas.Core.Blocks;

/// <summary>What the editor knows about a kind of block before it draws one.</summary>
/// <param name="CreateDefaultData">Makes the data for a new block; the argument is "now" in UTC.</param>
public sealed record BlockDescriptor(
    CanvasNodeType Type,
    string DisplayName,
    string IconName,
    double DefaultWidth,
    double DefaultHeight,
    double MinWidth,
    double MinHeight,
    bool ResizableWidth,
    bool ResizableHeight,
    Func<DateTime, NodeData> CreateDefaultData);

/// <summary>
/// Every kind of block the canvas can place.
/// </summary>
/// <remarks>
/// Core holds only the facts; which Razor component draws each kind is mapped in the editor library, so
/// this project never references Razor. Icon names must exist in the site's Feather sprite, which a guard
/// test checks.
/// </remarks>
public static class BlockRegistry
{
    public const double GroupMinWidth = 300;
    public const double GroupMinHeight = 200;
    public const double GroupPadding = 24;
    public const string GroupIconName = "layers";

    public static IReadOnlyList<BlockDescriptor> All { get; } =
    [
        new(CanvasNodeType.Card, "Card", "file-text", 280, 200, 220, 140, true, true, _ => new CardData()),
        new(CanvasNodeType.Message, "Message", "message-square", 320, 160, 240, 120, true, true, now => new MessageData { TimestampUtc = now }),
        new(CanvasNodeType.Map, "Map", "map-pin", 360, 260, 240, 180, true, true, _ => new MapData()),
        new(CanvasNodeType.Image, "Image", "image", 320, 240, 120, 90, true, true, _ => new ImageData()),
        new(CanvasNodeType.Link, "Link", "link", 320, 140, 220, 110, true, false, _ => new LinkData()),
        new(CanvasNodeType.Text, "Note", "edit-3", 220, 120, 160, 80, true, true, _ => new TextData()),
        new(CanvasNodeType.File, "File", "file", 260, 72, 200, 72, false, false, _ => new FileData()),
    ];

    public static BlockDescriptor Get(CanvasNodeType type) =>
        All.FirstOrDefault(d => d.Type == type) ?? throw new ArgumentOutOfRangeException(nameof(type), type, "No block is registered for this type.");

    /// <summary>The blocks a host has switched on, in palette order.</summary>
    public static IReadOnlyList<BlockDescriptor> Enabled(CanvasEditorOptions options) =>
        All.Where(d => options.EnabledBlocks.Contains(d.Type)).ToList();
}
