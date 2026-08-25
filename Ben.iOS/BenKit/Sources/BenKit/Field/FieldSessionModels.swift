import Foundation
import SwiftData

/// What a marker means. The enum the person sees; the wire carries it as a `marker`
/// measurements label, since the spec's `triggered_by` is a closed enum of three values.
public enum MarkerKind: String, Codable, Sendable, CaseIterable {
    case manual = "manual_marker"
    case sentryEmf = "sentry_emf"
    case sentrySound = "sentry_sound"
    case evpQuestion = "evp_question"
    case evpWaitEnd = "evp_wait_end"
    /// The device itself was moved — a bump, a knock, somebody picking it up.
    case deviceMoved = "device_moved"
    /// Something in the camera's view moved.
    case sceneMotion = "scene_motion"

    /// Which of the spec's three legal `triggered_by` values this kind reports as.
    public var trigger: FieldReading.Trigger {
        switch self {
        case .sentryEmf, .sentrySound, .deviceMoved, .sceneMotion: .event
        case .manual, .evpQuestion, .evpWaitEnd: .manual
        }
    }

    public var isAutomatic: Bool {
        switch self {
        case .sentryEmf, .sentrySound, .deviceMoved, .sceneMotion: true
        case .manual, .evpQuestion, .evpWaitEnd: false
        }
    }

    public var title: String {
        switch self {
        case .manual: "Marked"
        case .sentryEmf: "Magnetic spike"
        case .sentrySound: "Sound"
        case .evpQuestion: "Question asked"
        case .evpWaitEnd: "Stopped waiting"
        case .deviceMoved: "Device moved"
        case .sceneMotion: "Movement seen"
        }
    }
}

public enum CaptureKind: String, Codable, Sendable {
    case photo, video, audio

    public var mediaTypePrefix: String { self == .photo ? "image/" : "\(rawValue)/" }
}

/// Why a session stopped. `interrupted` is not a failure to hide — a session that ended because
/// the phone died is a fact a reviewer needs, and its `ended_at` is genuinely unknown.
public enum FieldSessionOutcome: String, Codable, Sendable {
    case recording, ended, interrupted
}

/// One field session. SwiftData holds the low-volume, editable, relational rows; the readings
/// themselves stream to an append-only log beside them, because a five-hour session is tens of
/// thousands of readings and inserting those one at a time through a MainActor context would
/// make the live screen unusable.
@Model
public final class FieldSession {
    @Attribute(.unique) public var id: UUID

    public var startedAt: Date
    public var endedAt: Date?
    public var outcomeRaw: String

    /// The operator's own words for where this is — "back bedroom, north wall".
    public var locationLabel: String?

    /// Set when the session was started against one of the user's investigations. Nil is
    /// ordinary: a tour guide or somebody scouting a building records without one.
    public var investigationId: UUID?
    public var investigationTitle: String?

    /// Denormalised so the sessions list never has to open a log file to draw a row.
    public var readingCount: Int
    public var markerCount: Int
    public var captureCount: Int

    /// Baselines as armed, in the units the wire uses.
    public var baselineEmfMicrotesla: Double?
    public var baselineSoundDbfs: Double?

    public var batteryPercentAtStart: Double?
    public var deviceModel: String

    /// Set once the session's document has reached the server. The device keeps everything
    /// regardless — this says what is safe to delete, never what has been deleted.
    public var serverSessionId: UUID?
    public var uploadedAt: Date?
    public var timezoneIdentifier: String

    @Relationship(deleteRule: .cascade, inverse: \FieldMarker.session)
    public var markers: [FieldMarker]
    @Relationship(deleteRule: .cascade, inverse: \FieldCapture.session)
    public var captures: [FieldCapture]

    public init(id: UUID = UUID(),
                startedAt: Date,
                locationLabel: String? = nil,
                investigationId: UUID? = nil,
                investigationTitle: String? = nil,
                batteryPercentAtStart: Double? = nil,
                deviceModel: String,
                timezoneIdentifier: String = TimeZone.current.identifier) {
        self.id = id
        self.startedAt = startedAt
        self.outcomeRaw = FieldSessionOutcome.recording.rawValue
        self.locationLabel = locationLabel
        self.investigationId = investigationId
        self.investigationTitle = investigationTitle
        self.readingCount = 0
        self.markerCount = 0
        self.captureCount = 0
        self.batteryPercentAtStart = batteryPercentAtStart
        self.deviceModel = deviceModel
        self.timezoneIdentifier = timezoneIdentifier
        self.markers = []
        self.captures = []
    }

    public var outcome: FieldSessionOutcome {
        get { FieldSessionOutcome(rawValue: outcomeRaw) ?? .interrupted }
        set { outcomeRaw = newValue.rawValue }
    }

    /// How long it ran. An interrupted session has no honest end, so it reports what was
    /// actually observed rather than pretending it stopped when the app happened to relaunch.
    public var duration: TimeInterval? {
        guard let endedAt else { return nil }
        return endedAt.timeIntervalSince(startedAt)
    }
}

@Model
public final class FieldMarker {
    @Attribute(.unique) public var id: UUID
    public var at: Date
    public var kindRaw: String
    /// Editable in review — a marker dropped in the dark gets its explanation later.
    public var note: String?

    /// Set when a recording was running: which file, and how far into it.
    public var audioFilename: String?
    public var audioOffsetSeconds: Double?

    /// What the instruments read at that moment, kept for the review list so the timeline does
    /// not have to seek the log.
    public var emfMicrotesla: Double?
    public var soundDbfs: Double?

    public var latitude: Double?
    public var longitude: Double?
    /// The room this was marked in.
    public var room: String?

    public var session: FieldSession?

    public init(id: UUID = UUID(), at: Date, kind: MarkerKind, note: String? = nil,
                audioFilename: String? = nil, audioOffsetSeconds: Double? = nil,
                emfMicrotesla: Double? = nil, soundDbfs: Double? = nil,
                latitude: Double? = nil, longitude: Double? = nil, room: String? = nil) {
        self.id = id
        self.at = at
        self.kindRaw = kind.rawValue
        self.note = note
        self.room = room
        self.audioFilename = audioFilename
        self.audioOffsetSeconds = audioOffsetSeconds
        self.emfMicrotesla = emfMicrotesla
        self.soundDbfs = soundDbfs
        self.latitude = latitude
        self.longitude = longitude
    }

    public var kind: MarkerKind {
        get { MarkerKind(rawValue: kindRaw) ?? .manual }
        set { kindRaw = newValue.rawValue }
    }
}

@Model
public final class FieldCapture {
    @Attribute(.unique) public var id: UUID
    public var at: Date
    public var kindRaw: String
    /// Relative to the session directory — `media/photo-001.jpg`. Never absolute: this is the
    /// path that ends up in an exported bundle, where absolute paths are a security boundary.
    public var relativePath: String
    public var byteCount: Int64
    public var durationSeconds: Double?

    public var latitude: Double?
    public var longitude: Double?
    public var headingDegrees: Double?

    /// When this file reached the server, if it has. Per FILE, because somebody picks three of
    /// twenty and the rest are still only on the phone.
    public var uploadedAt: Date?
    /// Why the last attempt failed, kept so a retry is an informed one rather than a guess.
    public var uploadProblem: String?

    /// The room the operator said they were in when this was captured. The only dependable
    /// answer to "where in the building was this taken" — a fix cannot tell rooms apart.
    public var room: String?

    /// Marked as the picture that represents the property — the one a case or an investigation
    /// would show. Optional by design: most captures are evidence, not a portrait, and nothing
    /// is chosen unless somebody chooses it.
    public var isRepresentative: Bool = false

    public var session: FieldSession?

    public init(id: UUID = UUID(), at: Date, kind: CaptureKind, relativePath: String,
                byteCount: Int64, durationSeconds: Double? = nil,
                latitude: Double? = nil, longitude: Double? = nil,
                headingDegrees: Double? = nil, room: String? = nil) {
        self.id = id
        self.at = at
        self.kindRaw = kind.rawValue
        self.relativePath = relativePath
        self.byteCount = byteCount
        self.durationSeconds = durationSeconds
        self.latitude = latitude
        self.longitude = longitude
        self.headingDegrees = headingDegrees
        self.room = room
    }

    public var kind: CaptureKind {
        get { CaptureKind(rawValue: kindRaw) ?? .photo }
        set { kindRaw = newValue.rawValue }
    }
}
