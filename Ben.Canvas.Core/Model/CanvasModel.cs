using Ben.Canvas.Core.Text;

namespace Ben.Canvas.Core.Model;

// FORMAT: enum member names below are written into board files as strings. Never rename or reorder
// them; add new members at the end. A board saved today must open in every later version.

/// <summary>The kinds of block a board holds.</summary>
public enum CanvasNodeType { Card, Message, Map, Image, Link, Text, File, Audio, Video, Table, Shape }

/// <summary>How a block wears its colour.</summary>
/// <remarks>
/// <para><b>Bar reproduces every board written before this existed</b>, which is why it is first and
/// why the schema does not move: a 3 px accent bar down the left edge, as every block has had.</para>
///
/// <para><b>Solid is the sticky.</b> TextNode already calls itself "a sticky note of plain text"; what
/// it lacked was a background. Turning the colour it already carries into the fill is what the
/// moodboard's panels and the deck's slides are made of — and it works for a card or a picture caption
/// too, which a separate StickyData never would.</para>
///
/// <para>FORMAT: written by name. An unknown value reads as Bar rather than refusing the board — a
/// fill is decoration, and the reasoning is on ForgivingEnumConverter.</para>
/// </remarks>
public enum NodeFill { Bar, Solid }

/// <summary>How a group is drawn.</summary>
/// <remarks>
/// <para><b>Outline is what a group has always looked like</b>: a dashed border and an 8% tint.</para>
///
/// <para><b>Panel is the moodboard's section and the deck's slide</b> — solid tint, solid border, the
/// label as a title bar. It matters beyond looks: SlideOrder's first rule is that groups win, so a deck
/// built from panels presents correctly with nothing else built.</para>
/// </remarks>
public enum GroupFill { Outline, Panel }

/// <summary>A side of a rectangle, where a connector attaches.</summary>
public enum CanvasSide { Top, Right, Bottom, Left }

/// <summary>Where a connector draws an arrowhead.</summary>
public enum EdgeArrow { None, End, Both }

/// <summary>How an image fills its box.</summary>
public enum ImageFit { Contain, Cover }

/// <summary>How much a link card knows. None means it has not been resolved yet.</summary>
public enum LinkPreviewTier { None, HostOnly, OurRecords, Unfurled }

/// <summary>
/// A board: the whole file.
/// </summary>
/// <remarks>
/// <para><see cref="Id"/> is the board's identity inside the file - edges and history refer to it - and is
/// never the key it is stored under on the device. An imported file keeps its Id and is filed under a new
/// local key, or importing the same file twice would silently overwrite the first copy.</para>
///
/// <para>Coordinates are world CSS pixels at zoom 1. Pan and zoom are per-device view state, not content,
/// and are not in this class. All timestamps are UTC.</para>
/// </remarks>
public sealed class CanvasDocument
{
    /// <summary>The format this build writes. Raised only with a migration in CanvasDocumentMigrations.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = CanvasCopy.Titles.DefaultBoardTitle;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CaseId { get; set; }

    /// <summary>The server's revision this copy was loaded at; sent back as If-Match.</summary>
    public int Revision { get; set; }

    public DateTime? PublishedAtUtc { get; set; }
    public List<CanvasNode> Nodes { get; set; } = [];
    public List<CanvasEdge> Edges { get; set; } = [];
    public List<CanvasGroup> Groups { get; set; } = [];

    /// <summary>The next paint order the store hands out. Monotonic, so two blocks never share one.</summary>
    public int NextZ { get; set; }
}

/// <summary>One block on the board.</summary>
public sealed class CanvasNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CanvasNodeType Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Paint order: higher is in front. May be negative after Send to back.</summary>
    public int Z { get; set; }

    /// <summary>A palette token ("1".."6"), never a colour, so both themes work.</summary>
    public string? ColorKey { get; set; }

    /// <summary>Whether the colour is a bar down the edge or the whole background.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Serialization.NodeFillConverter))]
    public NodeFill Fill { get; set; } = NodeFill.Bar;

    public Guid? GroupId { get; set; }
    public bool Locked { get; set; }
    public NodeData Data { get; set; } = new TextData();

    /// <summary>A copy with the same Id and a deep copy of its data.</summary>
    public CanvasNode Clone() => new()
    {
        Id = Id, Type = Type, X = X, Y = Y, Width = Width, Height = Height, Z = Z,
        ColorKey = ColorKey, Fill = Fill, GroupId = GroupId, Locked = Locked, Data = Data.Clone(),
    };
}

/// <summary>A connector between two blocks.</summary>
public sealed class CanvasEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }

    /// <summary>Null means the side is chosen automatically from where the blocks sit.</summary>
    public CanvasSide? FromSide { get; set; }

    public CanvasSide? ToSide { get; set; }
    public string? Label { get; set; }
    public EdgeArrow Arrow { get; set; } = EdgeArrow.End;
    public string? ColorKey { get; set; }

    public CanvasEdge Clone() => (CanvasEdge)MemberwiseClone();
}

/// <summary>
/// A labelled rectangle that blocks belong to. Membership is <see cref="CanvasNode.GroupId"/>; the rect is
/// explicit, so a group keeps its place even when its last member leaves.
/// </summary>
public sealed class CanvasGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Z { get; set; }
    public string? ColorKey { get; set; }

    /// <summary>Whether it is a dashed outline or a solid titled panel.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Serialization.GroupFillConverter))]
    public GroupFill Fill { get; set; } = GroupFill.Outline;

    public CanvasGroup Clone() => (CanvasGroup)MemberwiseClone();
}

/// <summary>The board's colour tokens.</summary>
/// <remarks>A key names a CSS token (--bc-color-1..6) mapped onto the site palette, never a literal colour.</remarks>
public static class CanvasPalette
{
    public static readonly IReadOnlyList<string> Keys = ["1", "2", "3", "4", "5", "6"];

    public static bool IsValid(string? key) => key is not null && Keys.Contains(key, StringComparer.Ordinal);
}
