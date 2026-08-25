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
