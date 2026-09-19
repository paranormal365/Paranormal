using System.Text.Json;

namespace Ben.Data.WebApi.Services.Canvas;

/// <summary>
/// Judges a save by somebody who may add to a board but not rework it.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-16: a member who can edit the case "can edit the board additively", while the author, a group
/// administrator and a site administrator may change what is already there. The editor can grey a handle, but only the
/// server can refuse the save that a crafted request makes anyway — so the rule lives here.</para>
///
/// <para><b>Added, not authored.</b> A piece belongs to whoever first saved it; that is recorded as the board is
/// written and passed in as <paramref name="owned"/>. Somebody adding to a board may rework and remove their own
/// pieces — a note they put up is theirs to take down — and must leave everybody else's exactly as they are, position
/// and all. Moving another person's card is changing their board.</para>
///
/// <para>Comparison is structural: the same values in a different property order is the same piece, because nothing
/// promises a browser serialises an object's fields in the order it read them.</para>
/// </remarks>
internal static class CanvasAdditiveGuard
{
    /// <summary>The collections a board keeps its pieces in; each element is identified by its <c>id</c>.</summary>
    private static readonly string[] Collections = ["nodes", "edges", "groups"];

    /// <summary>
    /// Null when the save only adds pieces (or changes ones this person added), otherwise the sentence to refuse with.
    /// </summary>
    public static string? WhyRefused(string storedJson, JsonElement incoming, IReadOnlySet<string> owned)
    {
        using var stored = JsonDocument.Parse(storedJson);

        foreach (var collection in Collections)
        {
            var before = ById(stored.RootElement, collection);
            var after = ById(incoming, collection);

            foreach (var (id, element) in before)
            {
                if (owned.Contains(id)) continue;   // their own piece: theirs to change or take away

                if (!after.TryGetValue(id, out var now))
                    return "This board is somebody else's work: you can add to it, but taking a piece away is for "
                         + "whoever put it there, a group administrator, or an administrator of the site.";

                if (!SameValue(element, now))
                    return "This board is somebody else's work: you can add to it, but changing or moving a piece is "
                         + "for whoever put it there, a group administrator, or an administrator of the site.";
            }
        }

        return null;
    }

    /// <summary>The ids of every piece in a board, for recording who added what.</summary>
    public static HashSet<string> IdsIn(JsonElement document)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var collection in Collections)
            foreach (var id in ById(document, collection).Keys)
                ids.Add(id);
        return ids;
    }

    private static Dictionary<string, JsonElement> ById(JsonElement document, string collection)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!document.TryGetProperty(collection, out var array) || array.ValueKind != JsonValueKind.Array) return map;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            if (!element.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
            map[id.GetString()!] = element;
        }
        return map;
    }

    /// <summary>Structural equality: same shape and same values, whatever order the properties arrived in.</summary>
    private static bool SameValue(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var left = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                if (left.Count != right.Count) return false;
                foreach (var (name, value) in left)
                {
                    if (!right.TryGetValue(name, out var other) || !SameValue(value, other)) return false;
                }
                return true;

            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength()) return false;
                using (var first = a.EnumerateArray())
                using (var second = b.EnumerateArray())
                {
                    while (first.MoveNext() && second.MoveNext())
                        if (!SameValue(first.Current, second.Current)) return false;
                }
                return true;

            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                // The text, not a double: a coordinate written 120 and 120.0 is the same place, and GetRawText tells
                // them apart, so both are read as numbers and compared as numbers.
                return a.GetDouble().Equals(b.GetDouble());

            default:
                return true;   // true, false and null carry nothing beyond their kind
        }
    }
}
