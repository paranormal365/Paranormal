using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Takes a session document out to somebody with no account, without taking the coordinates too.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A session document is returned verbatim everywhere else, because
/// it is the only copy that is definitely what the device wrote. A share link breaks that on
/// purpose in exactly one respect: a reading carries a GPS fix, and a fix taken inside somebody's
/// house is their street address. The person who made the link may choose to include them; the
/// default is that they do not travel, and this is what enforces it.</para>
///
/// <para><b>Swept by name, not by path.</b> The obvious implementation reaches into
/// <c>readings[].position</c> and clears two fields. The format's <c>position</c> object declares
/// <c>additionalProperties: true</c> and so does the format at large, so a device is free to write
/// a coordinate somewhere this code has never seen — and a redaction that only clears the places
/// it was told about fails silently and invisibly on the first such document. So the whole tree is
/// walked and every property whose name names a coordinate is nulled, wherever it sits and however
/// deep. Over-removal here costs a number nobody was promised; under-removal costs an address.</para>
///
/// <para><b>Nulled, not deleted.</b> The consumer is a schema that permits null for both, so a null
/// reads as "no fix" — a state a real indoor session reaches constantly. Removing the keys instead
/// would leave a document that differs structurally from every other one the player has parsed.</para>
///
/// <para><b>Accuracy and floor stay.</b> "Second floor, accurate to thirty metres" says the device
/// was indoors and how much to trust the numbers around it, and says nothing about where the
/// building is. The point of the redaction is the address, not the context.</para>
/// </remarks>
public static class SharedSessionDocument
{
    /// <summary>
    /// Property names that carry a coordinate. Matched case-insensitively against the whole
    /// document, so a nested or vendor-specific object cannot smuggle one past.
    /// </summary>
    private static readonly HashSet<string> CoordinateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "latitude", "longitude", "lat", "lon", "lng",
    };

    /// <summary>
    /// Returns the document with every coordinate nulled, and whether anything was actually
    /// removed — so the page reading it can say "positions were not shared" honestly, rather than
    /// claiming a redaction on a document that never had a fix in it.
    /// </summary>
    /// <remarks>
    /// An unparseable document is returned untouched with <c>false</c>. That looks like the wrong
    /// call for a security boundary, and would be — except the caller never reaches this method
    /// with a document it intends to send unredacted: <see cref="Prepare"/> is the only entry
    /// point, and it refuses to send anything it could not read.
    /// </remarks>
    private static (string Document, bool Removed) StripPositions(string document)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(document);
        }
        catch (JsonException)
        {
            return (document, false);
        }
        if (root is null) return (document, false);

        var removed = Strip(root);
        return (root.ToJsonString(), removed);
    }

    /// <summary>Walks the tree, nulling coordinates. Returns true if it nulled anything.</summary>
    private static bool Strip(JsonNode node)
    {
        var removed = false;

        switch (node)
        {
            case JsonObject obj:
                // Materialised first: assigning into the object while enumerating it throws.
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (CoordinateKeys.Contains(key))
                    {
                        // Already null is not a removal — saying "positions were withheld" about a
                        // session that never had one would be a lie in the reassuring direction.
                        if (obj[key] is not null) { obj[key] = null; removed = true; }
                        continue;
                    }
                    if (obj[key] is { } child) removed |= Strip(child);
                }
                break;

            case JsonArray array:
                foreach (var item in array)
                    if (item is not null) removed |= Strip(item);
                break;
        }

        return removed;
    }

    /// <summary>
    /// The one entry point: what a shared link should send for this document.
    /// </summary>
    /// <returns>
    /// The document to send and whether coordinates were withheld from it. Null when
    /// <paramref name="includePositions"/> is false and the document could not be parsed — a
    /// document this code cannot read is a document it cannot redact, and sending it anyway on the
    /// hope that it holds no fix is the failure this whole class exists to prevent.
    /// </returns>
    public static (string Document, bool PositionsWithheld)? Prepare(string document, bool includePositions)
    {
        if (includePositions) return (document, false);

        try
        {
            _ = JsonNode.Parse(document);
        }
        catch (JsonException)
        {
            return null;
        }

        var (redacted, removed) = StripPositions(document);
        return (redacted, removed);
    }
}
