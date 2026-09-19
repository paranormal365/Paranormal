using System.Text.Json;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The document is well-formed JSON in the right shape — <c>DeviceDataSummary.Read</c> settled
/// that. This asks the next question: could a Field Kit have written these numbers?
/// </summary>
/// <remarks>
/// <para>Nothing here executes, so a hostile document cannot hurt the server. What it can do is
/// lie: readings from before the session started, a magnetic field ten times what any phone can
/// measure, a position in the middle of the Pacific for a cellar in Nashville. Those would play
/// back as evidence. Each rule below is a physical or structural fact about what the app records,
/// and a document that breaks one is refused with a sentence naming the first reading that did.</para>
///
/// <para>The ranges are generous on purpose. A phone's magnetometer saturates around
/// ±2,000 µT and the app records a magnitude, so 10,000 µT is well past any real reading without
/// being tight enough to refuse an odd but honest one. Sound is dBFS, which cannot exceed 0.</para>
/// </remarks>
public static class FieldSessionDocumentGuard
{
    /// <summary>A twelve-hour session at one reading a second is 43,200; this is more than double.</summary>
    public const int MaxReadings = 100_000;
    public const double MaxMicrotesla = 10_000;
    public const double MinSoundDbfs = -200;
    /// <summary>How far outside the session's own window a reading may fall — clocks drift, and the
    /// app stamps the window from the same clock, so this is slack, not tolerance for invention.</summary>
    public static readonly TimeSpan WindowSlack = TimeSpan.FromMinutes(5);

    public static string? Refusal(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("session", out var session)
            || !session.TryGetProperty("started_at", out var startedAtEl)
            || !TryDate(startedAtEl, out var startedAt))
            return "the session does not say when it started.";

        DateTime? endedAt = session.TryGetProperty("ended_at", out var endedEl) && TryDate(endedEl, out var e) ? e : null;
        if (endedAt is { } end && end < startedAt)
            return "the session ends before it starts.";

        if (!root.TryGetProperty("readings", out var readings) || readings.ValueKind != JsonValueKind.Array)
            return null;   // the empty-document rule lives at the door

        var count = readings.GetArrayLength();
        if (count > MaxReadings)
            return $"it carries {count:N0} readings; a session can carry at most {MaxReadings:N0}.";

        var windowStart = startedAt - WindowSlack;
        var windowEnd = (endedAt ?? DateTime.MaxValue.AddDays(-1)) + WindowSlack;
        DateTime? previous = null;
        var index = 0;

        foreach (var reading in readings.EnumerateArray())
        {
            index++;
            if (!reading.TryGetProperty("at", out var atEl) || !TryDate(atEl, out var at))
                return $"reading {index} has no time.";
            if (at < windowStart || at > windowEnd)
                return $"reading {index} is stamped {at:u}, outside the session's own window.";
            if (previous is { } p && at < p)
                return $"reading {index} is stamped before the reading before it.";
            previous = at;

            if (reading.TryGetProperty("measurements", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                if (Value(m, "emf") is { } emf && (double.IsNaN(emf) || emf < 0 || emf > MaxMicrotesla))
                    return $"reading {index} claims a magnetic field of {emf} µT; no phone measures that.";
                if (Value(m, "sound_level") is { } dbfs && (double.IsNaN(dbfs) || dbfs > 0 || dbfs < MinSoundDbfs))
                    return $"reading {index} claims a sound level of {dbfs} dBFS; that scale tops out at 0.";
            }

            if (reading.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Object)
            {
                var lat = Number(pos, "latitude");
                var lon = Number(pos, "longitude");
                if (lat is { } la && (double.IsNaN(la) || la < -90 || la > 90))
                    return $"reading {index} has a latitude of {la}; the Earth stops at 90.";
                if (lon is { } lo && (double.IsNaN(lo) || lo < -180 || lo > 180))
                    return $"reading {index} has a longitude of {lo}; the Earth stops at 180.";
                if (Number(pos, "accuracy_meters") is { } acc && (double.IsNaN(acc) || acc < 0))
                    return $"reading {index} has a negative position accuracy.";
            }

            if (reading.TryGetProperty("motion", out var motion) && motion.ValueKind == JsonValueKind.Object
                && Number(motion, "heading_degrees") is { } heading
                && (double.IsNaN(heading) || heading < 0 || heading > 360))
                return $"reading {index} has a heading of {heading}°; a compass goes to 360.";
        }

        return null;
    }

    private static bool TryDate(JsonElement el, out DateTime value)
    {
        value = default;
        if (el.ValueKind != JsonValueKind.String || !el.TryGetDateTime(out var parsed)) return false;
        value = parsed.ToUniversalTime();
        return true;
    }

    /// <summary>A measurement is <c>{ "value": n, … }</c>; anything else is not a number to judge.</summary>
    private static double? Value(JsonElement measurements, string name)
        => measurements.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Object
            ? Number(el, "value") : null;

    private static double? Number(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)
            ? d : null;
}
