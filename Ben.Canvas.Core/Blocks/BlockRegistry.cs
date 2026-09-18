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
        // Sound is a waveform and a play button — wide enough to scrub, no taller than it needs.
        new(CanvasNodeType.Audio, "Audio", "music", 340, 132, 240, 116, true, false, _ => new AudioData()),
        // Wide by default and resizable both ways: a grid is read across, and how many rows it needs
        // is the one thing the block cannot know. Starts as a header and one row, two columns.
        new(CanvasNodeType.Table, "Table", "grid", 420, 200, 200, 100, true, true,
            _ => new TableData { HasHeaderRow = true, Rows = [["", ""], ["", ""]] }),
        // Square by default, because a circle and a diamond both want equal sides; small, because a
        // shape labels a region rather than holding a document.
        new(CanvasNodeType.Shape, "Shape", "square", 160, 160, 60, 60, true, true, _ => new ShapeData()),
        // A link, not a document: as wide as a file row and no taller than the two lines it shows.
        new(CanvasNodeType.Board, "Board", "book-open", 300, 96, 200, 80, true, false, _ => new BoardData()),
        // A CARD's size, not the film's. Ben, 2026-09-16: "The video should be card sized, not
        // original sized... so it doesn't take up the screen... maybe can resize the card to fit
        // the size the end user wants." A phone films 1080×1920; opened at its own size one clip
        // would bury the board. So: a modest 16:9 box, resizable in both directions from there.
        new(CanvasNodeType.Video, "Video", "video", 320, 200, 200, 130, true, true, _ => new VideoData()),
    ];

    public static BlockDescriptor Get(CanvasNodeType type) =>
        All.FirstOrDefault(d => d.Type == type) ?? throw new ArgumentOutOfRangeException(nameof(type), type, "No block is registered for this type.");

    /// <summary>The blocks a host has switched on, in palette order.</summary>
    public static IReadOnlyList<BlockDescriptor> Enabled(CanvasEditorOptions options) =>
        All.Where(d => options.EnabledBlocks.Contains(d.Type)).ToList();
}
