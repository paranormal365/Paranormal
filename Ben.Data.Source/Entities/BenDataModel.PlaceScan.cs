using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One version of a room's LiDAR scan, kept by the place (LiDAR plan, phase 1).
    /// </summary>
    /// <remarks>
    /// <para><b>Scans belong to the place, not to whoever made them.</b> Ben, 10/28/2026: scans are
    /// "per location, not per person", "so there are not multiple people uploading the same rooms".
    /// So a room has one <see cref="PlaceScanState.Current"/> scan, keyed on the place and the room's
    /// normalized name, and everybody else who walks in compares against it rather than scanning it
    /// again. A filtered unique index makes "one current scan per room" the database's rule.</para>
    ///
    /// <para><b>Keyed on a name, not on a group's room.</b> <see cref="PlaceRoom"/> rows belong to
    /// the group that named them, so two groups at one building keep two lists. A scan may point at
    /// one with the same name (<see cref="PlaceRoomId"/>), but nothing depends on it.</para>
    ///
    /// <para><b>Replaced versions stay.</b> A comparison made last March points at the version it was
    /// made against, so replacing a scan moves the old one to <see cref="PlaceScanState.Previous"/>
    /// rather than deleting it.</para>
    /// </remarks>
    public class PlaceScan : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid PlaceId { get; set; }

        /// <summary>
        /// The room's name normalized — trimmed, lower-cased, inner whitespace collapsed — so
        /// "bedroom 1" and " Bedroom  1" are one room. The phone normalizes the same way.
        /// </summary>
        public string RoomKey { get; set; } = null!;

        /// <summary>The name as whoever scanned the room first typed it.</summary>
        public string RoomName { get; set; } = null!;

        /// <summary>A group's room at this place with the same name, when there is one. No key.</summary>
        public Guid? PlaceRoomId { get; set; }

        /// <summary>1, 2, … per place and room, in the order the versions arrived.</summary>
        public int Version { get; set; }

        public PlaceScanState State { get; set; }
        public PlaceScanRetention Retention { get; set; }

        /// <summary>When the phone finished scanning — the scan's age is counted from here.</summary>
        public DateTime ScannedUtc { get; set; }

        public Guid ScannedByAppUserId { get; set; }

        /// <summary>The group the scanner was acting for, when there was one.</summary>
        public Guid? ScannedForOrganizationId { get; set; }

        /// <summary>
        /// Whose free-account allowance the file counts against. Null when the verified venue's group
        /// pays (<see cref="OwnerOrganizationId"/>).
        /// </summary>
        public Guid? OwnerAppUserId { get; set; }

        /// <summary>The verified venue's group at upload time, which then pays for the file.</summary>
        public Guid? OwnerOrganizationId { get; set; }

        /// <summary>Where the <c>.scan</c> file lives: <c>places/{placeId}/scans/{scanId}/room.scan</c>.</summary>
        public string StoragePath { get; set; } = null!;

        public long FileSize { get; set; }

        /// <summary>Lower-case hex SHA-256 of the file as it was stored.</summary>
        public string Sha256 { get; set; } = null!;

        /// <summary>
        /// The custodian's say-so to show the walkthrough on the public place page (phase 3). Off
        /// until somebody turns it on.
        /// </summary>
        public bool ShownOnPlacePage { get; set; }

        /// <summary>The phone's model identifier, e.g. <c>iPhone16,1</c>.</summary>
        public string? DeviceModel { get; set; }

        /// <summary>The phone's capture pipeline version, pinned like the EVP detector's.</summary>
        public int ScannerVersion { get; set; }

        /// <summary>Floor area from the room model, when the phone could measure it.</summary>
        public double? FloorAreaM2 { get; set; }

        public int TriangleCount { get; set; }
        public int KeyframeCount { get; set; }

        /// <summary>When an offer was accepted or declined, and by whom.</summary>
        public DateTime? DecidedUtc { get; set; }
        public Guid? DecidedByAppUserId { get; set; }

        /// <summary>What the custodian said when deciding an offer — the scanner reads it.</summary>
        public string? DecisionNote { get; set; }

        /// <summary>
        /// When the custodian was told this scan turned a year old. Set once, so the note is sent once.
        /// </summary>
        public DateTime? YearNoticeSentUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Place Place { get; set; } = null!;
        public virtual AppUser ScannedByAppUser { get; set; } = null!;
        public virtual AppUser? DecidedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }

    /// <summary>
    /// Somebody scanning a room right now (LiDAR plan, phase 1).
    /// </summary>
    /// <remarks>
    /// <para>Taken when somebody taps <b>Scan</b>, for twenty minutes, so a second person at the same
    /// building is told "Dana is scanning Bedroom 1 right now" instead of spending five minutes
    /// scanning it too. A courtesy, not a lock: the upload decides what a scan becomes whatever the
    /// holds say.</para>
    ///
    /// <para>"Unique while unexpired" cannot be an index, so the controller checks and expired rows
    /// are ignored until the sweep removes them.</para>
    /// </remarks>
    public class PlaceScanHold
    {
        public Guid Id { get; set; }
        public Guid PlaceId { get; set; }
        public string RoomKey { get; set; } = null!;
        public string RoomName { get; set; } = null!;
        public Guid AppUserId { get; set; }
        public DateTime TakenUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }

        public virtual Place Place { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
    }

    /// <summary>
    /// One thing a comparison scan found, so a place can say "3 things moved since March" (LiDAR
    /// plan, phase 2).
    /// </summary>
    /// <remarks>
    /// <para>Written from the session document when a session is uploaded — each
    /// <c>scan_change</c> reading becomes a row — and replaced with the document when the session is
    /// sent again. The comparison itself stays in the session; the row is what the place keeps.</para>
    ///
    /// <para>Keyed to the version of the room's scan it was made against, which outlives being
    /// replaced or taken down, so the row still says what it was compared with.</para>
    /// </remarks>
    public class PlaceScanChange
    {
        public Guid Id { get; set; }

        /// <summary>The version of the room's scan the comparison was made against.</summary>
        public Guid PlaceScanId { get; set; }

        public Guid FieldSessionUploadId { get; set; }

        public PlaceScanChangeKind Kind { get; set; }

        /// <summary>RoomPlan's category (<c>chair</c>, <c>table</c>…) or <c>unclassified</c>.</summary>
        public string ObjectCategory { get; set; } = null!;

        /// <summary>For a move: how far, and how far it turned.</summary>
        public double? DistanceCm { get; set; }
        public double? RotationDeg { get; set; }

        /// <summary>For something new or missing: its size.</summary>
        public double? SizeXCm { get; set; }
        public double? SizeYCm { get; set; }
        public double? SizeZCm { get; set; }

        /// <summary>Where, in metres, in the room scan's frame.</summary>
        public double WhereX { get; set; }
        public double WhereY { get; set; }
        public double WhereZ { get; set; }

        /// <summary>The picture of it inside the session bundle: <c>scans/compare-001.scan#changes/01.jpg</c>.</summary>
        public string? PictureEntryPath { get; set; }

        public DateTime ComparedUtc { get; set; }

        public virtual PlaceScan PlaceScan { get; set; } = null!;
        public virtual FieldSessionUpload FieldSessionUpload { get; set; } = null!;
    }

    /// <summary>
    /// A SuperAdmin handing a place's scans to a group (LiDAR plan, phase 1; Ben, 10/28/2026).
    /// </summary>
    /// <remarks>
    /// <para>Read after the verified venue and before the first-scanner fallback, so a confirmed
    /// venue always speaks for its own building: a venue claim coming through later takes over and
    /// closes the grant (<see cref="ClosedReason"/> <c>venue_verified</c>).</para>
    ///
    /// <para>One row per place, unique on <see cref="PlaceId"/>: a closed grant stays as the record
    /// of the last holder until a new grant rewrites it, and the audit log keeps every change.</para>
    /// </remarks>
    public class PlaceScanCustody
    {
        public Guid Id { get; set; }
        public Guid PlaceId { get; set; }
        public Guid OrganizationId { get; set; }
        public DateTime GrantedUtc { get; set; }
        public Guid GrantedByAppUserId { get; set; }

        /// <summary>Why the administrator gave it — shown to whoever asks who holds the scans.</summary>
        public string? Note { get; set; }

        public DateTime? ClosedUtc { get; set; }

        /// <summary><c>venue_verified</c> or <c>admin</c>.</summary>
        public string? ClosedReason { get; set; }

        public virtual Place Place { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser GrantedByAppUser { get; set; } = null!;

        /// <summary>Closed because a verified venue now holds custody.</summary>
        public const string ClosedByVenue = "venue_verified";

        /// <summary>Closed by a SuperAdmin.</summary>
        public const string ClosedByAdmin = "admin";
    }
}
