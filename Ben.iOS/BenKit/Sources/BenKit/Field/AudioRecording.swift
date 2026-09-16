import Foundation

/// Recording sound to a file, as opposed to merely measuring it.
///
/// Separate from `AudioLevelSource` because they are different promises — a meter can run all
/// night on nothing, while a recording fills a disk — but the same object usually implements
/// both, since two things fighting over one microphone is how a night's audio ends up silent.
public protocol AudioRecording: Sendable {
    /// Begins writing to `url`. Throws rather than failing quietly: a recording somebody
    /// believes is running and is not is the worst outcome this feature has.
    func beginRecording(to url: URL) async throws
    /// Stops, and reports how long it ran.
    @discardableResult
    func endRecording() async -> TimeInterval
    var isRecording: Bool { get async }

    /// What the microphone did without being asked: taken away, and handed back.
    ///
    /// Ben, 2026-09-16, testing the approved build: recording a video and coming back left the sound dead —
    /// "it is like it doesn't know to continue recording audio whether it is recording video or not". The system
    /// camera takes the audio session, which arrives as an interruption; a recorder that only stops on the way in
    /// leaves the rest of the night silent. So both halves are reported and the session acts on them.
    ///
    /// A recorder that cannot be interrupted — a stub, a silent one — says nothing, which the default provides.
    var events: AsyncStream<AudioRecordingEvent> { get }
}

/// Something that happened to the microphone rather than something the app asked for.
public enum AudioRecordingEvent: Sendable, Equatable {
    /// The microphone was taken. Whatever was recorded up to here is finished and playable.
    case interrupted

    /// It is available again: a recording that was running should carry on as a new clip.
    case resumed
}

public extension AudioRecording {
    var events: AsyncStream<AudioRecordingEvent> {
        AsyncStream { $0.finish() }
    }
}

/// What went wrong with a recording, in words a person can act on.
public enum AudioRecordingError: Error, LocalizedError, Equatable {
    case microphoneUnavailable
    case couldNotStart(String)
    case interrupted

    public var errorDescription: String? {
        switch self {
        case .microphoneUnavailable:
            "The microphone isn't available. Check the app's permission in Settings."
        case .couldNotStart(let reason):
            "Recording couldn't start: \(reason)"
        case .interrupted:
            "The recording was interrupted — a call, or another app taking the microphone."
        }
    }
}
