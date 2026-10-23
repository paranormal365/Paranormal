import Foundation

/// Who still wants the microphone: the level meter's feed, a recording, both, or nobody.
///
/// **Why this exists (10/23/2026).** Ben's first session on 1.1.5 had video and a moving sound
/// reading, and no sound at all. The session restarts its sensor feeds whenever a setting changes
/// mid-session — Watch for Motion, the sentry, a channel — and the old meter feed ending used to
/// tear the whole microphone down, the open recording with it. The new feed brought the
/// microphone back, so the reading kept moving; the file had been closed underneath the session,
/// and at Stop it held nothing and was thrown away as empty.
///
/// The rule here: a feed ending takes away only that feed, and the microphone is put away only
/// when nothing at all wants it. A feed being replaced can end AFTER its replacement has started,
/// so each feed is known by its own id.
public struct MicrophoneDemand: Sendable, Equatable {
    /// The meter feed now running, if any.
    public private(set) var meterFeed: UUID?
    /// Whether a recording is open.
    public private(set) var isRecording = false

    public init() {}

    /// Whether anything wants the microphone kept running.
    public var wantsMicrophone: Bool { meterFeed != nil || isRecording }

    /// A meter feed has started; it replaces any earlier one. Keep the id for `meterEnded`.
    public mutating func meterStarted() -> UUID {
        let feed = UUID()
        meterFeed = feed
        return feed
    }

    /// A meter feed has ended. True when nothing wants the microphone any more.
    @discardableResult
    public mutating func meterEnded(_ feed: UUID) -> Bool {
        if meterFeed == feed { meterFeed = nil }
        return !wantsMicrophone
    }

    public mutating func recordingStarted() { isRecording = true }

    /// The recording has been closed. True when nothing wants the microphone any more.
    @discardableResult
    public mutating func recordingEnded() -> Bool {
        isRecording = false
        return !wantsMicrophone
    }

    /// Everything let go at once — the microphone is being put away regardless.
    public mutating func reset() { self = MicrophoneDemand() }
}
