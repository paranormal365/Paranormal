namespace Ben.Service.Models.FieldSessions;

/// <summary>
/// Everything a player needs to replay one field session, worked out once on the server.
/// </summary>
/// <remarks>
/// <para><b>Why one shape, built once.</b> The player used to receive the phone's whole document
/// and parse it again on every page open — and each viewer's copy lived on the website for as long
/// as they watched. The rules for reading that document (where a clip began, which base was in
/// force, what counts as a marker) are written down in <c>ProjectNotes/specs/replay-semantics.md</c>
/// and applied here, in one place, so the website's player and any other reader agree with the
/// phone by construction rather than by re-implementing it.</para>
///
/// <para><b>Times are whole milliseconds from the session's start</b> (<see cref="StartUtc"/>), so
/// the browser does integer arithmetic on its own clock and never parses a date.</para>
///
/// <para><b>Readings are columns, not rows.</b> One array per channel, all the same length and in
/// time order. A null is "this reading did not carry it". Columns are smaller to send than a list of
/// objects and are what a binary search over time wants anyway.</para>
///
/// <para><b>Privacy is applied BEFORE this is built</b> — the same redaction sweeps the share link
/// and the public archive already use — so a timeline built for a share link with positions
/// withheld has no coordinates in it to leak. <see cref="Positions"/> says which rule applied, so
/// the player can say why there is no track rather than looking broken.</para>
/// </remarks>
public sealed record SessionTimeline(
    int Version,
    DateTime StartUtc,
    long DurationMs,
    /// <summary>True when the phone wrote an end time; false for an interrupted session, whose
    /// end is the last thing it recorded and may grow once the browser reads a recording's
    /// length.</summary>
    bool HasEndTime,
    TimelinePositionPolicy Positions,
    TimelineReadings Readings,
    IReadOnlyList<string> Rooms,
    IReadOnlyList<TimelineMarker> Markers,
    IReadOnlyList<TimelinePhoto> Photos,
    IReadOnlyList<TimelineClip> Clips,
    IReadOnlyList<TimelineAway> Away,
    TimelineTrace Trace,
    /// <summary>The path walked, thinned for drawing, as [latitude, longitude] pairs. Empty unless
    /// <see cref="Positions"/> is <see cref="TimelinePositionPolicy.Exact"/>.</summary>
    IReadOnlyList<double[]> Track,
    /// <summary>Readings the document carried that could not be placed in time (no readable
    /// <c>at</c>, or not an object). Counted, never fatal.</summary>
    int SkippedReadings,
    /// <summary>The base that stands in before the first reading to carry one (rule I3): the
    /// session's last base, as the phone has always used for the whole night.</summary>
    double? FallbackEmfBase,
    /// <summary>Rooms scanned for the place, and rooms compared with its scans (LiDAR plan, spec
    /// 1.2). Empty for a session with none.</summary>
    IReadOnlyList<TimelineRoomScan>? RoomScans = null,
    /// <summary>What those comparisons found, one per thing (LiDAR plan, spec 1.2).</summary>
    IReadOnlyList<TimelineScanChange>? ScanChanges = null)
{
    public const int CurrentVersion = 1;
}

/// <summary>Which coordinates this timeline carries.</summary>
public enum TimelinePositionPolicy
{
    /// <summary>Where the phone said, to the metre — the owner, the group, an open investigation.</summary>
    Exact = 0,
    /// <summary>None at all: a share link made without positions.</summary>
    Withheld = 1,
    /// <summary>Every position is the place's approximate public point: the public archive.</summary>
    PublicPoint = 2,
}

/// <summary>One column per channel; every array has one entry per reading.</summary>
public sealed record TimelineReadings(
    long[] T,
    /// <summary>Magnetic field, µT, as recorded.</summary>
    double?[] Emf,
    /// <summary>The magnetic base in force at this reading, µT — the last one written at or before
    /// it (rule I3). Null until a base is set.</summary>
    double?[] EmfBase,
    /// <summary>Sound level, dBFS.</summary>
    double?[] Sound,
    double?[] Lat,
    double?[] Lon,
    double?[] Accuracy,
    double?[] Heading,
    double?[] Course,
    /// <summary>Index into <see cref="SessionTimeline.Rooms"/>, or null when no room was said.</summary>
    int?[] Room);

/// <summary>Something marked during the session — by a person or by the phone.</summary>
public sealed record TimelineMarker(
    long T,
    /// <summary>The label the phone wrote, e.g. <c>sentry_emf</c>.</summary>
    string Kind,
    /// <summary>What to call it on screen: "Magnetic spike", or the label itself for a kind this
    /// server does not know yet.</summary>
    string Title,
    /// <summary>Raised by the phone on its own (a sentry, motion), rather than by a person.</summary>
    bool Automatic,
    string? Note,
    string? Room,
    /// <summary>The field at the mark, mG from the base in force, or absolute when none.</summary>
    double? FieldMilligauss,
    /// <summary>What the investigator added about this mark after the session, before sending it
    /// (the package's <c>mark_details</c>, 10/15/2026). Shown as written; the website never edits it.</summary>
    string? Details = null,
    /// <summary>When those details were written.</summary>
    DateTime? DetailsAddedAt = null);

/// <summary>A room scanned for its place, or compared with the place's scan of it (LiDAR plan).</summary>
/// <param name="Kind"><c>scanned</c> or <c>compared</c>.</param>
/// <param name="Path">For a comparison, the comparison scan inside the session's bundle.</param>
/// <param name="Outcome">For a scan: <c>current</c>, <c>replaced</c> or <c>offered</c>.</param>
public sealed record TimelineRoomScan(
    long T, string Kind, string Room, Guid? PlaceScanId, int? Version,
    string? Path, int? Changes, string? Outcome, double? DurationSeconds);

/// <summary>One thing a comparison found: moved, missing or new (LiDAR plan).</summary>
/// <param name="Picture">Inside the session's bundle: <c>scans/compare-001.scan#changes/01.jpg</c>.</param>
public sealed record TimelineScanChange(
    long T, string What, string Object, string? Note,
    double? DistanceCm, double? RotationDeg, double[]? SizeCm, double[]? Where,
    string? Picture, Guid? PlaceScanId);

/// <summary>A photograph and the moment it was taken.</summary>
public sealed record TimelinePhoto(long T, Guid FileId, string Path);

/// <summary>A stretch of sound or video.</summary>
public sealed record TimelineClip(
    Guid FileId,
    string Path,
    /// <summary><c>audio</c> or <c>video</c>.</summary>
    string Kind,
    /// <summary>When it began, ms from the session's start — null until its length is known, for
    /// a clip noted only when it ended (rule M4). The browser fills it in from the file.</summary>
    long? StartMs,
    /// <summary>How long it runs, when known.</summary>
    long? DurationMs,
    /// <summary>When it was noted as finished, for a clip whose start waits on its length.</summary>
    long? NotedEndMs);

/// <summary>A stretch the app was put away, from the phone's own two marks.</summary>
public sealed record TimelineAway(long FromMs, long ToMs, string? Note);

/// <summary>The readings thinned for drawing, extremes kept, in time order.</summary>
public sealed record TimelineTrace(
    /// <summary>[ms, mG] pairs — from base where one was in force, absolute where none.</summary>
    IReadOnlyList<double[]> Magnetic,
    /// <summary>[ms, dBFS] pairs.</summary>
    IReadOnlyList<double[]> Sound,
    /// <summary>Whether any reading had a magnetic base in force: the axis says "from base" or "abs".</summary>
    bool MagneticFromBase);
