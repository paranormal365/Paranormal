import Foundation

/// A session as a list row sees it — a value type, so the UI never holds a live SwiftData object
/// across an await and nothing outside this file needs to know the model exists.
public struct FieldSessionSummary: Sendable, Identifiable, Equatable {
    public var id: UUID
    public var startedAt: Date
    public var endedAt: Date?
    public var outcome: FieldSessionOutcome
    public var locationLabel: String?
    public var investigationId: UUID?
    public var investigationTitle: String?
    public var readingCount: Int
    public var markerCount: Int
    public var captureCount: Int
    public var serverSessionId: UUID?
    public var uploadedAt: Date?

    public init(id: UUID, startedAt: Date, endedAt: Date?, outcome: FieldSessionOutcome,
                locationLabel: String?, investigationId: UUID?, investigationTitle: String?,
                readingCount: Int, markerCount: Int, captureCount: Int,
                serverSessionId: UUID? = nil, uploadedAt: Date? = nil) {
        self.id = id
        self.startedAt = startedAt
        self.endedAt = endedAt
        self.outcome = outcome
        self.locationLabel = locationLabel
        self.investigationId = investigationId
        self.investigationTitle = investigationTitle
        self.readingCount = readingCount
        self.markerCount = markerCount
        self.captureCount = captureCount
        self.serverSessionId = serverSessionId
        self.uploadedAt = uploadedAt
    }

    init(_ session: FieldSession) {
        self.init(id: session.id,
                  startedAt: session.startedAt,
                  endedAt: session.endedAt,
                  outcome: session.outcome,
                  locationLabel: session.locationLabel,
                  investigationId: session.investigationId,
                  investigationTitle: session.investigationTitle,
                  readingCount: session.readingCount,
                  markerCount: session.markerCount,
                  captureCount: session.captureCount,
                  serverSessionId: session.serverSessionId,
                  uploadedAt: session.uploadedAt)
    }

    /// Whether anybody has given this session a name of its own.
    ///
    /// iOS-4 of the 2026-09-06 evaluation: a session with no label printed its start time as the
    /// title AND as the first thing in the subtitle, so the list read the same timestamp twice on
    /// every untitled row. The row needs to know which it is looking at, not just what to print.
    public var isUntitled: Bool {
        (locationLabel?.isEmpty ?? true) && (investigationTitle?.isEmpty ?? true)
    }

    /// What to call it in a list. The operator's own label wins; failing that, where it sat in
    /// the calendar, because "Session 4" tells nobody anything.
    ///
    /// The unnamed case says it is unnamed rather than repeating the timestamp the row is about
    /// to print underneath (iOS-4). Nothing is lost: the date and time are in the subtitle, where
    /// they sit beside the length and the counts a reader is scanning for anyway.
    public var title: String {
        if let locationLabel, !locationLabel.isEmpty { return locationLabel }
        if let investigationTitle, !investigationTitle.isEmpty { return investigationTitle }
        return "Untitled session"
    }

    public var duration: TimeInterval? {
        endedAt.map { $0.timeIntervalSince(startedAt) }
    }

    public var isRecording: Bool { outcome == .recording }
    /// Open on the live screen with nothing logged yet — Start has not been pressed.
    public var isPending: Bool { outcome == .pending }
    /// Either of the two states the live screen resumes into.
    public var isOpen: Bool { isPending || isRecording }
    public var isUploaded: Bool { uploadedAt != nil }
}
