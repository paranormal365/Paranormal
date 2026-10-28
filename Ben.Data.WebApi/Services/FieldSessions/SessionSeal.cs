using System.Text.Json;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>
/// What the phone sealed for each recording in a session's .ben: its SHA-256 and size, written when
/// the session was saved (<c>SessionSeal.swift</c>). Read straight from the .ben, which is never edited.
/// </summary>
/// <remarks>
/// The seal is not signed by the phone yet (its <c>signature</c> field is planned), so it proves the
/// recording is what the phone wrote when it saved the session, not who the phone was. A copy made
/// for scanning is checked against it, and a certificate says whether it matched (10/28/2026).
/// </remarks>
public static class SessionSeal
{
    public sealed record Entry(string Sha256, long Bytes);

    /// <summary>The sealed entries by path, or an empty map when there is no seal or it can't be read.</summary>
    public static async Task<IReadOnlyDictionary<string, Entry>> ReadAsync(
        IBenBundleStore bundles, string bundlePath, CancellationToken ct)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var stream = await bundles.OpenEntryAsync(bundlePath, BenBundle.SealEntryPath, ct);
            if (stream is null) return entries;
            using var seal = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!seal.RootElement.TryGetProperty("entries", out var list) || list.ValueKind != JsonValueKind.Array)
                return entries;
            foreach (var e in list.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var path = e.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                var sha = e.TryGetProperty("sha256", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
                var bytes = e.TryGetProperty("byte_count", out var b) && b.TryGetInt64(out var n) ? n : -1;
                if (path is { Length: > 0 } && sha is { Length: 64 }) entries[path] = new Entry(sha.ToLowerInvariant(), bytes);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            // A seal that can't be read is reported as no seal: never as a match.
        }
        return entries;
    }
}
