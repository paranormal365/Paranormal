using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// One paragraph that lets a cited field session stand on its own in a printed report.
/// </summary>
/// <remarks>
/// <para>A report citation used to say when the session ran, who recorded it and how many
/// readings it holds. A client reading the PDF, not the site, learns nothing from that they can
/// check. This says what the night held: how long, in which rooms, where the magnetic field
/// peaked and when, against what base level, what was being recorded at that moment, and the
/// mark the investigator placed there if they placed one.</para>
///
/// <para>It is a pure function of the document the app uploaded — the same JSON the player
/// reads — so it needs no new column and cannot disagree with playback. Units follow the site:
/// the app records microtesla, the gauge and the player show milligauss (×10).</para>
/// </remarks>
public static class FieldSessionReadout
{
    public sealed record Result(
        TimeSpan Duration, IReadOnlyList<string> Rooms,
        double? PeakMilligauss, double? BaselineMilligauss, TimeSpan? PeakOffset, string? PeakRoom,
        IReadOnlyList<string> RecordingAtPeak, string? MarkNearPeak,
        double? PeakSoundDbfs, TimeSpan? PeakSoundOffset,
        int Photos, int Recordings, string Sentence);

    /// <summary>Null when the document cannot be read as a session; a report never invents a readout.</summary>
    public static Result? Compose(string? documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson)) return null;
        try { return ComposeCore(documentJson); }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static Result? ComposeCore(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("readings", out var readings) || readings.ValueKind != JsonValueKind.Array)
            return null;

        DateTime? start = null, end = null;
        double? baselineUt = null;
        double? peakUt = null; DateTime peakAt = default; string? peakRoom = null;
        double? peakDb = null; DateTime peakDbAt = default;
        var rooms = new List<string>();
        string? room = null;
        var photos = 0; var recordings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var audioSpans = new List<(string Name, DateTime Start, DateTime? End)>();
        var marks = new List<(DateTime At, string Text)>();

        foreach (var r in readings.EnumerateArray())
        {
            if (!r.TryGetProperty("at", out var atEl) || !atEl.TryGetDateTime(out var at)) continue;
            at = at.ToUniversalTime();
            start ??= at; end = at;

            if (r.TryGetProperty("measurements", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                if (m.TryGetProperty("room", out var rm) && rm.TryGetProperty("value", out var rv) && rv.ValueKind == JsonValueKind.String)
                {
                    room = rv.GetString();
                    if (room is not null && !rooms.Contains(room)) rooms.Add(room);
                }
                if (m.TryGetProperty("emf", out var emf) && emf.ValueKind == JsonValueKind.Object)
                {
                    if (Number(emf, "baseline") is { } b) baselineUt ??= b;
                    if (Number(emf, "value") is { } v && (peakUt is null || v > peakUt))
                    {
                        peakUt = v; peakAt = at; peakRoom = room;
                    }
                }
                if (m.TryGetProperty("sound_level", out var snd) && snd.ValueKind == JsonValueKind.Object
                    && Number(snd, "value") is { } db && (peakDb is null || db > peakDb))
                {
                    peakDb = db; peakDbAt = at;
                }
                if (m.TryGetProperty("marker", out var mk) && mk.TryGetProperty("value", out var mv) && mv.ValueKind == JsonValueKind.String)
                {
                    var note = r.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    marks.Add((at, string.IsNullOrWhiteSpace(note) ? Humanise(mv.GetString()!) : note!));
                }
            }

            if (r.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
            {
                var text = noteEl.GetString() ?? "";
                if (text.StartsWith("photo: ", StringComparison.OrdinalIgnoreCase)) photos++;
                else if (text.StartsWith("video: ", StringComparison.OrdinalIgnoreCase)) recordings.Add(text[7..].Trim());
            }

            if (r.TryGetProperty("audio_ref", out var audio) && audio.ValueKind == JsonValueKind.Object
                && audio.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String)
            {
                var name = fn.GetString()!;
                recordings.Add(name);
                var offset = Number(audio, "start_offset_seconds") ?? 0;
                var duration = Number(audio, "duration_seconds");
                var began = at.AddSeconds(-offset);
                if (!audioSpans.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    audioSpans.Add((name, began, duration is { } d ? began.AddSeconds(d) : null));
            }
        }

        if (start is null) return null;
        var length = end!.Value - start.Value;

        var atPeak = new List<string>();
        string? markNear = null;
        TimeSpan? peakOffset = null;
        if (peakUt is not null)
        {
            peakOffset = peakAt - start.Value;
            foreach (var s in audioSpans)
                if (s.Start <= peakAt && (s.End is null || s.End >= peakAt)) atPeak.Add(FileLeaf(s.Name));
            markNear = marks.Where(k => Math.Abs((k.At - peakAt).TotalSeconds) <= 30)
                            .OrderBy(k => Math.Abs((k.At - peakAt).TotalSeconds))
                            .Select(k => k.Text).FirstOrDefault();
        }

        var result = new Result(
            length, rooms,
            peakUt is { } p ? Math.Round(p * 10, 0) : null,
            baselineUt is { } bl ? Math.Round(bl * 10, 0) : null,
            peakOffset, peakRoom, atPeak, markNear,
            peakDb, peakDb is null ? null : peakDbAt - start.Value,
            photos, recordings.Count, "");
        return result with { Sentence = Sentence(result) };
    }

    /// <summary>The paragraph. Written so that every clause is a fact from the document and a
    /// missing channel drops its clause rather than inventing a value.</summary>
    private static string Sentence(Result r)
    {
        var sb = new StringBuilder();
        sb.Append("Over ").Append(Span(r.Duration));
        if (r.Rooms.Count == 1) sb.Append(" in the ").Append(r.Rooms[0]);
        else if (r.Rooms.Count > 1) sb.Append(" through ").Append(Join(r.Rooms));
        sb.Append(", ");

        if (r.PeakMilligauss is { } peak)
        {
            sb.Append("the magnetic field peaked at ").Append(peak.ToString("N0", CultureInfo.InvariantCulture)).Append(" mG");
            if (r.BaselineMilligauss is { } b) sb.Append(" against a base of ").Append(b.ToString("N0", CultureInfo.InvariantCulture)).Append(" mG");
            if (r.PeakOffset is { } off) sb.Append(' ').Append(Span(off)).Append(" in");
            if (r.PeakRoom is not null && r.Rooms.Count > 1) sb.Append(", in the ").Append(r.PeakRoom);
            if (r.RecordingAtPeak.Count > 0) sb.Append(", while ").Append(Join(r.RecordingAtPeak)).Append(r.RecordingAtPeak.Count == 1 ? " was" : " were").Append(" recording");
            sb.Append('.');
            if (r.MarkNearPeak is not null) sb.Append(" A mark was placed there: “").Append(r.MarkNearPeak).Append("”.");
        }
        else if (r.PeakSoundDbfs is { } db)
        {
            sb.Append("no magnetic field was recorded; sound peaked at ").Append(db.ToString("N0", CultureInfo.InvariantCulture)).Append(" dBFS");
            if (r.PeakSoundOffset is { } off) sb.Append(' ').Append(Span(off)).Append(" in");
            sb.Append('.');
        }
        else
        {
            sb.Append("no magnetic field or sound level was recorded.");
        }

        var extras = new List<string>();
        if (r.Recordings > 0) extras.Add(r.Recordings == 1 ? "one recording" : $"{r.Recordings} recordings");
        if (r.Photos > 0) extras.Add(r.Photos == 1 ? "one photo" : $"{r.Photos} photos");
        if (extras.Count > 0) sb.Append(" The session carries ").Append(Join(extras)).Append('.');
        return sb.ToString();
    }

    private static string Span(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min"
         : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s"
         : $"{t.Seconds} s";

    private static string Join(IReadOnlyList<string> items)
        => items.Count <= 1 ? string.Join("", items)
         : items.Count == 2 ? $"{items[0]} and {items[1]}"
         : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    private static string FileLeaf(string path) => path.Replace('\\', '/').Split('/')[^1];

    private static string Humanise(string marker) => marker switch
    {
        "sentry_emf" => "magnetic spike", "sentry_sound" => "sound spike", "manual_marker" => "a manual mark",
        "audio" => "audio started", "evp" => "an EVP question", _ => marker.Replace('_', ' '),
    };

    private static double? Number(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) ? d : null;
}
