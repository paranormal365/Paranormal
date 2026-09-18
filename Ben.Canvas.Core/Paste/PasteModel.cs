using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Paste;

/// <summary>Everything one paste or drop delivered, in the shape the browser module posts it.</summary>
/// <param name="Source">"paste", "drop", "button" or "photo".</param>
public sealed record PasteEnvelope(List<PasteItem> Items, double WorldX, double WorldY, string Source);

/// <summary>
/// One clipboard or drop item.
/// </summary>
/// <remarks>
/// Files never cross into .NET: the browser has already stored each one on the device as
/// bc-assets/{AssetId}{Ext} and sends only its first twelve bytes, so the classifier can tell a real
/// picture from a renamed HEIC without a large copy passing through interop.
/// </remarks>
/// <param name="Kind">"file" or "string".</param>
public sealed record PasteItem(
    string Kind,
    string MimeType,
    string? Text = null,
    string? FileName = null,
    long Size = 0,
    Guid? AssetId = null,
    string? Ext = null,
    byte[]? Head = null,
    int? Width = null,
    int? Height = null);

/// <summary>
/// The board's own clipboard format.
/// </summary>
/// <remarks>
/// Chrome and Safari refuse custom MIME types in a ClipboardItem, so the board writes plain text JSON and
/// recognises its own by the marker property, written first.
/// </remarks>
public sealed class CanvasClipboardPayload
{
    public const string MarkerName = "$ishcanvas";

    [JsonPropertyName(MarkerName)]
    [JsonPropertyOrder(-2)]
    public int Marker { get; set; } = 1;

    [JsonPropertyOrder(-1)]
    public int Version { get; set; } = 1;

    public List<CanvasNode> Nodes { get; set; } = [];
    public List<CanvasEdge> Edges { get; set; } = [];

    /// <summary>Reads our own copy back; anything else, including JSON without the marker, is not ours.</summary>
    public static bool TryReadPayload(string? text, out CanvasClipboardPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{') || !trimmed.Contains(MarkerName, StringComparison.Ordinal)) return false;

        try
        {
            var read = Serialization.CanvasSerializer.Deserialize<CanvasClipboardPayload>(trimmed);
            if (read is null || read.Marker != 1) return false;
            read.Nodes ??= [];
            read.Edges ??= [];
            read.Nodes.RemoveAll(n => n is null);
            read.Edges.RemoveAll(e => e is null);
            payload = read;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}

/// <summary>Why part of a paste was not placed.</summary>
public enum PasteRefusalKind { Heic, TooLargeImage, TooLargeFile, TooManyItems, TextTruncated, NothingToPaste, Permission, NotStored }

/// <summary>A refusal and the sentence that explains it.</summary>
public sealed record PasteRefusal(PasteRefusalKind Kind, string Message);

/// <summary>What one pasted item turns into.</summary>
public abstract record PasteIntent
{
    public sealed record Internal(CanvasClipboardPayload Payload) : PasteIntent;
    public sealed record Image(Guid AssetId, string Ext, int? Width, int? Height) : PasteIntent;
    public sealed record File(Guid AssetId, string? Ext, string FileName, long Size, string ContentType) : PasteIntent;

    /// <summary>A recording that should play on the board rather than sit on it as a chip.</summary>
    public sealed record Audio(Guid AssetId, string? Ext, string FileName, long Size, string ContentType) : PasteIntent;

    /// <inheritdoc cref="Audio" />
    public sealed record Video(Guid AssetId, string? Ext, string FileName, long Size, string ContentType) : PasteIntent;
    public sealed record Link(string Url) : PasteIntent;
    /// <param name="Zoom">How close to look, when whoever found the place knows how exactly they found
    /// it. Null keeps the map box's own default, which is what a bare coordinate pair gets.</param>
    public sealed record Map(double Latitude, double Longitude, string? Address = null, double? Zoom = null) : PasteIntent;

    /// <summary>
    /// A written address, not yet a place. Only the editor can finish this one: it asks the API's
    /// geocoder and turns it into a <see cref="Map"/>, or into <see cref="Text"/> when the address
    /// is not found. It never reaches the placer.
    /// </summary>
    public sealed record Place(string Address) : PasteIntent;
    public sealed record Html(string Markup, string PlainText) : PasteIntent;
    /// <summary>A grid pasted as tab-separated text, already squared up.</summary>
    public sealed record Table(List<List<string>> Rows, bool HasHeaderRow) : PasteIntent;

    public sealed record Text(string Value) : PasteIntent;
}

/// <summary>The classifier's answer: what to place and what to explain.</summary>
public sealed record PastePlan(List<PasteIntent> Intents, List<PasteRefusal> Refusals)
{
    /// <summary>Asset ids the browser stored that no intent uses, so the caller can delete them.</summary>
    public List<(Guid AssetId, string? Ext)> UnusedAssets { get; init; } = [];
}
