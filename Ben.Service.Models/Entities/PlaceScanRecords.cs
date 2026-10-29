using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

// ── Room scans kept by a place (LiDAR plan, phase 1) ─────────────────────────────────────────
// The phone's contract. Ben.iOS/BenKit/Tests/BenKitTests/Fixtures/place-scans/ holds these as the
// API serializes them, written by PlaceScanFixtureTests from the real controller; change a record
// and that test says which fixture moved.

/// <summary>
/// The rooms at a place and what the caller may do in each — the phone's room picker.
/// </summary>
/// <param name="MayScan">Whether the caller may scan or compare here at all.</param>
/// <param name="WhyCannotScan">The sentence when they may not.</param>
/// <param name="Custodian">The group that looks after every room's scans here, when one does.</param>
/// <param name="StorageLeftBytes">
/// What the caller's own account can still store, or null when nothing caps it — a paid group, or
/// a verified venue paying for the place's scans.
/// </param>
/// <param name="Rooms">Scanned rooms, rooms somebody is scanning, and the venue's own rooms not yet scanned.</param>
public sealed record PlaceScanRoomList(
    Guid PlaceId, string? PlaceName, PlaceKind Kind,
    bool MayScan, string? WhyCannotScan,
    PlaceScanCustodianRecord? Custodian,
    long? StorageLeftBytes,
    IReadOnlyList<PlaceScanRoomRecord> Rooms);

/// <summary>One room at a place.</summary>
/// <param name="Current">The room's scan, or null when it has none.</param>
/// <param name="CanScan">The room has no scan and nobody else is scanning it.</param>
/// <param name="CanCompare">The room has a scan to compare against.</param>
/// <param name="CanReplace">The caller may replace the current scan outright.</param>
/// <param name="ReplaceReason">When they may not, what happens to a new scan instead.</param>
/// <param name="HoldBy">Somebody scanning the room right now — the caller included.</param>
/// <param name="OffersWaiting">Offers for the caller to decide, when they are the custodian.</param>
/// <param name="PlaceRoomId">The verified venue's room of the same name, when it has one.</param>
public sealed record PlaceScanRoomRecord(
    string RoomKey, string RoomName,
    PlaceScanSummaryRecord? Current,
    bool CanScan, bool CanCompare, bool CanReplace, string? ReplaceReason,
    PlaceScanHoldRecord? HoldBy,
    int OffersWaiting,
    Guid? PlaceRoomId);

/// <summary>One version of a room's scan, as a list shows it.</summary>
public sealed record PlaceScanSummaryRecord(
    Guid Id, int Version, PlaceScanState State, PlaceScanRetention Retention,
    DateTime ScannedUtc, string ScannedBy, bool ScannedByYou,
    long FileSize, int KeyframeCount, double? FloorAreaM2, int TriangleCount);

/// <summary>Somebody scanning a room right now.</summary>
/// <param name="HoldId">Only on the caller's own hold — the id they release it by.</param>
public sealed record PlaceScanHoldRecord(
    Guid? HoldId, string RoomKey, string RoomName, string HeldBy, bool IsYou, DateTime Until);

/// <summary>Who looks after a place's scans.</summary>
/// <param name="Kind"><c>venue</c>, <c>grant</c>, <c>scanner</c> or <c>group</c>.</param>
public sealed record PlaceScanCustodianRecord(string Kind, string Name, bool IsYou);

/// <summary>One version of a room's scan in full, with what the file carries.</summary>
public sealed record PlaceScanRecord(
    Guid Id, Guid PlaceId, string RoomKey, string RoomName, int Version,
    PlaceScanState State, PlaceScanRetention Retention,
    DateTime ScannedUtc, string ScannedBy, bool ScannedByYou,
    long FileSize, string Sha256, string? DeviceModel, int ScannerVersion,
    int KeyframeCount, double? FloorAreaM2, int TriangleCount,
    bool ShownOnPlacePage, DateTime? DecidedUtc, string? DecisionNote,
    IReadOnlyList<PlaceScanEntryRecord> Entries);

/// <summary>One member of a <c>.scan</c> file, servable by its path.</summary>
public sealed record PlaceScanEntryRecord(string Path, long Length, string ContentType);

/// <summary>What an upload became.</summary>
/// <param name="Outcome"><c>current</c>, <c>replaced</c> or <c>offered</c>.</param>
/// <param name="Says">The sentence to show.</param>
public sealed record PlaceScanUploadResult(string Outcome, string Says, PlaceScanRecord Scan);

/// <summary>
/// The 409 when a room already has a scan the caller may not replace — or one landed while they
/// were scanning. The phone keeps its file and offers <see cref="Choices"/>.
/// </summary>
/// <param name="Reason"><c>already scanned</c>.</param>
/// <param name="Choices"><c>compare</c>, <c>offer</c>, <c>discard</c>, and <c>replace</c> when allowed.</param>
public sealed record PlaceScanConflictRecord(
    string Reason, string Says, string RoomName, PlaceScanSummaryRecord Current,
    bool MayReplace, string? ReplaceReason, IReadOnlyList<string> Choices);

/// <summary>The 409 when somebody else is scanning the room.</summary>
/// <param name="Reason"><c>held</c>.</param>
public sealed record PlaceScanHeldRecord(string Reason, string Says, string HeldBy, DateTime Until);

/// <summary>Asking to scan a room.</summary>
public sealed record TakeScanHoldRequest(string? Room);

/// <summary>Accepting or declining an offered scan; the note reaches the scanner.</summary>
public sealed record DecideScanOfferRequest(string? Note);

/// <summary>A SuperAdmin handing a place's scans to a group.</summary>
public sealed record GrantScanCustodyRequest(Guid OrganizationId, string? Note);

/// <summary>Who holds a place's scans by an administrator's grant.</summary>
public sealed record PlaceScanCustodyRecord(
    Guid PlaceId, Guid OrganizationId, string OrganizationName,
    DateTime GrantedUtc, string? Note, DateTime? ClosedUtc, string? ClosedReason);
