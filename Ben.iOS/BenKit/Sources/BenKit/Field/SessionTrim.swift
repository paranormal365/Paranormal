import Foundation

/// The stretch of a session worth sending (item 210).
///
/// **An hour usually matters for ten seconds.** A night's recording is mostly a building being
/// quiet, and all of it is uploaded, stored and paged through for the sake of the part that is
/// not. Choosing a window before the upload means the group stores what mattered, the reviewer
/// opens what mattered, and the report cites what mattered.
///
/// **Trimming happens here, on the phone, and never on the server.** Ben chose that on
/// 2026-09-04 over trimming after upload, and it is the better half of the trade by some
/// distance: the untrimmed session simply never leaves the device, so nothing on the server is
/// ever destroyed, there is no irreversible operation to warn about, and the sentence a
/// person is shown — *the full recording stays on this phone* — is a fact rather than a promise.
/// It also saves the upload, which over a home connection is the part that actually hurts.
public struct SessionWindow: Sendable, Equatable {
    public var start: Date
    public var end: Date

    public init(start: Date, end: Date) {
        // Ordered on construction rather than trusted: the two ends come from a UI where either
        // handle can be dragged past the other, and a reversed window silently sends nothing.
        self.start = min(start, end)
        self.end = max(start, end)
    }

    public var duration: TimeInterval { end.timeIntervalSince(start) }

    public func contains(_ moment: Date) -> Bool { moment >= start && moment <= end }

    /// Whether this window is the whole session, within a second.
    ///
    /// Used to decide whether anything is being trimmed at all. A second of slack, because a
    /// slider that lands a hair inside the ends is a person who did not mean to trim.
    public func isWholeSession(startedAt: Date, endedAt: Date?) -> Bool {
        guard let endedAt else { return start <= startedAt.addingTimeInterval(1) }
        return start <= startedAt.addingTimeInterval(1)
            && end >= endedAt.addingTimeInterval(-1)
    }
}

/// One recording or photograph, as the plan needs to see it.
public struct TrimmableMedia: Sendable, Equatable {
    public var relativePath: String
    public var kind: CaptureKind
    /// When it began, in session time.
    public var startedAt: Date
    /// How long it runs. Nil for a photograph, and nil for a recording nothing has measured yet.
    public var duration: TimeInterval?

    public init(relativePath: String, kind: CaptureKind, startedAt: Date, duration: TimeInterval?) {
        self.relativePath = relativePath
        self.kind = kind
        self.startedAt = startedAt
        self.duration = duration
    }

    /// True for the kinds that occupy a stretch of time rather than a moment.
    public var isTimed: Bool { kind != .photo }
}

/// What sending a window would actually send.
///
/// Computed before anything is exported, because the numbers are what a person decides on: three
/// readings and a twelve-minute cut of one recording is a different decision from four hundred
/// readings and the whole night.
public struct SessionTrimPlan: Sendable, Equatable {

    /// What happens to one file.
    public enum Outcome: Sendable, Equatable {
        /// Sent exactly as it is on the phone.
        case sentWhole
        /// Sent as a cut copy. Offsets are seconds INTO THE ORIGINAL FILE, which is what
        /// AVFoundation wants and what a reader can check against the original.
        case cut(from: TimeInterval, duration: TimeInterval)
        /// Not sent at all — it falls entirely outside the window.
        case leftOut
    }

    public struct MediaDecision: Sendable, Equatable {
        public var media: TrimmableMedia
        public var outcome: Outcome
    }

    public var window: SessionWindow
    public var isWholeSession: Bool
    public var readingCount: Int
    public var markerCount: Int
    public var media: [MediaDecision]

    public var sentWhole: [MediaDecision] { media.filter { $0.outcome == .sentWhole } }
    public var leftOut: [MediaDecision] { media.filter { $0.outcome == .leftOut } }
    public var cut: [MediaDecision] {
        media.filter { if case .cut = $0.outcome { return true } else { return false } }
    }

    /// The paths that will be sent at all, in the order they were given.
    public var includedPaths: [String] {
        media.filter { $0.outcome != .leftOut }.map(\.media.relativePath)
    }

    /// Decides what a window sends.
    ///
    /// - Parameters:
    ///   - window: the stretch to keep.
    ///   - startedAt: the session's own start, for deciding whether anything is being trimmed.
    ///   - endedAt: nil when the session was interrupted and its end is genuinely unknown.
    ///   - readingTimes: every reading's timestamp.
    ///   - markerTimes: the timestamps of readings carrying a mark, counted separately because a
    ///     window with no marks in it is a window somebody probably did not mean to choose.
    ///   - media: everything captured.
    public static func plan(window: SessionWindow,
                            startedAt: Date,
                            endedAt: Date?,
                            readingTimes: [Date],
                            markerTimes: [Date],
                            media: [TrimmableMedia]) -> SessionTrimPlan {
        SessionTrimPlan(
            window: window,
            isWholeSession: window.isWholeSession(startedAt: startedAt, endedAt: endedAt),
            readingCount: readingTimes.count(where: window.contains),
            markerCount: markerTimes.count(where: window.contains),
            media: media.map { MediaDecision(media: $0, outcome: outcome(for: $0, in: window)) })
    }

    /// What happens to one file, and why.
    ///
    /// A photograph is a moment: it is in or it is out. A recording is a span, and the three
    /// cases are the obvious ones — except for the fourth, which is a recording whose length
    /// nothing has measured.
    static func outcome(for item: TrimmableMedia, in window: SessionWindow) -> Outcome {
        guard item.isTimed else {
            return window.contains(item.startedAt) ? .sentWhole : .leftOut
        }

        guard let duration = item.duration, duration > 0 else {
            // **Unknown length errs towards keeping.** Without a duration this cannot tell a
            // recording that ended before the window from one that spans it, and dropping
            // evidence on a guess is the one failure worth ruling out. A recording that began
            // after the window closed is the single case that is knowable, so that one goes.
            return item.startedAt > window.end ? .leftOut : .sentWhole
        }

        let itemEnd = item.startedAt.addingTimeInterval(duration)
        if itemEnd < window.start || item.startedAt > window.end { return .leftOut }

        let insideStart = max(item.startedAt, window.start)
        let insideEnd   = min(itemEnd, window.end)

        // Within a second of the whole file is the whole file. Cutting a recording to 99.9% of
        // itself costs an export and a re-encode risk for nothing.
        let from = insideStart.timeIntervalSince(item.startedAt)
        let keep = insideEnd.timeIntervalSince(insideStart)
        if from <= 1 && keep >= duration - 1 { return .sentWhole }
        if keep <= 0 { return .leftOut }

        return .cut(from: from, duration: keep)
    }
}

/// The in and out points of a trim, and the rules for dragging them (item 210).
///
/// **Modelled on a video trimmer**, at Ben's request (2026-09-04): the in point starts at the
/// beginning and the out point at the end, each is dragged on its own, and what will be sent is
/// everything between them. The arithmetic lives here rather than in the view so the awkward
/// parts — a handle dragged past its partner, a drag off the end of the track, a session with no
/// honest end time — are decided once and can be tested without a simulator.
public struct SessionTrimRange: Sendable, Equatable {

    /// The shortest window that can be chosen. Below this the two handles are effectively one
    /// point, and a window of nothing uploads a session with no readings in it.
    public static let minimumDuration: TimeInterval = 1

    public let sessionStart: Date
    public let sessionEnd: Date
    public private(set) var inPoint: Date
    public private(set) var outPoint: Date

    /// - Parameter endedAt: nil for a session that was interrupted; the last reading stands in,
    ///   because a trimmer with no right-hand end has nothing to drag.
    public init(startedAt: Date, endedAt: Date?, lastReadingAt: Date?) {
        let end = endedAt ?? lastReadingAt ?? startedAt.addingTimeInterval(Self.minimumDuration)
        self.sessionStart = startedAt
        // A session whose end is not after its start would give a zero-width track and a division
        // by zero in every fraction below.
        self.sessionEnd = max(end, startedAt.addingTimeInterval(Self.minimumDuration))
        self.inPoint = startedAt
        self.outPoint = self.sessionEnd
    }

    public var duration: TimeInterval { sessionEnd.timeIntervalSince(sessionStart) }
    public var keptDuration: TimeInterval { outPoint.timeIntervalSince(inPoint) }

    /// True while the handles are still at the ends — nothing is being trimmed.
    public var isWholeSession: Bool {
        inPoint <= sessionStart.addingTimeInterval(0.5)
            && outPoint >= sessionEnd.addingTimeInterval(-0.5)
    }

    public var window: SessionWindow { SessionWindow(start: inPoint, end: outPoint) }

    /// Where a moment sits along the track, 0 to 1.
    public func fraction(of moment: Date) -> Double {
        guard duration > 0 else { return 0 }
        return min(1, max(0, moment.timeIntervalSince(sessionStart) / duration))
    }

    /// The moment at a point along the track. Values outside 0…1 are clamped, because a finger
    /// dragged past the end of the track is still a finger asking for the end of the track.
    public func date(atFraction fraction: Double) -> Date {
        sessionStart.addingTimeInterval(min(1, max(0, fraction)) * duration)
    }

    /// Drags the in point, never past the out point.
    public mutating func moveIn(toFraction fraction: Double) {
        let latest = outPoint.addingTimeInterval(-Self.minimumDuration)
        inPoint = min(max(date(atFraction: fraction), sessionStart), max(latest, sessionStart))
    }

    /// Drags the out point, never before the in point.
    public mutating func moveOut(toFraction fraction: Double) {
        let earliest = inPoint.addingTimeInterval(Self.minimumDuration)
        outPoint = max(min(date(atFraction: fraction), sessionEnd), min(earliest, sessionEnd))
    }

    /// Puts both handles back at the ends.
    public mutating func reset() {
        inPoint = sessionStart
        outPoint = sessionEnd
    }
}
