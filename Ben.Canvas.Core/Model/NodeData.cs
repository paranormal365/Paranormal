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
