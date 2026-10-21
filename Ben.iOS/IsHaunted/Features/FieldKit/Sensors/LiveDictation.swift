import Foundation
import Speech
import AVFoundation
import BenKit

/// On-device speech recognition, through `SFSpeechRecognizer`.
///
/// `requiresOnDeviceRecognition` is set unconditionally. Left off, Apple may send audio to its
/// servers — which would work at the kerb and fail in the cellar, and would also mean somebody's
/// investigation notes leaving the device without them choosing that. Neither is acceptable here,
/// so where on-device is unsupported the feature is simply not offered.
final class LiveDictation: DictationService, @unchecked Sendable {

    private let recognizer = SFSpeechRecognizer(locale: Locale.current)
        ?? SFSpeechRecognizer(locale: Locale(identifier: "en_US"))
    private let engine = AVAudioEngine()
    private let lock = NSLock()

    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var task: SFSpeechRecognitionTask?
    private var latest = ""
    /// The session's own microphone. While it is running, dictation listens to ITS buffers rather
    /// than opening a second engine on the same input. Two engines on one microphone is one way
    /// the session's sound stopped during a question (10/20/2026): starting this one reconfigured
    /// the hardware underneath the session's engine, which stopped. And the shared audio session
    /// is left alone when dictation stops: switching it off would end the session's recording too
    /// (10/15/2026).
    private weak var shared: (any MicrophoneTapSharing)?
    /// Set while listening through the session's microphone.
    private var sharedListener: UUID?

    init(sharing shared: (any MicrophoneTapSharing)? = nil) {
        self.shared = shared
    }

    var isAvailableOffline: Bool {
        get async {
            guard let recognizer, recognizer.isAvailable else { return false }
            // The whole gate. A device that can only transcribe online does not get the button.
            return recognizer.supportsOnDeviceRecognition
        }
    }

    func requestPermission() async -> Bool {
        guard await isAvailableOffline else { return false }
        let speech = await withCheckedContinuation { continuation in
            SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0) }
        }
        guard speech == .authorized else { return false }

        return await withCheckedContinuation { continuation in
            AVAudioApplication.requestRecordPermission { continuation.resume(returning: $0) }
        }
    }

    func start() async throws -> AsyncStream<DictationUpdate> {
        guard let recognizer, await isAvailableOffline else {
            throw DictationError.unavailableOffline
        }
        guard await requestPermission() else { throw DictationError.permissionDenied }

        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = true
        request.requiresOnDeviceRecognition = true

        setText("")
        store(request: request)

        return AsyncStream { continuation in
            do {
                // Through the session's microphone when it is running: no second engine, and the
                // audio session is not touched.
                if let shared, let id = shared.addListener({ request.append($0) }) {
                    store(listener: id)
                } else {
                    try startOwnEngine(feeding: request)
                }

                let task = recognizer.recognitionTask(with: request) { [weak self] result, error in
                    if let result {
                        let text = result.bestTranscription.formattedString
                        self?.setText(text)
                        continuation.yield(DictationUpdate(text: text,
                                                           isFinal: result.isFinal))
                        if result.isFinal { continuation.finish() }
                    }
                    if error != nil { continuation.finish() }
                }
                store(task: task)
            } catch {
                continuation.finish()
            }

            continuation.onTermination = { [weak self] _ in
                Task { await self?.stop() }
            }
        }
    }

    /// The microphone, opened by dictation itself — only when no session has it (a note written
    /// outside a recording, say).
    private func startOwnEngine(feeding request: SFSpeechAudioBufferRecognitionRequest) throws {
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playAndRecord, mode: .measurement,
                                options: [.mixWithOthers, .defaultToSpeaker])
        try session.setActive(true)

        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        // No microphone — the camera has it, or the session never came up — reports as a
        // format with no channels, and installing a tap for it raises rather than throws.
        guard format.sampleRate > 0, format.channelCount > 0 else {
            throw DictationError.unavailableOffline
        }
        // A tap left by a start that failed after this point would make the next install
        // raise; removing one that is not there is allowed and costs nothing.
        input.removeTap(onBus: 0)
        input.installTap(onBus: 0, bufferSize: 1024, format: format) { buffer, _ in
            request.append(buffer)
        }
        engine.prepare()
        try engine.start()
    }

    @discardableResult
    func stop() async -> String {
        let (request, task) = takeHandles()
        request?.endAudio()
        task?.finish()

        if let listener = takeListener() {
            // Listening through the session's microphone: step off it, and leave it running.
            shared?.removeListener(listener)
        } else {
            // The tap comes off whether or not the engine got as far as running: `start` installs
            // it before `engine.start()`, and a failure between the two would otherwise leave it
            // in place for the next attempt to trip over.
            engine.inputNode.removeTap(onBus: 0)
            if engine.isRunning { engine.stop() }
            if shared?.isUsingMicrophone != true {
                try? AVAudioSession.sharedInstance().setActive(false)
            }
        }

        // A beat for the recogniser to hand back its last revision — stopping mid-word otherwise
        // loses the end of the sentence, which is usually the part that mattered.
        try? await Task.sleep(for: .milliseconds(350))
        return currentText()
    }

    // Locks stay in synchronous helpers: Swift 6 refuses one held across an await.
    private func store(request: SFSpeechAudioBufferRecognitionRequest) {
        lock.lock(); defer { lock.unlock() }
        self.request = request
    }

    private func store(listener: UUID) {
        lock.lock(); defer { lock.unlock() }
        sharedListener = listener
    }

    private func takeListener() -> UUID? {
        lock.lock(); defer { lock.unlock() }
        let listener = sharedListener
        sharedListener = nil
        return listener
    }

    private func store(task: SFSpeechRecognitionTask) {
        lock.lock(); defer { lock.unlock() }
        self.task = task
    }

    private func takeHandles() -> (SFSpeechAudioBufferRecognitionRequest?, SFSpeechRecognitionTask?) {
        lock.lock(); defer { lock.unlock() }
        let handles = (request, task)
        request = nil
        task = nil
        return handles
    }

    private func setText(_ text: String) {
        lock.lock(); defer { lock.unlock() }
        latest = text
    }

    private func currentText() -> String {
        lock.lock(); defer { lock.unlock() }
        return latest
    }
}
