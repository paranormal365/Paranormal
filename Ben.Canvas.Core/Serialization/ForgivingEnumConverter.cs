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

/// <summary>A block fill this build does not know reads as the accent bar.</summary>
public sealed class NodeFillConverter : ForgivingEnumConverter<Model.NodeFill>
{
    protected override Model.NodeFill Fallback => Model.NodeFill.Bar;
}

/// <summary>A group fill this build does not know reads as the dashed outline.</summary>
public sealed class GroupFillConverter : ForgivingEnumConverter<Model.GroupFill>
{
    protected override Model.GroupFill Fallback => Model.GroupFill.Outline;
}

/// <summary>A connector route this build does not know reads as the curve.</summary>
public sealed class EdgeRouteConverter : ForgivingEnumConverter<Model.EdgeRoute>
{
    protected override Model.EdgeRoute Fallback => Model.EdgeRoute.Curve;
}

/// <summary>A connector line style this build does not know reads as solid.</summary>
public sealed class EdgeLineConverter : ForgivingEnumConverter<Model.EdgeLine>
{
    protected override Model.EdgeLine Fallback => Model.EdgeLine.Solid;
}

/// <summary>
/// A connector end marker, where <c>null</c> is a real answer meaning "not chosen yet".
/// </summary>
/// <remarks>
/// The nullable form cannot share <see cref="ForgivingEnumConverter{T}"/>, which is for the value
/// itself. Null stays null — that is how a board written before markers existed is told apart from one
/// that deliberately chose no marker — and an unknown name reads as None.
/// </remarks>
public sealed class NullableEdgeMarkerConverter : System.Text.Json.Serialization.JsonConverter<Model.EdgeMarker?>
{
    public override Model.EdgeMarker? Read(
        ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType is System.Text.Json.JsonTokenType.Null) return null;
        if (reader.TokenType != System.Text.Json.JsonTokenType.String) return Model.EdgeMarker.None;

        return Enum.TryParse<Model.EdgeMarker>(reader.GetString(), ignoreCase: true, out var value)
               && Enum.IsDefined(value)
            ? value
            : Model.EdgeMarker.None;
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer, Model.EdgeMarker? value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString());
    }
}
