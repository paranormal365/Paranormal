using System.Globalization;
using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// What a session's comparisons with a place's room scans found, as rows the place can count
/// (LiDAR plan, phase 2).
/// </summary>
/// <remarks>
/// <para><b>Read from the session document, on the upload that brings it</b> — not a route of its
/// own. Each <c>scan_change</c> reading (spec 1.2) is a row; sending the session again replaces its
/// rows, because the document is the record and the rows are only what the place keeps of it.</para>
///
/// <para><b>Only against scans that exist.</b> A change names the version of the room's scan it was
/// made against; one naming no scan this server knows is left out rather than refused — the
/// session is still the person's, and its readings still say what they say.</para>
/// </remarks>
public static class PlaceScanChangeRows
{
    /// <summary>Replaces the session's rows with what its document says. The caller saves.</summary>
    public static async Task ReplaceAsync(BenDataContext db, FieldSessionUpload session, string document, CancellationToken ct)
    {
        db.PlaceScanChanges.RemoveRange(
            await db.PlaceScanChanges.Where(c => c.FieldSessionUploadId == session.Id).ToListAsync(ct));

        var rows = Read(document);
        if (rows.Count == 0) return;

        var named = rows.Select(r => r.PlaceScanId).Distinct().ToList();
        var known = (await db.PlaceScans.AsNoTracking()
            .Where(s => named.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct)).ToHashSet();

        foreach (var row in rows.Where(r => known.Contains(r.PlaceScanId)))
        {
            row.FieldSessionUploadId = session.Id;
            db.PlaceScanChanges.Add(row);
        }
    }

    /// <summary>The rows a document's <c>scan_change</c> readings describe, not yet tied to a session.</summary>
    public static List<PlaceScanChange> Read(string document)
    {
        var rows = new List<PlaceScanChange>();
        using var parsed = JsonDocument.Parse(document);
        if (!parsed.RootElement.TryGetProperty("readings", out var readings) || readings.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var reading in readings.EnumerateArray())
        {
            if (reading.ValueKind != JsonValueKind.Object
                || !reading.TryGetProperty("measurements", out var measurements) || measurements.ValueKind != JsonValueKind.Object
                || !measurements.TryGetProperty("scan_change", out var change) || change.ValueKind != JsonValueKind.Object)
                continue;

            var kind = Text(change, "value") switch
            {
                "moved" => PlaceScanChangeKind.Moved,
                "missing" => PlaceScanChangeKind.Missing,
                "new" => PlaceScanChangeKind.New,
                _ => (PlaceScanChangeKind?)null,
            };
            if (kind is null || !Guid.TryParse(Text(change, "place_scan_id"), out var placeScanId)) continue;
            if (!reading.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String
                || !DateTime.TryParse(at.GetString(), CultureInfo.InvariantCulture,
                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var comparedUtc))
                continue;

            var where = Numbers(change, "where");
            var size = Numbers(change, "size_cm");
            var category = Text(change, "object") is { Length: > 0 } o ? o : "unclassified";
            var picture = Text(change, "picture");

            rows.Add(new PlaceScanChange
            {
                Id = Guid.NewGuid(),
                PlaceScanId = placeScanId,
                Kind = kind.Value,
                ObjectCategory = category.Length > 40 ? category[..40] : category,
                DistanceCm = Number(change, "distance_cm"),
                RotationDeg = Number(change, "rotation_deg"),
                SizeXCm = size?[0], SizeYCm = size?[1], SizeZCm = size?[2],
                WhereX = where?[0] ?? 0, WhereY = where?[1] ?? 0, WhereZ = where?[2] ?? 0,
                PictureEntryPath = picture is { Length: <= 400 } ? picture : null,
                ComparedUtc = comparedUtc,
            });
        }
        return rows;
    }

    private static string? Text(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Number(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static double[]? Numbers(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3) return null;
        var values = v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out var d) ? d : double.NaN).ToArray();
        return values.Any(double.IsNaN) ? null : values;
    }
}
