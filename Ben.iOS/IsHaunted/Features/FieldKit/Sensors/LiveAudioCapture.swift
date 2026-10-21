import Foundation
import os
import AVFoundation
import BenKit

/// Something else that wants to hear the microphone while the session has it — dictation.
///
/// One engine, one owner: a second AVAudioEngine opening the same input while the session's is
/// running reconfigures the audio hardware underneath it, and the session's engine stops.
protocol MicrophoneTapSharing: AnyObject, Sendable {
    /// Whether the session's microphone is running, so a listener can join it.
    var isUsingMicrophone: Bool { get }
    /// Hands every buffer the microphone delivers to `handler` until removed. Nil when the
    /// microphone isn't running, so the caller opens its own.
    func addListener(_ handler: @escaping (AVAudioPCMBuffer) -> Void) -> UUID?
    func removeListener(_ id: UUID)
}

/// The microphone: one engine, one owner, doing every job — the meter, the recording, and
/// dictation when a question is being typed as it is asked.
///
/// Metering and recording come off the SAME tap deliberately. Two objects each opening the
/// microphone is how a night's audio ends up silent — one of them wins, the other fails quietly,
/// and nobody finds out until they go looking for the recording that was never made.
///
/// **The engine is kept running (10/21/2026).** Ben recorded a session with video, sound and
/// questions: the sound reading never moved off -60 dB, and the sound didn't play back. Two things
/// were wrong. The meter stopped at -60 (see `SoundLevel`), which in `.measurement` mode is about
/// where an ordinary voice sits. And an AVAudioEngine stops itself whenever the audio hardware is
/// reconfigured — the camera starting or changing its quality, dictation opening a second engine
/// on the same microphone — and says so only with `AVAudioEngineConfigurationChange`, which
/// nothing here listened for. The tap stayed installed on a stopped engine, so nothing restarted
/// it: the meter held its last reading, and the recording got no more sound. Now:
///
/// - a configuration change rebuilds the tap at the input's new format and starts the engine
///   again;
/// - a watch notices the microphone going quiet for three seconds with no reason given, and does
///   the same;
/// - dictation joins this tap instead of opening a second engine (`MicrophoneTapSharing`);
/// - the file is written by `SessionAudioWriter`, which converts a buffer whose format has
///   changed, writes a gap as silence, and reports the sound actually in the file.
///
/// Interruptions are watched for and reported. A call, or another app taking the microphone,
/// stops a recording; somebody who believes they are recording and is not has lost the night, so
/// this is surfaced rather than swallowed.
final class LiveAudioCapture: AudioLevelSource, AudioRecording, MicrophoneTapSharing,
                              @unchecked Sendable {
    private static let log = Logger(subsystem: "com.ishaunted.ios", category: "microphone")

    private let engine = AVAudioEngine()
    private let lock = NSLock()

    // Everything below is read and written under `lock`.
    /// The recording being written, if any. See `SessionAudioWriter` for what it guards against.
    private var writer: SessionAudioWriter?
    private var tapInstalled = false
    /// The meter, kept so the tap can be rebuilt after the microphone comes back.
    private var meter: (@Sendable (AudioLevelSample) -> Void)?
    /// Whether something wants the microphone running: the meter, or a recording.
    private var wanted = false
    /// True while a video clip has been lent the microphone, so nothing here fights it for one.
    private var lent = false
    private var listeners: [UUID: (AVAudioPCMBuffer) -> Void] = [:]
    /// When the last buffer arrived, and when the engine was last started — the stall watch's clock.
    private var lastBufferAt: Date?
    private var engineStartedAt: Date?
    /// When the engine was last restarted here, so one change can't set off a run of restarts.
    private var lastRestartAt: Date?

    private var interruptionObserver: NSObjectProtocol?
    private var routeObserver: NSObjectProtocol?
    private var configurationObserver: NSObjectProtocol?
    private var stallWatch: Task<Void, Never>?

    private let eventFeed: AsyncStream<AudioRecordingEvent>
    private let eventSink: AsyncStream<AudioRecordingEvent>.Continuation

    /// Interruptions and the microphone coming back. See `AudioRecording.events`.
    var events: AsyncStream<AudioRecordingEvent> { eventFeed }

    init() {
        (eventFeed, eventSink) = AsyncStream.makeStream(of: AudioRecordingEvent.self)
    }

    var isAvailable: Bool { true }

    var isRecording: Bool {
        get async { hasOpenFile() }
    }

    // MARK: - Locked helpers
    //
    // Every lock is taken inside a synchronous helper: Swift 6 rightly refuses an NSLock held
    // across an await, and these are all short critical sections anyway.

    private func locked<T>(_ body: () -> T) -> T {
        lock.lock(); defer { lock.unlock() }
        return body()
    }

    private func hasOpenFile() -> Bool { locked { writer != nil } }
    private func hasTap() -> Bool { locked { tapInstalled } }
    private func isLent() -> Bool { locked { lent } }
    private func setLent(_ value: Bool) { locked { lent = value } }
    private func isWanted() -> Bool { locked { wanted } }

    private func adopt(_ created: SessionAudioWriter) {
        locked { writer = created }
    }

    /// Releases the file — which is what finalises the container — and reports how much sound it
    /// holds.
    private func releaseFile() -> TimeInterval {
        let finished = locked { () -> SessionAudioWriter? in
            defer { writer = nil }
            return writer
        }
        return finished?.close() ?? 0
    }

    // MARK: - The meter

    func levels() -> AsyncStream<AudioLevelSample> {
        AsyncStream { continuation in
            locked {
                meter = { continuation.yield($0) }
                wanted = true
            }
            do {
                try configureSession()
                try startEngine()
            } catch {
                // No microphone, or permission refused: the session carries on without sound.
                // A missing channel narrows a reading; it never stops one.
                locked { meter = nil }
                continuation.finish()
                return
            }
            // Watched from the meter as well as from a recording: the gauge going dead when
            // somebody takes a video is what Ben saw first, and the meter runs whether a clip is
            // being written or not.
            watchForInterruptions()
            continuation.onTermination = { [weak self] _ in self?.teardown() }
        }
    }

    // MARK: - Recording

    func beginRecording(to url: URL) async throws {
        // Asked, or refused, BEFORE the engine starts. With the microphone refused AVAudioEngine
        // does not fail: it delivers silence, and a night's recording is written full of it with
        // nothing on screen to say so (found 2026-09-27).
        switch AVAudioApplication.shared.recordPermission {
        case .denied:
            throw AudioRecordingError.microphoneRefused
        case .undetermined:
            guard await AVAudioApplication.requestRecordPermission() else {
                throw AudioRecordingError.microphoneRefused
            }
        default:
            break
        }
        do {
            try configureSession()
            locked { wanted = true }
            if !hasTap() || !engine.isRunning { try startEngine() }

            let format = engine.inputNode.outputFormat(forBus: 0)
            guard format.sampleRate > 0 else { throw AudioRecordingError.microphoneUnavailable }

            let created = try SessionAudioWriter(url: url, inputFormat: format)
            adopt(created)
            watchForInterruptions()
        } catch let error as AudioRecordingError {
            throw error
        } catch {
            throw AudioRecordingError.couldNotStart(error.localizedDescription)
        }
    }

    /// Stops, and reports how much sound the file holds.
    @discardableResult
    func endRecording() async -> TimeInterval { endRecording(keepingWatch: false) }

    /// Closes the clip. An interruption keeps the watch in place, because the microphone is
    /// expected back.
    @discardableResult
    private func endRecording(keepingWatch: Bool) -> TimeInterval {
        // Releasing the AVAudioFile is what finalises the container. Dropped without this, an
        // m4a has no moov atom and will not play anywhere.
        let seconds = releaseFile()
        if !keepingWatch, locked({ meter == nil }) { stopWatching() }
        return seconds
    }

    // MARK: - Sharing the microphone

    /// Whether the microphone is in use here — the level meter or a recording — so that something
    /// else using it for a moment (dictation) joins this tap instead of opening its own.
    var isUsingMicrophone: Bool { hasTap() && !isLent() }

    func addListener(_ handler: @escaping (AVAudioPCMBuffer) -> Void) -> UUID? {
        guard isUsingMicrophone else { return nil }
        let id = UUID()
        locked { listeners[id] = handler }
        return id
    }

    func removeListener(_ id: UUID) {
        locked { listeners[id] = nil }
    }

    // MARK: - Plumbing

    private func configureSession() throws {
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playAndRecord, mode: .measurement,
                                options: [.mixWithOthers, .defaultToSpeaker])
        try session.setActive(true)
    }

    /// Installs the tap at whatever format the microphone has NOW and starts the engine.
    private func startEngine() throws {
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0 else {
            throw AudioRecordingError.microphoneUnavailable
        }

        if hasTap() { input.removeTap(onBus: 0) }
        input.installTap(onBus: 0, bufferSize: 2048, format: format) { [weak self] buffer, _ in
            self?.deliver(buffer)
        }
        locked {
            tapInstalled = true
            engineStartedAt = Date()
            lastBufferAt = nil
        }
        if !engine.isRunning {
            engine.prepare()
            try engine.start()
        }
        watchTheEngine()
    }

    /// One buffer from the microphone: the meter, the file and any listener all get the same one,
    /// so what a person watched and what was recorded cannot disagree.
    private func deliver(_ buffer: AVAudioPCMBuffer) {
        let (meter, listeners) = locked { () -> ((@Sendable (AudioLevelSample) -> Void)?, [(AVAudioPCMBuffer) -> Void]) in
            lastBufferAt = Date()
            return (self.meter, Array(self.listeners.values))
        }
        if let meter {
            let levels = Self.levels(of: buffer)
            meter(AudioLevelSample(at: Date(), averageDbfs: levels.average, peakDbfs: levels.peak))
        }
        write(buffer)
        for listener in listeners { listener(buffer) }
    }

    /// RMS and peak of the first channel, in dBFS. See `SoundLevel` for why the floor is -100.
    private static func levels(of buffer: AVAudioPCMBuffer) -> (average: Double, peak: Double) {
        let silent = (SoundLevel.floorDbfs, SoundLevel.floorDbfs)
        guard let channel = buffer.floatChannelData?[0], buffer.frameLength > 0 else { return silent }
        let channels = Int(buffer.format.channelCount)
        let interleaved = buffer.format.isInterleaved && channels > 1
        let count = Int(buffer.frameLength) * (interleaved ? channels : 1)
        return SoundLevel.levels(of: channel, count: count, stride: interleaved ? channels : 1) ?? silent
    }

    // MARK: - Writing the file

    private func write(_ buffer: AVAudioPCMBuffer) {
        locked { writer }?.write(buffer)
    }

    // MARK: - Keeping the engine running

    /// Listens for the engine stopping itself, and watches for it going quiet without saying so.
    private func watchTheEngine() {
        if locked({ configurationObserver == nil }) {
            let observer = NotificationCenter.default.addObserver(
                forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main
            ) { [weak self] _ in
                Self.log.notice("The audio hardware changed; starting the microphone again.")
                self?.restartIfWanted()
            }
            locked { configurationObserver = observer }
        }
        if locked({ stallWatch == nil }) {
            let watch = Task { [weak self] in
                while !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(1))
                    // On the main queue, like the notifications: the engine is only ever started
                    // and stopped from one place at a time.
                    await MainActor.run { self?.restartIfStalled() }
                }
            }
            locked { stallWatch = watch }
        }
    }

    private func restartIfStalled() {
        let stalled = locked { () -> Bool in
            guard tapInstalled, wanted, !lent else { return false }
            return RecordingClock.hasStalled(lastBufferAt: lastBufferAt ?? engineStartedAt, now: Date())
        }
        guard stalled else { return }
        Self.log.notice("The microphone went quiet with no reason given; starting it again.")
        restartIfWanted()
    }

    /// Rebuilds the tap at the input's current format and starts the engine, unless the
    /// microphone has been handed to a clip or taken.
    private func restartIfWanted() {
        guard isWanted(), !isLent(), hasTap() else { return }
        let tooSoon = locked { () -> Bool in
            if let lastRestartAt, Date().timeIntervalSince(lastRestartAt) < 0.5 { return true }
            lastRestartAt = Date()
            return false
        }
        guard !tooSoon else { return }
        do {
            if engine.isRunning { engine.stop() }
            try configureSession()
            try startEngine()
        } catch {
            // The stall watch tries again in a few seconds; throwing from here would take the app
            // down, and the screen already shows the sound has stopped moving.
            locked { engineStartedAt = Date() }
            Self.log.error("The microphone couldn't start again: \(error.localizedDescription, privacy: .public)")
        }
    }

    private func stopWatching() {
        let (interruption, route, configuration, watch) = locked {
            defer {
                interruptionObserver = nil
                routeObserver = nil
                configurationObserver = nil
                stallWatch = nil
            }
            return (interruptionObserver, routeObserver, configurationObserver, stallWatch)
        }
        for observer in [interruption, route, configuration].compactMap({ $0 }) {
            NotificationCenter.default.removeObserver(observer)
        }
        watch?.cancel()
    }

    /// Watches for the microphone being taken and handed back.
    ///
    /// Ben, 2026-09-16: recording a video and returning left the sound dead, and the session crashed. Both come from
    /// the same place. The system camera takes the audio session, which arrives as an interruption; the old code
    /// closed the file and stopped there, so nothing recorded again — and the tap it left installed belonged to an
    /// input whose format the camera had changed, which is what AVAudioEngine throws over.
    ///
    /// So: on the way in, close the file and take the engine down properly. On the way out — and on any route change,
    /// because a camera handing the microphone back does not always send `.ended` — rebuild the tap from the input's
    /// current format and say so, and the session starts the next clip.
    private func watchForInterruptions() {
        guard locked({ interruptionObserver == nil }) else { return }

        let interruption = NotificationCenter.default.addObserver(
            forName: AVAudioSession.interruptionNotification,
            object: AVAudioSession.sharedInstance(), queue: .main
        ) { [weak self] notification in
            guard let raw = notification.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
                  let type = AVAudioSession.InterruptionType(rawValue: raw) else { return }
            switch type {
            case .began: self?.microphoneTaken()
            case .ended: self?.microphoneReturned()
            @unknown default: break
            }
        }

        let route = NotificationCenter.default.addObserver(
            forName: AVAudioSession.routeChangeNotification,
            object: AVAudioSession.sharedInstance(), queue: .main
        ) { [weak self] _ in
            // A route change while the engine is down is the camera giving the microphone back.
            self?.microphoneReturnedIfIdle()
        }
        locked {
            interruptionObserver = interruption
            routeObserver = route
        }
    }

    // MARK: - Lending the microphone to a video clip

    /// Handed over on purpose: a clip is about to record the sound itself.
    ///
    /// The file is finalised first, then the tap, the engine and the audio session all go — a
    /// capture session cannot take an input the engine is still holding. No `.interrupted` is
    /// sent, and nothing that arrives while lent is acted on: the session asked for this and is
    /// already handling it, and a second round of stop-and-resume would end the next clip too.
    func releaseMicrophone() async {
        setLent(true)
        _ = endRecording(keepingWatch: true)
        removeTapIfAny()
        engine.stop()
        engine.reset()
        try? AVAudioSession.sharedInstance()
            .setActive(false, options: .notifyOthersOnDeactivation)
    }

    /// The clip has stopped. The meter's tap is rebuilt at whatever format the microphone has now.
    func reclaimMicrophone() async {
        setLent(false)
        guard isWanted() else { return }
        do {
            try configureSession()
            try startEngine()
        } catch {
            // Same as a microphone coming back on its own: the next route change tries again,
            // and throwing out of here would take the app down.
        }
    }

    /// The microphone was taken: finish the clip and put the engine away, so no stale tap survives.
    private func microphoneTaken() {
        guard !isLent() else { return }
        _ = endRecording(keepingWatch: true)
        removeTapIfAny()
        engine.stop()
        engine.reset()
        eventSink.yield(.interrupted)
    }

    private func removeTapIfAny() {
        guard hasTap() else { return }
        engine.inputNode.removeTap(onBus: 0)
        locked { tapInstalled = false }
    }

    /// The microphone is available again: rebuild the meter's tap at the format it now has.
    private func microphoneReturned() {
        guard !isLent(), isWanted() else { return }
        do {
            try configureSession()
            try startEngine()
            eventSink.yield(.resumed)
        } catch {
            // Nothing to do here but stay quiet: the next route change tries again, and the screen already says
            // sound stopped. Throwing from a notification would take the app down, which is what we are fixing.
        }
    }

    private func microphoneReturnedIfIdle() {
        guard !isLent(), !hasTap() else { return }
        microphoneReturned()
    }

    private func teardown() {
        _ = releaseFile()
        locked {
            meter = nil
            wanted = false
            listeners = [:]
        }
        stopWatching()
        removeTapIfAny()
        engine.stop()
        try? AVAudioSession.sharedInstance().setActive(false)
    }
}
