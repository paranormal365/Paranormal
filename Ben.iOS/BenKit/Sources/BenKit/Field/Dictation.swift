import Foundation

/// Turning speech into text, on the device.
///
/// **Offline is the requirement, not a preference.** A field session happens in a building with
/// no signal, so a transcriber that needs the network is no use at all here — it would work in
/// the car park and fail in the cellar, which is worse than not offering it.
///
/// Apple's recognisers can run entirely on-device, but ONLY on hardware and in languages that
/// support it. Where they cannot, the button is not shown: offering dictation that silently
/// needs a connection would be a promise broken at the worst moment.
public protocol DictationService: Sendable {
    /// True only when this device can transcribe with no network at all.
    var isAvailableOffline: Bool { get async }
    /// Asks for permission. Returns false if refused, or if the device cannot do it offline.
    func requestPermission() async -> Bool
    /// Starts listening. Partial results arrive as they are recognised.
    func start() async throws -> AsyncStream<DictationUpdate>
    /// Stops listening and returns the final text.
    @discardableResult
    func stop() async -> String
}

public struct DictationUpdate: Sendable, Equatable {
    public var text: String
    /// False while the recogniser may still revise what it has heard.
    public var isFinal: Bool

    public init(text: String, isFinal: Bool) {
        self.text = text
        self.isFinal = isFinal
    }
}

public enum DictationError: Error, LocalizedError, Equatable {
    case unavailableOffline
    case permissionDenied
    case couldNotStart(String)

    public var errorDescription: String? {
        switch self {
        case .unavailableOffline:
            "This device can't turn speech into text without a connection, so dictation isn't "
            + "offered here."
        case .permissionDenied:
            "Speech recognition is off for this app. Turn it on in Settings to dictate notes."
        case .couldNotStart(let reason):
            "Dictation couldn't start: \(reason)"
        }
    }
}

/// How a note was written down.
public enum NoteKind: String, Sendable, Codable, CaseIterable {
    /// Spoken and transcribed on the device. The default where the device can do it.
    case dictated
    /// Typed.
    case typed
    /// Recorded as audio, with no transcription — either because the device cannot, or because
    /// the person would rather have their own voice than a machine's reading of it.
    case audio

    public var title: String {
        switch self {
        case .dictated: "Dictate"
        case .typed: "Type"
        case .audio: "Record"
        }
    }

    public var icon: String {
        switch self {
        case .dictated: "waveform.badge.mic"
        case .typed: "keyboard"
        case .audio: "mic.circle"
        }
    }
}
