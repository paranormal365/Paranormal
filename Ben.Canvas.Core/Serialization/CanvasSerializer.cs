using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Paste;
using Ben.Canvas.Core.Text;

namespace Ben.Canvas.Core.Serialization;

/// <summary>
/// The only way a board is turned into text and back.
/// </summary>
/// <remarks>
/// <para>Every reader and writer - the device store, the server store, export, the clipboard and the tests -
/// uses these options and nothing else. Two option sets that disagree about enum names or property case
/// produce files one half of the editor cannot read.</para>
///
/// <para><see cref="Options"/> is indented, for files a person may open. <see cref="CompactOptions"/> is the
/// same contract without whitespace, for the device store and the clipboard, where every byte counts
/// against a small quota.</para>
///
/// <para>The document types are source-generated, so reading a large board does not pay for reflection on
/// a phone. Anything else falls back to the reflection resolver.</para>
/// </remarks>
public static class CanvasSerializer
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);

    public static readonly JsonSerializerOptions CompactOptions = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(CanvasJsonContext.Default, new DefaultJsonTypeInfoResolver()),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    public static string Serialize(CanvasDocument document, bool compact = false) =>
        JsonSerializer.Serialize(document, compact ? CompactOptions : Options);

    public static byte[] SerializeToUtf8Bytes(CanvasDocument document, bool compact = false) =>
        JsonSerializer.SerializeToUtf8Bytes(document, compact ? CompactOptions : Options);

    public static string Serialize<T>(T value, bool compact = false) =>
        JsonSerializer.Serialize(value, compact ? CompactOptions : Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static CanvasDocument? Deserialize(string json) => JsonSerializer.Deserialize<CanvasDocument>(json, Options);

    /// <summary>
    /// Reads a board, or says in a sentence why it cannot.
    /// </summary>
    /// <remarks>
    /// The checks run in the order a person would want the answer: an empty file is empty, not unreadable;
    /// valid JSON that is something else is "not a board", not a parser message; a newer board says so.
    /// Only a board that passes all of them is upgraded and returned.
    /// </remarks>
    public static (CanvasDocument? Document, string? Problem) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, CanvasCopy.Sentences.FileEmpty);

        CanvasDocument? document;
        try
        {
            if (!LooksLikeADocument(json)) return (null, CanvasCopy.Sentences.NotABoard);
            document = Deserialize(json);
        }
        catch (JsonException ex)
        {
            return (null, CanvasCopy.Sentences.NotReadable(ex.Message));
        }
        catch (NotSupportedException ex)
        {
            return (null, CanvasCopy.Sentences.NotReadable(ex.Message));
        }

        if (document is null) return (null, CanvasCopy.Sentences.NotABoard);

        if (document.SchemaVersion > CanvasDocument.CurrentSchemaVersion)
            return (null, CanvasCopy.Sentences.NewerFormat(document.SchemaVersion, CanvasDocument.CurrentSchemaVersion));

        return (CanvasDocumentMigrations.Upgrade(document), null);
    }

    /// <summary>A board has at least a format number and a list of blocks; anything else is some other JSON.</summary>
    private static bool LooksLikeADocument(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) return false;

        var hasVersion = false;
        var hasNodes = false;
        foreach (var property in parsed.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase)) hasVersion = true;
            if (property.Name.Equals("nodes", StringComparison.OrdinalIgnoreCase)) hasNodes = true;
        }

        return hasVersion && hasNodes;
    }
}

/// <summary>Source-generated metadata for the persisted types.</summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CanvasDocument))]
[JsonSerializable(typeof(CanvasClipboardPayload))]
[JsonSerializable(typeof(List<Ben.Canvas.Core.Persistence.DocumentSummary?>))]
[JsonSerializable(typeof(List<Ben.Canvas.Core.Persistence.DocumentSummary>))]
internal sealed partial class CanvasJsonContext : JsonSerializerContext;
