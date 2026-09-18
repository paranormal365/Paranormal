using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ben.Canvas.Core.Serialization;

/// <summary>
/// Reads an enum by name, and falls back to a default rather than refusing the file.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, and where it must NOT be used.</b> The rule for a block's <c>kind</c> is
/// the opposite of this one: an unknown kind fails the read, "so a file from a newer editor is refused
/// with a reason rather than opened with blank blocks". Losing a whole block silently would be worse
/// than a refusal a person can act on.</para>
///
/// <para>A style WITHIN a kind is a different question. If a later build adds a shape called "Star",
/// every board carrying one would become unreadable in this build — and what was lost is a rounded
/// corner, not a block. So a value this build does not know reads as the default, the board opens,
/// and <c>CanvasDocumentMigrations</c> is what makes that correction stick on the next save.</para>
///
/// <para>Writing is unchanged: always the name, never the number, so inserting a value into the enum
/// cannot shift what existing boards mean.</para>
/// </remarks>
public abstract class ForgivingEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    /// <summary>What an unknown name reads as.</summary>
    protected abstract T Fallback { get; }

    private T _fallback => Fallback;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) return _fallback;

        return Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : _fallback;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>A shape style this build does not know reads as a rectangle.</summary>
/// <remarks>
/// Its own type because <c>[JsonConverter]</c> needs a parameterless constructor, so the fallback
/// cannot be passed in.
/// </remarks>
public sealed class ShapeKindConverter : ForgivingEnumConverter<Model.ShapeKind>
{
    protected override Model.ShapeKind Fallback => Model.ShapeKind.Rectangle;
}
