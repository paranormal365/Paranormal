using System.Text.Json;
using Ben.Service.Models.FieldSessions;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>A file a viewer may be served, with its length when the server could read it.</summary>
public sealed record TimelineFile(Guid FileId, string RelativePath, double? DurationSeconds);

/// <summary>
/// Turns the phone's session document into the <see cref="SessionTimeline"/> a player replays.
/// </summary>
/// <remarks>
/// <para><b>The rules are <c>ProjectNotes/specs/replay-semantics.md</c></b>, and the rule ids in the
/// comments below (S1, I3, M4…) are its rows. The phone's replay obeys the same table, and both
/// are run against the same fixtures (<c>Ben.iOS/BenKit/Tests/BenKitTests/Fixtures/replay-conformance</c>).</para>
///
/// <para><b>Pure.</b> No storage, no database, no clock: a document and a list of files in, a
/// timeline out. The controller does the reading and the access check; this does the meaning. That
/// is what lets it be tested on a fixture and cached by its inputs.</para>
///
/// <para><b>Privacy is the caller's.</b> The document handed in has already been through the
/// redaction the audience needs (<see cref="SharedSessionDocument.Prepare"/>,
/// <see cref="PublicSessionBundle.Sanitize"/>), so there is one redaction path, not two. The policy
/// passed here only decides what is DRAWN: no track unless positions are exact.</para>
///
/// <para><b>Never throws on a bad document.</b> A reading it cannot place is counted in
/// <see cref="SessionTimeline.SkippedReadings"/> and left out (S3). A document that is not JSON at
/// all is the caller's to refuse before getting here.</para>
/// </remarks>
public static class FieldSessionTimelineBuilder
{
    /// <summary>The most points either trace keeps; extremes survive the thinning.</summary>
    public const int TracePoints = 400;
    /// <summary>The most points the drawn path keeps.</summary>
    public const int TrackPoints = 4000;
    /// <summary>Fixes closer than this to the last one kept add nothing to a drawn path.</summary>
    public const double TrackSpacingMeters = 1.0;

    /// <summary>Kinds the phone raises by itself, drawn differently from the ones a person marks.</summary>
    private static readonly HashSet<string> AutomaticKinds = new(StringComparer.Ordinal)
    {
        "sentry_emf", "sentry_sound", "device_moved", "scene_motion", "app_backgrounded", "app_returned",
        "scan_change", "figure_seen", "unexplained_shape",
    };

    private static readonly HashSet<string> CaptureKinds = new(StringComparer.Ordinal) { "photo", "video", "audio" };

    /// <param name="arrived">Every file the session brought, when <paramref name="files"/> is only what this
    /// audience may play (a share link of one file, the public archive's approved ones).</param>
    public static SessionTimeline Build(string document, IReadOnlyList<TimelineFile> files, TimelinePositionPolicy policy,
                                        IReadOnlyList<string>? arrived = null)
    {
        using var parsed = JsonDocument.Parse(document);
        var root = parsed.RootElement;

        DateTime? startedAt = null, endedAt = null;
        string? timeZoneId = null;
        double? batteryAtStart = null;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object)
        {
            startedAt = Moment(session, "started_at");
            endedAt = Moment(session, "ended_at");
            timeZoneId = KnownZone(Text(session, "timezone"));
            batteryAtStart = Number(session, "battery_percent_at_start");
        }

        // ── The readings, in time order (S3) ──────────────────────────────────
        var rows = new List<Row>();
        var skipped = 0;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("readings", out var readings) && readings.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in readings.EnumerateArray())
            {
                if (Read(element) is { } row) rows.Add(row);
                else skipped++;
            }
        }
        rows = rows.OrderBy(r => r.At).ToList();   // stable: equal times keep their written order

        // ── Bounds (S1, S2) ────────────────────────────────────────────────────
        var start = startedAt is { } began && (rows.Count == 0 || began <= rows[0].At)
            ? began
            : rows.Count > 0 ? rows[0].At : startedAt ?? DateTime.UnixEpoch;
        long Ms(DateTime at) => (long)Math.Round((at - start).TotalMilliseconds);

        // ── Base in force (I3), rooms (I5) ─────────────────────────────────────
        var n = rows.Count;
        var t = new long[n];
        var emf = new double?[n];
        var emfBase = new double?[n];
        var sound = new double?[n];
        var lat = new double?[n];
        var lon = new double?[n];
        var accuracy = new double?[n];
        var heading = new double?[n];
        var course = new double?[n];
        var room = new int?[n];
        var rooms = new List<string>();
        var roomIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        double? baseInForce = null, lastBase = null;
        var withPositions = policy != TimelinePositionPolicy.Withheld;

        for (var i = 0; i < n; i++)
        {
            var row = rows[i];
            t[i] = Ms(row.At);
            if (row.EmfBase is { } carried) { baseInForce = carried; lastBase = carried; }
            emf[i] = Round(row.Emf, 4);
            emfBase[i] = Round(baseInForce, 4);
            sound[i] = Round(row.Sound, 2);
            if (withPositions && row.Lat is not null && row.Lon is not null)
            {
                lat[i] = Round(row.Lat, 7);
                lon[i] = Round(row.Lon, 7);
                accuracy[i] = Round(row.Accuracy, 1);
            }
            heading[i] = Round(row.Heading, 1);
            course[i] = Round(row.Course, 1);
            if (row.Room is { Length: > 0 } name)
            {
                if (!roomIndex.TryGetValue(name, out var index))
                {
                    index = rooms.Count;
                    rooms.Add(name);
                    roomIndex[name] = index;
                }
                room[i] = index;
            }
        }

        // ── Details added to marks after the session (10/15/2026) ─────────────
        // Beside the readings, never in them: each names its mark by the mark's moment and kind.
        var details = new Dictionary<(long, string), (string Text, DateTime? AddedAt)>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("mark_details", out var added) && added.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in added.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var at = Moment(entry, "at");
                var kindOf = entry.TryGetProperty("marker", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                var text = entry.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                if (at is null || string.IsNullOrWhiteSpace(kindOf) || string.IsNullOrWhiteSpace(text)) continue;
                details[(Ms(at.Value), kindOf)] = (text.Trim(), Moment(entry, "added_at"));
            }
        }

        // ── Markers (M1) ───────────────────────────────────────────────────────
        var markers = new List<TimelineMarker>();
        for (var i = 0; i < n; i++)
        {
            var row = rows[i];
            if (row.Marker is not { Length: > 0 } kind || CaptureKinds.Contains(kind)) continue;
            double? field = row.Emf is { } value
                ? Math.Round((value - (emfBase[i] ?? lastBase ?? 0)) * 10, 1)
                : null;
            var roomName = room[i] is { } r ? rooms[r] : null;
            details.TryGetValue((t[i], kind), out var addedLater);
            markers.Add(new TimelineMarker(t[i], kind, Title(kind), AutomaticKinds.Contains(kind), row.Note, roomName, field,
                addedLater.Text, addedLater.AddedAt));
        }

        // ── Recordings and photographs (M2–M5) ────────────────────────────────
        var startsFromReference = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var lengthFromReference = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var notedEnds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var photoTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var photoRooms = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        // Everything the document names, with when it first names it, to say which never arrived.
        var named = new Dictionary<string, (string Kind, long T)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < n; i++)
        {
            var row = rows[i];
            if (row.AudioFile is { } audioFile)
            {
                var path = Normalise(audioFile);
                named.TryAdd(path, (IsVideo(path) ? "video" : "audio", t[i]));
                if (row.AudioDuration is { } length && length > 0) lengthFromReference.TryAdd(path, length);
                // M5: a reference taken while the recording ran says how far in it was. M4: the one
                // written when a clip ended says how long it ran. The first reference fixes the start.
                if (row.AudioOffset is { } offset)
                    startsFromReference.TryAdd(path, t[i] - (long)Math.Round(offset * 1000));
                else if (row.AudioDuration is { } duration && duration > 0)
                    startsFromReference.TryAdd(path, t[i] - (long)Math.Round(duration * 1000));
            }
            if (row.Note is { } note)
            {
                if (StartsWith(note, "photo: ", out var photo) && photoTimes.TryAdd(Normalise(photo), t[i]))
                {
                    photoRooms[Normalise(photo)] = room[i] is { } r ? rooms[r] : null;
                    named.TryAdd(Normalise(photo), ("photo", t[i]));
                }
                if (StartsWith(note, "video: ", out var video))
                {
                    notedEnds.TryAdd(Normalise(video), t[i]);
                    named.TryAdd(Normalise(video), ("video", t[i]));
                }
                if (StartsWith(note, "audio: ", out var audio))
                {
                    notedEnds.TryAdd(Normalise(audio), t[i]);
                    named.TryAdd(Normalise(audio), ("audio", t[i]));
                }
            }
        }

        var clips = new List<TimelineClip>();
        var photos = new List<TimelinePhoto>();
        foreach (var file in files)
        {
            var path = Normalise(file.RelativePath);
            if (IsImage(path))
            {
                // A photo with no "photo:" note has no moment; it is listed with the files, not on
                // the timeline.
                if (photoTimes.TryGetValue(path, out var takenAt))
                    photos.Add(new TimelinePhoto(takenAt, file.FileId, file.RelativePath,
                                                 photoRooms.GetValueOrDefault(path)));
                continue;
            }
            var kind = IsAudio(path) ? "audio" : IsVideo(path) ? "video" : null;
            if (kind is null) continue;

            var seconds = file.DurationSeconds is > 0 ? file.DurationSeconds
                        : lengthFromReference.TryGetValue(path, out var referenced) ? referenced : (double?)null;
            long? durationMs = seconds is { } s ? (long)Math.Round(s * 1000) : null;

            long? startMs;
            long? notedEndMs = notedEnds.TryGetValue(path, out var end) ? end : null;
            if (startsFromReference.TryGetValue(path, out var fromReference)) startMs = fromReference;
            else if (notedEndMs is { } ended) startMs = durationMs is { } length ? ended - length : null;
            // A recording nothing in the document mentions: from the session's start, which is
            // what the player has always done with one.
            else startMs = 0;

            clips.Add(new TimelineClip(file.FileId, file.RelativePath, kind, startMs, durationMs, notedEndMs));
        }
        clips = clips.OrderBy(c => c.StartMs ?? c.NotedEndMs ?? 0).ToList();
        photos = photos.OrderBy(p => p.T).ToList();

        // ── The end (S2) ───────────────────────────────────────────────────────
        var lastReadingMs = n > 0 ? t[^1] : 0;
        long endMs;
        if (endedAt is { } ended2)
        {
            endMs = Math.Max(Ms(ended2), lastReadingMs);
        }
        else
        {
            endMs = lastReadingMs;
            foreach (var clip in clips)
                if (clip.StartMs is { } s && clip.DurationMs is { } d) endMs = Math.Max(endMs, s + d);
        }
        endMs = Math.Max(0, endMs);

        // ── Away stretches, traces, the drawn path ─────────────────────────────
        var away = AwayStretches(markers, endMs);
        var magneticFromBase = emfBase.Any(b => b is not null);
        var magnetic = Thin(Enumerable.Range(0, n)
            .Where(i => emf[i] is not null)
            .Select(i => new[] { (double)t[i], Math.Round(((double)emf[i]! - (emfBase[i] ?? lastBase ?? 0)) * 10, 2) })
            .ToList());
        var soundTrace = Thin(Enumerable.Range(0, n)
            .Where(i => sound[i] is not null)
            .Select(i => new[] { (double)t[i], (double)sound[i]! })
            .ToList());
        var track = policy == TimelinePositionPolicy.Exact ? Track(lat, lon) : [];

        var (roomScans, scanChanges) = root.ValueKind == JsonValueKind.Object
                                       && root.TryGetProperty("readings", out var scanReadings)
            ? ScanReadings(scanReadings, Ms)
            : ([], []);
        var (figures, shapes) = root.ValueKind == JsonValueKind.Object
                                && root.TryGetProperty("readings", out var mapReadings)
            ? PersonMapReadings(mapReadings, Ms, files)
            : ([], []);

        var brought = (arrived ?? files.Select(f => f.RelativePath).ToList())
            .Select(Normalise).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = named
            .Where(kv => !brought.Contains(kv.Key))
            .Select(kv => new TimelineMissing(kv.Key, kv.Value.Kind, kv.Value.T))
            .OrderBy(m => m.T)
            .ToList();

        var timeline = new SessionTimeline(
            Version: SessionTimeline.CurrentVersion,
            StartUtc: DateTime.SpecifyKind(start, DateTimeKind.Utc),
            DurationMs: endMs,
            HasEndTime: endedAt is not null,
            Positions: policy,
            Readings: new TimelineReadings(t, emf, emfBase, sound, lat, lon, accuracy, heading, course, room),
            Rooms: rooms,
            Markers: markers,
            Photos: photos,
            Clips: clips,
            Away: away,
            Trace: new TimelineTrace(magnetic, soundTrace, magneticFromBase),
            Track: track,
            SkippedReadings: skipped,
            FallbackEmfBase: lastBase,
            TimeZoneId: timeZoneId,
            BatteryAtStart: batteryAtStart is >= 0 and <= 100 ? Math.Round(batteryAtStart.Value) : null,
            Missing: missing,
            RoomScans: roomScans,
            ScanChanges: scanChanges,
            Figures: figures,
            Shapes: shapes);

        // A question's listening window, by the same rule the EVP scanner notes candidates against.
        var windows = Audio.Scanning.EvpQuestionWindows.Of(timeline);
        if (windows.Count == 0) return timeline;
        var until = windows.ToDictionary(w => w.FromMs, w => w.ToMs);
        return timeline with
        {
            Markers = timeline.Markers
                .Select(m => m.Kind == "evp_question" && until.TryGetValue(m.T, out var to) ? m with { ListenUntilMs = to } : m)
                .ToList(),
        };
    }

    /// <summary>
    /// The person map's figures and shapes a document records (LiDAR plan, phase 4, spec 1.2): a
    /// <c>figure</c> measurement is a sighting whose value is its joints file, a <c>shape</c> one
    /// says its size in words. Each names the video it was in and its picture, and those are found
    /// among the session's files, so a player can fetch them by id.
    /// </summary>
    private static (List<TimelineFigure> Figures, List<TimelineShape> Shapes) PersonMapReadings(
        JsonElement readings, Func<DateTime, long> ms, IReadOnlyList<TimelineFile> files)
    {
        var figures = new List<TimelineFigure>();
        var shapes = new List<TimelineShape>();
        if (readings.ValueKind != JsonValueKind.Array) return (figures, shapes);

        var byPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) byPath.TryAdd(Normalise(file.RelativePath), file.FileId);
        Guid? Id(string? path) => path is { Length: > 0 } && byPath.TryGetValue(Normalise(path), out var id) ? id : null;

        foreach (var element in readings.EnumerateArray())
        {
            if (Moment(element, "at") is not { } at
                || !element.TryGetProperty("measurements", out var m) || m.ValueKind != JsonValueKind.Object)
                continue;
            var t = ms(at);
            var note = element.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

            if (m.TryGetProperty("figure", out var figure) && figure.ValueKind == JsonValueKind.Object
                && Text(figure, "value") is { Length: > 0 } joints)
            {
                var video = Text(figure, "video");
                var picture = Text(figure, "picture");
                figures.Add(new TimelineFigure(t, joints, Id(joints), video, Id(video),
                    Number(figure, "from"), Number(figure, "to"), Number(figure, "peak_confidence"),
                    picture, Id(picture), note));
            }
            if (m.TryGetProperty("shape", out var shape) && shape.ValueKind == JsonValueKind.Object
                && Text(shape, "value") is { Length: > 0 } words)
            {
                var video = Text(shape, "video");
                var picture = Text(shape, "picture");
                shapes.Add(new TimelineShape(t, words, Triple(shape, "size_cm"), Number(shape, "distance_m"),
                    Triple(shape, "where"), video, Id(video), Number(shape, "from"), Number(shape, "to"),
                    picture, Id(picture), note));
            }
        }
        return (figures.OrderBy(f => f.T).ToList(), shapes.OrderBy(s => s.T).ToList());
    }

    private static double[]? Triple(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3
           && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number)
            ? v.EnumerateArray().Select(x => x.GetDouble()).ToArray()
            : null;

    /// <summary>
    /// The room scans and comparisons a document records, and what the comparisons found (LiDAR
    /// plan, spec 1.2): a <c>room_scan</c> or <c>scan</c> measurement is a scan, a
    /// <c>scan_change</c> is a change. Each is a measurement whose value says the room, the file or
    /// what happened, with its other facts beside it.
    /// </summary>
    private static (List<TimelineRoomScan> Scans, List<TimelineScanChange> Changes) ScanReadings(
        JsonElement readings, Func<DateTime, long> ms)
    {
        var scans = new List<TimelineRoomScan>();
        var changes = new List<TimelineScanChange>();
        if (readings.ValueKind != JsonValueKind.Array) return (scans, changes);

        foreach (var element in readings.EnumerateArray())
        {
            if (Moment(element, "at") is not { } at
                || !element.TryGetProperty("measurements", out var m) || m.ValueKind != JsonValueKind.Object)
                continue;
            var t = ms(at);

            if (m.TryGetProperty("room_scan", out var scanned) && scanned.ValueKind == JsonValueKind.Object
                && Text(scanned, "value") is { Length: > 0 } room)
            {
                scans.Add(new TimelineRoomScan(t, "scanned", room, Id(scanned, "place_scan_id"),
                    (int?)Number(scanned, "version"), null, null, Text(scanned, "outcome"), Number(scanned, "duration_seconds")));
            }
            if (m.TryGetProperty("scan", out var compared) && compared.ValueKind == JsonValueKind.Object
                && Text(compared, "value") is { Length: > 0 } path)
            {
                scans.Add(new TimelineRoomScan(t, "compared", Text(compared, "room") ?? "", Id(compared, "place_scan_id"),
                    (int?)Number(compared, "place_scan_version"), path, (int?)Number(compared, "changes"), null, null));
            }
            if (m.TryGetProperty("scan_change", out var change) && change.ValueKind == JsonValueKind.Object
                && Text(change, "value") is "moved" or "missing" or "new")
            {
                var note = element.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                changes.Add(new TimelineScanChange(t, Text(change, "value")!, Text(change, "object") ?? "unclassified", note,
                    Number(change, "distance_cm"), Number(change, "rotation_deg"),
                    Triple(change, "size_cm"), Triple(change, "where"), Text(change, "picture"), Id(change, "place_scan_id")));
            }
        }
        return (scans, changes);

        static Guid? Id(JsonElement e, string name) => Guid.TryParse(Text(e, name), out var id) ? id : null;
    }

    /// <summary>A zone the site knows, by its IANA name, or null: the page never guesses at one it can't convert to.</summary>
    private static string? KnownZone(string? id) => Ben.Data.Common.Helpers.Zones.Normalize(id);

    // ── One reading ───────────────────────────────────────────────────────────

    private sealed record Row(
        DateTime At, double? Emf, double? EmfBase, double? Sound,
        double? Lat, double? Lon, double? Accuracy, double? Heading, double? Course,
        string? Room, string? Marker, string? Note,
        string? AudioFile, double? AudioOffset, double? AudioDuration);

    private static Row? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || Moment(element, "at") is not { } at) return null;

        double? emf = null, emfBase = null, sound = null;
        string? room = null, marker = null;
        if (element.TryGetProperty("measurements", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            if (m.TryGetProperty("emf", out var e)) { emf = Number(e, "value"); emfBase = Number(e, "baseline"); }
            if (m.TryGetProperty("sound_level", out var s)) sound = Number(s, "value");
            if (m.TryGetProperty("room", out var r)) room = Text(r, "value");
            if (m.TryGetProperty("marker", out var k)) marker = Text(k, "value");
        }

        double? latitude = null, longitude = null, accuracy = null;
        if (element.TryGetProperty("position", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            latitude = Number(p, "latitude");
            longitude = Number(p, "longitude");
            accuracy = Number(p, "accuracy_meters");
        }

        double? heading = null, course = null;
        if (element.TryGetProperty("motion", out var motion) && motion.ValueKind == JsonValueKind.Object)
        {
            heading = Number(motion, "heading_degrees");
            course = Number(motion, "course_degrees");
        }

        string? audioFile = null;
        double? audioOffset = null, audioDuration = null;
        if (element.TryGetProperty("audio_ref", out var a))
        {
            if (a.ValueKind == JsonValueKind.String) audioFile = a.GetString();
            else if (a.ValueKind == JsonValueKind.Object)
            {
                audioFile = Text(a, "filename");
                audioOffset = Number(a, "start_offset_seconds");
                audioDuration = Number(a, "duration_seconds");
            }
        }

        var note = element.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        return new Row(at, emf, emfBase, sound, latitude, longitude, accuracy, heading, course,
                       room, marker, note, audioFile, audioOffset, audioDuration);
    }

    // ── Pieces ────────────────────────────────────────────────────────────────

    /// <summary>What to call a marker on screen. A label this server does not know is shown as itself.</summary>
    public static string Title(string kind) => kind switch
    {
        "sentry_emf" => "Magnetic spike",
        "sentry_sound" => "Sound",
        "device_moved" => "Device moved",
        "scene_motion" => "Motion detected",
        "app_backgrounded" => "App put away",
        "app_returned" => "Back in the app",
        "evp_question" => "Question asked",
        "evp_wait_end" => "Stopped waiting",
        "manual_marker" => "Marked",
        // LiDAR plan: a room scanned for the place, what a comparison found, and the person map.
        "room_scan" => "Room scanned",
        "room_compared" => "Room compared",
        "scan_change" => "Something changed",
        "figure_seen" => "Figure seen",
        "unexplained_shape" => "Unexplained shape",
        _ => kind,
    };

    /// <summary>Pairs each "put away" with the "back" after it; one never closed runs to the end.</summary>
    private static List<TimelineAway> AwayStretches(List<TimelineMarker> markers, long endMs)
    {
        var stretches = new List<TimelineAway>();
        TimelineMarker? open = null;
        foreach (var marker in markers)
        {
            if (marker.Kind == "app_backgrounded" && open is null) open = marker;
            else if (marker.Kind == "app_returned" && open is not null)
            {
                stretches.Add(new TimelineAway(open.T, marker.T, open.Note));
                open = null;
            }
        }
        if (open is not null) stretches.Add(new TimelineAway(open.T, endMs, open.Note));
        return stretches;
    }

    /// <summary>
    /// Thins [ms, value] points to <see cref="TracePoints"/>, keeping each bucket's low and high —
    /// in the order they happened, so the line never doubles back on itself. Averaging would
    /// flatten the one spike somebody opened the page to look at.
    /// </summary>
    internal static IReadOnlyList<double[]> Thin(List<double[]> points, int most = TracePoints)
    {
        if (points.Count <= most) return points;
        var bucket = (int)Math.Ceiling(points.Count / (double)(most / 2));
        var kept = new List<double[]>(most + 2);
        for (var from = 0; from < points.Count; from += bucket)
        {
            var to = Math.Min(from + bucket, points.Count);
            int low = from, high = from;
            for (var i = from + 1; i < to; i++)
            {
                if (points[i][1] < points[low][1]) low = i;
                if (points[i][1] > points[high][1]) high = i;
            }
            if (low == high) kept.Add(points[low]);
            else
            {
                kept.Add(points[Math.Min(low, high)]);
                kept.Add(points[Math.Max(low, high)]);
            }
        }
        return kept;
    }

    /// <summary>The fixes as a drawable path: near-duplicates dropped, then evenly thinned to a cap.</summary>
    private static IReadOnlyList<double[]> Track(double?[] lat, double?[] lon)
    {
        var path = new List<double[]>();
        for (var i = 0; i < lat.Length; i++)
        {
            if (lat[i] is not { } la || lon[i] is not { } lo) continue;
            if (path.Count > 0 && Metres(path[^1][0], path[^1][1], la, lo) < TrackSpacingMeters) continue;
            path.Add([la, lo]);
        }
        if (path.Count <= TrackPoints) return path;
        var step = path.Count / (double)TrackPoints;
        var thinned = new List<double[]>(TrackPoints + 1);
        for (var k = 0; k < TrackPoints; k++) thinned.Add(path[(int)(k * step)]);
        if (!ReferenceEquals(thinned[^1], path[^1])) thinned.Add(path[^1]);
        return thinned;
    }

    internal static double Metres(double lat1, double lon1, double lat2, double lon2)
    {
        const double earth = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180 * Math.Cos((lat1 + lat2) / 2 * Math.PI / 180);
        return earth * Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    private static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool StartsWith(string note, string prefix, out string rest)
    {
        if (note.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = note[prefix.Length..].Trim();
            return rest.Length > 0;
        }
        rest = "";
        return false;
    }

    internal static bool IsAudio(string path) =>
        path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

    internal static bool IsVideo(string path) =>
        path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    internal static bool IsImage(string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".heic", StringComparison.OrdinalIgnoreCase);

    private static double? Round(double? value, int digits) => value is { } v ? Math.Round(v, digits) : null;

    private static double? Number(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out var d) && double.IsFinite(d)
            ? d : null;

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static DateTime? Moment(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
           && value.TryGetDateTime(out var moment)
            ? moment.ToUniversalTime() : null;
}
