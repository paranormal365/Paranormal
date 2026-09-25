using System.Text.Json;

namespace Ben.Data.WebApi.Services.Canvas;

/// <summary>
/// Who put each piece on a board: piece id to person, kept beside the board rather than inside it.
/// </summary>
/// <remarks>
/// <para>It is beside the document because the document belongs to the editor: a field the browser writes is a claim,
/// and "who added this" decides whether somebody may change it. Only the server may answer that, so only the server
/// keeps it.</para>
/// <para><b>First writer wins and is never rewritten.</b> A piece belongs to whoever first saved it; somebody with
/// full rights reworking it later does not take it over, and a piece that disappears keeps its entry so a board it is
/// restored onto still knows whose it was.</para>
/// </remarks>
internal static class PieceOwners
{
    public static Dictionary<string, Guid> Read(string? json)
    {
        var owners = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return owners;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return owners;

            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String && Guid.TryParse(property.Value.GetString(), out var id))
                    owners[property.Name] = id;
        }
        catch (JsonException)
        {
            // A column nothing but this class writes should not be unreadable; if it ever is, an empty answer makes
            // every piece somebody else's, which refuses more than it allows. That is the safe direction.
        }
        return owners;
    }

    /// <summary>The ids this person added.</summary>
    public static HashSet<string> Owned(Dictionary<string, Guid> owners, Guid userId)
        => [.. owners.Where(p => p.Value == userId).Select(p => p.Key)];

    /// <summary>Records the pieces this save introduced, leaving every earlier entry as it stands.</summary>
    public static string Write(Dictionary<string, Guid> owners, IEnumerable<string> idsNow, Guid userId)
    {
        foreach (var id in idsNow)
            owners.TryAdd(id, userId);

        return JsonSerializer.Serialize(owners.ToDictionary(p => p.Key, p => p.Value.ToString("D")));
    }
}
