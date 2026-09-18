using System.Text.Json;

namespace Ben.Data.WebApi.Services.Canvas;

/// <summary>
/// The board ids a board's cards point at.
/// </summary>
/// <remarks>
/// <para><b>Why the server reads this and not only the editor.</b> Ben's rule, 2026-09-18: "the only
/// way for it to hit the load link to other page is if the other page has been published… it will
/// cause issues if one is not published and one that is published has a link to a page which is not
/// published." A picker that lists published boards only stops the state being CREATED; a crafted
/// request, or a target published-then-deleted between picking and publishing, needs the rule held
/// where it cannot be bypassed.</para>
///
/// <para>Read straight from the JSON with the same <c>nodes[].data.kind</c> walk
/// <c>CanvasDocumentController.SanitizeDocument</c> already does, because the API deliberately does
/// not reference <c>Ben.Canvas.Core</c> — it is a WebAssembly-facing library.</para>
/// </remarks>
public static class CanvasBoardLinks
{
    /// <summary>Every board a board's cards name, without duplicates.</summary>
    public static IReadOnlyList<Guid> TargetsIn(string? documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson)) return [];

        try
        {
            using var parsed = JsonDocument.Parse(documentJson);
            return TargetsIn(parsed.RootElement);
        }
        catch (JsonException)
        {
            // An unreadable board has no links worth enforcing; the save path refuses it on its own.
            return [];
        }
    }

    /// <summary>The same, for a document already parsed.</summary>
    public static IReadOnlyList<Guid> TargetsIn(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return [];
        if (!TryGetIgnoreCase(root, "nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return [];

        var found = new List<Guid>();

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetIgnoreCase(node, "data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetIgnoreCase(data, "kind", out var kind) || kind.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(kind.GetString(), "board", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryGetIgnoreCase(data, "documentId", out var id) || id.ValueKind != JsonValueKind.String) continue;
            if (!Guid.TryParse(id.GetString(), out var target) || target == Guid.Empty) continue;

            if (!found.Contains(target)) found.Add(target);
        }

        return found;
    }

    /// <summary>
    /// Property lookup that tolerates case, because a hand-written or re-serialized board can carry
    /// either — the same allowance the sanitizer makes.
    /// </summary>
    private static bool TryGetIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
