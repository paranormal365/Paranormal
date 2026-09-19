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

/// What one upload is allowed to carry.
///
/// **The phone is not limited. The upload is.** Ben, 2026-09-12: record as much of the room as
/// the night needs, at whatever the camera gives — storage on the device is the investigator's
/// own business, and a session that stopped recording because an app decided five minutes was
/// enough is a session that missed the thing it was there for. What cannot be unlimited is the
/// package: video is one or two orders of magnitude heavier than everything else a session holds,
/// and it goes up a phone's upstream connection into an account with a storage allowance.
///
/// So the window decides. Send five minutes of video, move the window, send the next five. The
/// readings, marks, photographs and audio are not rationed — only video is, because only video
/// is the problem.
public enum UploadAllowance: Sendable {
    /// Video seconds permitted in a single upload.
    public static let videoSeconds: TimeInterval = 5 * 60

    /// Said the way a person reads it: "5 minutes".
    public static var spokenVideo: String {
        "\(Int(videoSeconds / 60)) minutes"
    }

    /// The ceiling on everything else, together, in one upload.
    ///
    /// **Why bytes and not minutes for the rest.** Nothing else in a session is heavy enough to
    /// ration by the clock, and rationing it by the clock would punish the cheap channels for
    /// video's sins. A five-hour night is roughly a megabyte of readings and marks, and AAC audio
    /// runs near a megabyte a minute — so a whole night of sound and instruments together lands
    /// around a third of this, and goes in one send. Photographs are the only other thing that
    /// can add up, and they add up in bytes, which is what this counts.
    ///
    /// **Why this number.** A personal account holds 2 GB (`AccountStorageGuard`), so this is a
    /// quarter of somebody's whole allowance in a single upload — generous enough that an ordinary
    /// night never meets it, small enough that four careless ones cannot silently fill an account
    /// or park a phone on a home connection for an hour.
    public static let maximumBytes: Int64 = 500 * 1024 * 1024

    public static var spokenSize: String {
        ByteCountFormatter.string(fromByteCount: maximumBytes, countStyle: .file)
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
    /// What the file weighs on the phone. Nil when nothing recorded it, in which case it is
    /// counted as nothing rather than guessed at.
    public var byteCount: Int64?
    /// The picture's shape, read off the file, so a smaller-quality estimate is arithmetic rather
    /// than a guess. Nil for anything that is not video.
    public var videoHeight: Int?
    public var videoFrameRate: Double?

    public init(relativePath: String, kind: CaptureKind, startedAt: Date, duration: TimeInterval?,
                byteCount: Int64? = nil,
                videoHeight: Int? = nil, videoFrameRate: Double? = nil) {
        self.relativePath = relativePath
        self.kind = kind
        self.startedAt = startedAt
        self.duration = duration
        self.byteCount = byteCount
        self.videoHeight = videoHeight
        self.videoFrameRate = videoFrameRate
    }

    /// Roughly what would go up if only `seconds` of this file were sent.
    ///
    /// Bitrate is near enough constant within one recording, so a proportion of the length is a
    /// proportion of the bytes. It is an estimate and is labelled as one wherever it is shown.
    func approximateBytes(forSeconds seconds: TimeInterval) -> Int64 {
        guard let byteCount else { return 0 }
        guard let duration, duration > 0 else { return byteCount }
        let fraction = min(1, max(0, seconds / duration))
        return Int64((Double(byteCount) * fraction).rounded())
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

    // ── How much video one upload may carry ───────────────────────────────────

    /// Seconds of video this window would actually send, cuts counted at their cut length.
    ///
    /// A clip whose length nothing has measured counts as nothing. That is not a loophole worth
    /// closing with a guess: refusing an upload on a number we do not have would block somebody
    /// over a file that may be four seconds long.
    public var videoSecondsSent: TimeInterval {
        media.reduce(0) { total, decision in
            guard decision.media.kind == .video else { return total }
            switch decision.outcome {
            case .leftOut:            return total
            case .cut(_, let length): return total + length
            case .sentWhole:          return total + (decision.media.duration ?? 0)
            }
        }
    }

    /// Whether this window carries more video than one upload is allowed.
    public var exceedsVideoAllowance: Bool {
        videoSecondsSent > UploadAllowance.videoSeconds + 1   // a second of slack for rounding
    }

    /// How much has to come off before it will go.
    public var videoSecondsOverAllowance: TimeInterval {
        max(0, videoSecondsSent - UploadAllowance.videoSeconds)
    }

    /// About how many bytes of media this window would send. Readings and marks are not counted:
    /// a whole night of them is a megabyte or so, and pretending to weigh them to the byte would
    /// dress an estimate up as an audit.
    public var approximateBytesSent: Int64 {
        media.reduce(0) { total, decision in
            switch decision.outcome {
            case .leftOut:            return total
            case .sentWhole:          return total + (decision.media.byteCount ?? 0)
            case .cut(_, let length): return total + decision.media.approximateBytes(forSeconds: length)
            }
        }
    }

    /// Whether this window is heavier than one upload may be.
    public var exceedsSizeAllowance: Bool {
        approximateBytesSent > UploadAllowance.maximumBytes
    }

    /// What the same window would weigh with the video sent at a smaller quality.
    ///
    /// Only video is touched. Re-encoding a photograph to save a few hundred kilobytes would cost
    /// its detail for nothing, and audio is not where the weight is.
    public func approximateBytesSent(atVideoQuality quality: VideoQuality) -> Int64 {
        media.reduce(0) { total, decision in
            let item = decision.media
            let sent: Int64
            switch decision.outcome {
            case .leftOut:            return total
            case .sentWhole:          sent = item.byteCount ?? 0
            case .cut(_, let length): sent = item.approximateBytes(forSeconds: length)
            }
            guard item.kind == .video, quality.changesAnything else { return total + sent }
            return total + quality.approximateBytes(from: sent,
                                                    sourceHeight: item.videoHeight,
                                                    sourceFrameRate: item.videoFrameRate)
        }
    }

    /// Whether sending the video smaller is enough to get this window under the ceiling.
    public func fits(atVideoQuality quality: VideoQuality) -> Bool {
        approximateBytesSent(atVideoQuality: quality) <= UploadAllowance.maximumBytes
    }

    /// The smallest change that would let this window go, or nil when no quality is enough and
    /// the window itself has to come in.
    public func smallestQualityThatFits() -> VideoQuality? {
        VideoQuality.offered.first(where: fits(atVideoQuality:))
    }

    /// Whether anything at all stops this window going up as one upload.
    public var exceedsAnAllowance: Bool { exceedsVideoAllowance || exceedsSizeAllowance }

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

extension SessionTrimPlan {
    /// What a trimmed session is called once it is sent (item 210).
    ///
    /// Ben offered two shapes — `name-Clip001` or `name (in–out)` — and left the choice. The times
    /// win: "back bedroom (20:00–30:00)" says what the clip IS, on the server's list, in the
    /// player's title and in the report, where a counter would only say that it is the third of
    /// something. Elapsed times rather than clock times, because the session's own start is what
    /// the readout beside the trimmer shows and what a person dragged against.
    ///
    /// A whole session keeps its name untouched: it is not a clip of anything.
    public static func clipLabel(base: String?, window: SessionWindow, sessionStart: Date,
                                 isWholeSession: Bool) -> String? {
        guard !isWholeSession else { return base }
        let from = clock(window.start.timeIntervalSince(sessionStart))
        let to = clock(window.end.timeIntervalSince(sessionStart))
        let span = "(\(from)–\(to))"
        guard let base, !base.trimmingCharacters(in: .whitespaces).isEmpty else {
            return "Clip \(span)"
        }
        return "\(base) \(span)"
    }

    /// h:mm:ss, or m:ss under an hour — the same spelling as the trimmer's readouts.
    public static func clock(_ seconds: TimeInterval) -> String {
        let total = Int(max(0, seconds.rounded()))
        let (h, m, s) = (total / 3600, (total % 3600) / 60, total % 60)
        return h > 0
            ? String(format: "%d:%02d:%02d", h, m, s)
            : String(format: "%d:%02d", m, s)
    }
}
