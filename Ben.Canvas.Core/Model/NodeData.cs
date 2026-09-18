using System.Text.Json.Serialization;

namespace Ben.Canvas.Core.Model;

// FORMAT: the "kind" discriminator strings are part of the board file format. Never rename them.

/// <summary>
/// What a block holds, one type per kind of block.
/// </summary>
/// <remarks>
/// <para>A mixed list needs a discriminator, or System.Text.Json cannot tell a card from a map when it
/// reads a file back. An unknown kind fails the read, so a file from a newer editor is refused with a
/// reason rather than opened with blank blocks.</para>
///
/// <para>Every property has a default, so a file written before a property existed still reads.
/// <see cref="Clone"/> is deep: commands keep copies for undo, and a shared dictionary would let a later
/// edit rewrite history.</para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(CardData), "card")]
[JsonDerivedType(typeof(MessageData), "message")]
[JsonDerivedType(typeof(MapData), "map")]
[JsonDerivedType(typeof(ImageData), "image")]
[JsonDerivedType(typeof(LinkData), "link")]
[JsonDerivedType(typeof(TextData), "text")]
[JsonDerivedType(typeof(FileData), "file")]
[JsonDerivedType(typeof(AudioData), "audio")]
[JsonDerivedType(typeof(VideoData), "video")]
[JsonDerivedType(typeof(TableData), "table")]
public abstract class NodeData
{
    /// <summary>A deep copy.</summary>
    public abstract NodeData Clone();
}

/// <summary>A card filled out from a template.</summary>
public sealed class CardData : NodeData
{
    public string TemplateId { get; set; } = "evidence";
    public string Title { get; set; } = "";

    /// <summary>Field key to value. Checkboxes store "true"/"false"; dates store "yyyy-MM-dd".</summary>
    public Dictionary<string, string?> Fields { get; set; } = new(StringComparer.Ordinal);

    public override NodeData Clone() => new CardData
    {
        TemplateId = TemplateId,
        Title = Title,
        Fields = new Dictionary<string, string?>(Fields ?? [], StringComparer.Ordinal),
    };
}

/// <summary>A message box with rich text.</summary>
public sealed class MessageData : NodeData
{
    /// <summary>Allow-listed HTML only, never raw paste; every read of a file re-normalises it.</summary>
    public string Html { get; set; } = "";

    public string Author { get; set; } = "";
    public DateTime TimestampUtc { get; set; }

    public override NodeData Clone() => (MessageData)MemberwiseClone();
}

/// <summary>A map box.</summary>
public sealed class MapData : NodeData
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Zoom { get; set; } = 14;
    public string? Address { get; set; }
    public List<MapPin> Pins { get; set; } = [];

    public override NodeData Clone() => new MapData
    {
        Latitude = Latitude,
        Longitude = Longitude,
        Zoom = Zoom,
        Address = Address,
        Pins = (Pins ?? []).Select(p => p.Clone()).ToList(),
    };
}

/// <summary>A pin on a map box.</summary>
public sealed class MapPin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Title { get; set; }

    public MapPin Clone() => (MapPin)MemberwiseClone();
}

/// <summary>An image box.</summary>
public sealed class ImageData : NodeData
{
    /// <summary>The picture stored on this device (bc-assets/{AssetId}{OpfsExt}).</summary>
    public Guid? AssetId { get; set; }

    public string? OpfsExt { get; set; }

    /// <summary>The picture uploaded to the case, once the board is saved to the server.</summary>
    public Guid? UploadFileId { get; set; }

    public ImageFit Fit { get; set; } = ImageFit.Contain;
    public string? Caption { get; set; }
    public int NaturalWidth { get; set; }
    public int NaturalHeight { get; set; }

    public override NodeData Clone() => (ImageData)MemberwiseClone();
}

/// <summary>A link card.</summary>
public sealed class LinkData : NodeData
{
    public string Url { get; set; } = "";
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SiteName { get; set; }

    /// <summary>
    /// The page's own https picture address. The proxy address is built from the API base when the card
    /// renders and is never stored, so a board moved between hosts does not carry a dead proxy URL.
    /// </summary>
    public string? ImageSourceUrl { get; set; }

    /// <summary>
    /// A picture address anybody's browser can load as it stands — our own copy of the page's picture,
    /// kept by the server when a signed-in person asked for the preview.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-17: "when you paste a link, I want it to build a preview or thumbnail like
    /// when you post a link in a twitter/x post or facebook post."</para>
    /// <para>The difference from <see cref="ImageSourceUrl"/> is who can see it. That one is the other
    /// site's own address and is only ever loaded through the API's proxy, which needs a bearer token —
    /// so a published board's link cards lost their pictures for exactly the readers a board is
    /// published for. This one is served by the API to anybody, and is stable, so it is kept with the
    /// board rather than rebuilt.</para>
    /// </remarks>
    public string? ImageUrl { get; set; }

    public Guid? ImageAssetId { get; set; }
    public DateTime? FetchedAtUtc { get; set; }
    public LinkPreviewTier Tier { get; set; } = LinkPreviewTier.None;

    public override NodeData Clone() => (LinkData)MemberwiseClone();
}

/// <summary>A sticky note of plain text.</summary>
public sealed class TextData : NodeData
{
    public string Text { get; set; } = "";

    public override NodeData Clone() => (TextData)MemberwiseClone();
}

/// <summary>A file that is not a picture.</summary>
public sealed class FileData : NodeData
{
    public Guid? AssetId { get; set; }
    public string? OpfsExt { get; set; }
    public Guid? UploadFileId { get; set; }
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "";

    public override NodeData Clone() => (FileData)MemberwiseClone();
}

/// <summary>
/// A recording that plays where it sits: sound drawn as a waveform, video in its own small screen.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16: "This is a visual board not just a board for notes." A recording shown as a file
/// chip is something you have to take somewhere else to hear — and on a paranormal board the sound
/// IS the evidence, so it has to be playable where the thinking is happening.
/// </para>
/// <para>
/// The same fields as a file, because underneath it is one: held on this device until the board is
/// saved, and a file on the case like any other afterwards. Only how it is drawn differs.
/// </para>
/// </remarks>
public sealed class AudioData : NodeData
{
    /// <summary>The recording stored on this device (bc-assets/{AssetId}{OpfsExt}).</summary>
    public Guid? AssetId { get; set; }

    public string? OpfsExt { get; set; }

    /// <summary>The recording uploaded to the case, once the board is saved to the server.</summary>
    public Guid? UploadFileId { get; set; }

    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "";
    public string? Caption { get; set; }

    public override NodeData Clone() => (AudioData)MemberwiseClone();
}

/// <inheritdoc cref="AudioData" />
/// <remarks>
/// Dropped in at a card's size rather than the film's own, and resizable from there. Ben,
/// 2026-09-16: "The video should be card sized, not original sized... so it doesn't take up the
/// screen... maybe can resize the card to fit the size the end user wants." A phone's video is
/// 1080 by 1920; opening one at its own size would bury the board under a single clip.
/// </remarks>
public sealed class VideoData : NodeData
{
    public Guid? AssetId { get; set; }

    public string? OpfsExt { get; set; }

    public Guid? UploadFileId { get; set; }

    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "";
    public string? Caption { get; set; }

    public override NodeData Clone() => (VideoData)MemberwiseClone();
}

/// <summary>
/// A grid of plain cells: rows of strings, rectangular, with an optional header row.
/// </summary>
/// <remarks>
/// <para><b>Ben asked for this on 2026-09-14</b> ("Remind me later to have you create the table") and
/// again when the four boards arrived, two of which are grids. It is the plainest useful table and
/// deliberately stays that way: no merged cells, no formulas, no sorting.</para>
///
/// <para><b>Merged cells are the line.</b> Merging turns a rectangle of strings into a graph, and every
/// operation here — insert, remove, square up, render, snapshot — would have to learn about it. None of
/// the boards Ben sent needs one, so the shape stays rectangular and <see cref="Square"/> is free to
/// assume it.</para>
///
/// <para><b>Rectangular is an invariant, not a hope.</b> A hand-edited file, a newer editor, or a
/// ragged spreadsheet selection can all produce short rows, so the reader squares the grid up rather
/// than refusing it — the rule <c>CanvasDocumentMigrations</c> states: a value that cannot be right is
/// corrected. Every mutator below preserves it.</para>
/// </remarks>
public sealed class TableData : NodeData
{
    /// <summary>The smallest grid that can be typed into and read.</summary>
    public const int MinRows = 1;

    /// <summary>The same, across.</summary>
    public const int MinColumns = 1;

    /// <summary>Whether the first row is a header, drawn and announced as one.</summary>
    public bool HasHeaderRow { get; set; } = true;

    /// <summary>The cells, row by row. Every row has the same number, which <see cref="Square"/> enforces.</summary>
    public List<List<string>> Rows { get; set; } = [];

    /// <summary>Columns in the grid: the width of the first row, or zero when there are no rows.</summary>
    [JsonIgnore]
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows[0].Count;

    /// <summary>
    /// Makes the grid rectangular and non-empty: short rows are padded to the widest, long ones are
    /// left alone (that width becomes the grid's), and an empty grid becomes one usable cell.
    /// </summary>
    /// <remarks>
    /// Called on read and after any paste. Running it twice gives the same grid as running it once,
    /// which is the same rule the document migrations hold themselves to.
    /// </remarks>
    public void Square()
    {
        Rows ??= [];
        Rows.RemoveAll(r => r is null);

        if (Rows.Count == 0) Rows.Add([]);

        var width = Math.Max(MinColumns, Rows.Max(r => r.Count));

        foreach (var row in Rows)
        {
            for (var i = 0; i < row.Count; i++) row[i] ??= "";
            while (row.Count < width) row.Add("");
        }
    }

    /// <summary>Inserts an empty column at <paramref name="at"/>, in every row.</summary>
    public void InsertColumn(int at)
    {
        Square();
        at = Math.Clamp(at, 0, ColumnCount);
        foreach (var row in Rows) row.Insert(at, "");
    }

    /// <summary>Removes a column from every row. The last column stays: a table with none cannot be typed into.</summary>
    public void RemoveColumn(int at)
    {
        Square();
        if (ColumnCount <= MinColumns || at < 0 || at >= ColumnCount) return;
        foreach (var row in Rows) row.RemoveAt(at);
    }

    /// <summary>Inserts an empty row at <paramref name="at"/>, with a cell per column.</summary>
    public void InsertRow(int at)
    {
        Square();
        at = Math.Clamp(at, 0, Rows.Count);
        Rows.Insert(at, [.. Enumerable.Repeat("", ColumnCount)]);
    }

    /// <summary>Removes a row. The last row stays, for the reason the last column does.</summary>
    public void RemoveRow(int at)
    {
        Square();
        if (Rows.Count <= MinRows || at < 0 || at >= Rows.Count) return;
        Rows.RemoveAt(at);
    }

    public override NodeData Clone() => new TableData
    {
        HasHeaderRow = HasHeaderRow,
        Rows = [.. Rows.Select(r => new List<string>(r))],
    };
}
