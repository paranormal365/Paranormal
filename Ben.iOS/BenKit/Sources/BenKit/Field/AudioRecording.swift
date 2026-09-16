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

    /// Hands the microphone to the camera on purpose, because a video clip is about to record
    /// the sound itself.
    ///
    /// Ben, 2026-09-16: "if we switch to recording video, the audio is just taken from the video
    /// file until stopped and then back to audio — so there is no gap in audio recording." So the
    /// microphone changes hands exactly once, deliberately, and the video file covers the stretch
    /// in between. Everything is put away — engine and tap, not merely the file closed — because
    /// two things holding one microphone is how a night ends up silent.
    ///
    /// No `.interrupted` event follows: this was asked for, and the session is already acting on it.
    func releaseMicrophone() async

    /// Takes the microphone back when the clip stops, so the session's own recording carries on.
    func reclaimMicrophone() async
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

    /// A recorder with no engine of its own has nothing to hand over.
    func releaseMicrophone() async {}
    func reclaimMicrophone() async {}
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
