import Foundation
// @preconcurrency: AVCaptureSession predates Sendable and is not marked, but start/stopRunning
// are documented as callable from any thread — hopping them through a queue is exactly what
// Apple's own sample code does. This treats the module's missing annotations as the legacy they
// are instead of decorating every capture with its own unsafe marker.
@preconcurrency import AVFoundation
import CoreMotion
import UIKit
import SwiftUI
import BenKit

/// Notification observers, held somewhere `deinit` is allowed to reach them.
private final class ObserverBox: @unchecked Sendable {
    private let lock = NSLock()
    private var observers: [NSObjectProtocol] = []

    var isEmpty: Bool { lock.lock(); defer { lock.unlock() }; return observers.isEmpty }

    func keep(_ observer: NSObjectProtocol) {
        lock.lock(); observers.append(observer); lock.unlock()
    }

    func release() {
        lock.lock()
        let held = observers
        observers = []
        lock.unlock()
        for observer in held { NotificationCenter.default.removeObserver(observer) }
    }
}

/// What the camera can refuse to do, in the words the screen shows.
enum FieldCameraError: LocalizedError {
    case notRunning
    case noClipRunning
    case photoFailed
    case clipFailed(String)

    var errorDescription: String? {
        switch self {
        case .notRunning:
            "The camera isn't running yet. Give it a moment and try again."
        case .noClipRunning:
            "No clip is recording."
        case .photoFailed:
            "The photo came back empty and wasn't saved."
        case .clipFailed(let reason):
            reason
        }
    }
}

/// The camera as an instrument: a live view you can aim, a judgement about whether anything in
/// front of it moved, and the photos and clips themselves.
///
/// A phone left in a corner is useless if you could not see what it was pointing at when you put
/// it down, so the preview exists to be aimed by. The motion detection is deliberately crude —
/// it compares how much of a downsampled frame changed — because anything cleverer would make
/// promises about WHAT moved that a phone in the dark cannot keep.
///
/// Photos and clips are taken by this same session rather than by the system camera, because the
/// app leaving for another one is what broke a session: the picker took the camera, this session
/// was interrupted and nothing ever started it again, and the microphone went with it (Ben,
/// 2026-09-16). One session, owned here, never handed over.
///
/// **A clip records the sound too, and the session's own recording steps aside for it.** Ben,
/// 2026-09-16: "the audio is just taken from the video file until stopped and then back to audio
/// — so there is no gap in audio recording, just video added to a part." The microphone changes
/// hands once, deliberately: the audio clip running is closed and kept, the video covers the
/// stretch that follows, and the session's recording starts again as a new clip the moment the
/// video stops. Laid end to end on the review's one timeline, the sound has no hole in it.
///
/// The caller does the handover — `lendMicrophoneToTheClip()` before `startClip`, and
/// `takeMicrophoneBackFromTheClip()` after `finishClip` — because only the session knows whether
/// it was recording and where the next clip's file goes.
@MainActor
@Observable
final class FieldCameraSession {

    let session = AVCaptureSession()
    private(set) var isRunning = false
    private(set) var problem: String?

    /// The clip being recorded now, and when it started, so the screen can count it up.
    private(set) var clipStartedAt: Date?
    var isRecordingClip: Bool { clipStartedAt != nil }

    private let output = AVCaptureVideoDataOutput()
    private let photoOutput = AVCapturePhotoOutput()
    private let movieOutput = AVCaptureMovieFileOutput()
    private let delegate = FrameDelegate()
    private let queue = DispatchQueue(label: "com.ishaunted.field.camera")

    /// Delegates AVFoundation calls back on its own queue. Held here for as long as the capture
    /// they belong to is in flight.
    private var photoCaptures: [PhotoCapture] = []
    private var clipCapture: ClipCapture?
    /// The microphone, attached only for the length of a clip and taken off again straight after.
    private var audioInput: AVCaptureDeviceInput?
    private var rotation: AVCaptureDevice.RotationCoordinator?
    /// In a box of its own so `deinit` — which cannot touch main-actor state — can still hand
    /// the observers back. @Observable would otherwise make this a computed property that
    /// nothing outside the actor may read.
    @ObservationIgnored private let interruptionObservers = ObserverBox()

    init() {
        delegate.owner = self
    }

    deinit {
        interruptionObservers.release()
    }

    /// A stream of "how much of the view changed", for the engine to judge against a threshold.
    nonisolated func sceneMotion() -> AsyncStream<SceneMotionSample> {
        delegate.stream()
    }

    func start() {
        guard !isRunning else { return }

        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            configureAndRun()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { [weak self] granted in
                Task { @MainActor in
                    if granted { self?.configureAndRun() }
                    else { self?.problem = "Camera access was declined." }
                }
            }
        default:
            problem = "Camera access is off for this app. Turn it on in Settings to use video."
        }
    }

    func stop() {
        guard isRunning else { return }
        let session = session
        queue.async { session.stopRunning() }
        isRunning = false
    }

    // MARK: - Taking something

    /// One photo, written to a file of its own. The caller moves it into the session.
    func capturePhoto() async throws -> URL {
        guard isRunning else { throw FieldCameraError.notRunning }

        let capture = PhotoCapture()
        photoCaptures.append(capture)
        defer { photoCaptures.removeAll { $0 === capture } }

        let settings = AVCapturePhotoSettings()
        if let connection = photoOutput.connection(with: .video),
           let angle = rotation?.videoRotationAngleForHorizonLevelCapture,
           connection.isVideoRotationAngleSupported(angle) {
            connection.videoRotationAngle = angle
        }
        return try await capture.run(on: photoOutput, settings: settings)
    }

    /// Starts a clip that records the sound as well as the picture.
    ///
    /// The microphone must already have been lent to this by the session — see the type's
    /// remarks. `withSound: false` exists for a session recording no audio at all.
    func startClip(withSound: Bool = true) throws {
        guard isRunning else { throw FieldCameraError.notRunning }
        guard clipCapture == nil else { return }

        // Up from the watching preset for the length of the clip only: a 480p feed is fine to
        // aim by and to difference frames against, and is not what anybody wants to look at
        // afterwards. It goes back down when the clip ends, so an all-night session still costs
        // what it used to.
        session.beginConfiguration()
        if session.canSetSessionPreset(.hd1280x720) { session.sessionPreset = .hd1280x720 }

        if withSound, audioInput == nil {
            // The category has to be one that records: the app's own audio session is left alone
            // by this capture session (automaticallyConfiguresApplicationAudioSession is off, so
            // it can never reconfigure the field recorder's), which means setting it here is
            // nobody else's job. Without it the movie is written silently and says nothing.
            let audio = AVAudioSession.sharedInstance()
            try? audio.setCategory(.playAndRecord, mode: .videoRecording,
                                   options: [.mixWithOthers, .defaultToSpeaker])
            try? audio.setActive(true)

            if let device = AVCaptureDevice.default(for: .audio),
               let input = try? AVCaptureDeviceInput(device: device),
               session.canAddInput(input) {
                session.addInput(input)
                audioInput = input
            } else {
                // Worth saying rather than quietly filming a silent clip: the session's own
                // recording has already stepped aside for this.
                problem = "The microphone wasn't available, so this clip has no sound."
            }
        }
        if session.outputs.contains(movieOutput) == false {
            guard session.canAddOutput(movieOutput) else {
                // Unwound before the throw, not left for finishClip — which is never called on
                // this path. Left as it was, the microphone stayed attached to THIS session while
                // the field recorder tried to take it back, and the preset stayed at 720p for the
                // rest of the night.
                if let audioInput {
                    session.removeInput(audioInput)
                    self.audioInput = nil
                }
                if session.canSetSessionPreset(.medium) { session.sessionPreset = .medium }
                session.commitConfiguration()
                throw FieldCameraError.clipFailed(
                    "This device won't record a clip while it is watching the room.")
            }
            session.addOutput(movieOutput)
        }
        session.commitConfiguration()

        if let connection = movieOutput.connection(with: .video),
           let angle = rotation?.videoRotationAngleForHorizonLevelCapture,
           connection.isVideoRotationAngleSupported(angle) {
            connection.videoRotationAngle = angle
        }

        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("field-\(UUID().uuidString).mov")
        let capture = ClipCapture()
        clipCapture = capture
        clipStartedAt = Date()
        movieOutput.startRecording(to: url, recordingDelegate: capture)
    }

    /// Ends the clip and hands back the file it wrote.
    func finishClip() async throws -> URL {
        guard let capture = clipCapture else { throw FieldCameraError.noClipRunning }
        defer {
            clipCapture = nil
            clipStartedAt = nil
            session.beginConfiguration()
            if session.outputs.contains(movieOutput) { session.removeOutput(movieOutput) }
            // The microphone goes back before the session reclaims it: an input still attached
            // here is an input the field recorder's engine cannot have.
            if let audioInput {
                session.removeInput(audioInput)
                self.audioInput = nil
            }
            if session.canSetSessionPreset(.medium) { session.sessionPreset = .medium }
            session.commitConfiguration()
        }
        movieOutput.stopRecording()
        return try await capture.fileWhenFinished()
    }

    private func configureAndRun() {
        guard !isRunning else { return }
        session.beginConfiguration()
        // Low, on purpose: this feed is for aiming and for spotting movement, not for the
        // recording. High resolution here would cost battery all night for nothing — a clip
        // raises it for its own length and puts it back.
        session.sessionPreset = .medium
        // The field session owns the audio session: its recorder is running and must keep
        // running. Letting AVCaptureSession configure the app's audio session is how the camera
        // takes the microphone even with no audio input attached — so it never does, and a clip
        // sets the category itself for exactly as long as it holds the microphone.
        session.automaticallyConfiguresApplicationAudioSession = false

        var cameraDevice: AVCaptureDevice?
        if let existing = (session.inputs.first as? AVCaptureDeviceInput)?.device {
            cameraDevice = existing
        } else {
            guard let device = AVCaptureDevice.default(.builtInWideAngleCamera,
                                                       for: .video, position: .back),
                  let input = try? AVCaptureDeviceInput(device: device),
                  session.canAddInput(input)
            else {
                session.commitConfiguration()
                problem = "No camera is available on this device."
                return
            }
            session.addInput(input)
            cameraDevice = device
        }

        if session.outputs.contains(output) == false {
            output.alwaysDiscardsLateVideoFrames = true
            output.videoSettings = [
                kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
            ]
            output.setSampleBufferDelegate(delegate, queue: queue)
            if session.canAddOutput(output) { session.addOutput(output) }
        }

        if session.outputs.contains(photoOutput) == false, session.canAddOutput(photoOutput) {
            session.addOutput(photoOutput)
        }

        session.commitConfiguration()

        if let cameraDevice, rotation == nil {
            rotation = AVCaptureDevice.RotationCoordinator(device: cameraDevice, previewLayer: nil)
        }
        watchForInterruptions()

        let session = session
        queue.async { session.startRunning() }
        isRunning = true
        problem = nil
    }

    /// A phone call, another app taking the camera, or the system reclaiming it leaves a preview
    /// frozen and a session that nobody restarts. Before this, coming back from the system
    /// camera left a black rectangle where the room used to be.
    private func watchForInterruptions() {
        guard interruptionObservers.isEmpty else { return }
        let centre = NotificationCenter.default

        interruptionObservers.keep(centre.addObserver(
            forName: AVCaptureSession.wasInterruptedNotification,
            object: session, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                self.isRunning = false
                self.problem = "Something else is using the camera."
            }
        })

        interruptionObservers.keep(centre.addObserver(
            forName: AVCaptureSession.interruptionEndedNotification,
            object: session, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                self.problem = nil
                let session = self.session
                self.queue.async { session.startRunning() }
                self.isRunning = true
            }
        })

        interruptionObservers.keep(centre.addObserver(
            forName: AVCaptureSession.runtimeErrorNotification,
            object: session, queue: .main
        ) { [weak self] notification in
            // Only the one error Apple says to restart after: media services were reset. Any
            // other runtime error restarted the session unconditionally, and a session that fails
            // for the same reason again raises the same notification again — a restart loop with
            // the preview frozen the whole time. Everything else is said, and the shutter stays
            // off until the camera is running again.
            let error = notification.userInfo?[AVCaptureSessionErrorKey] as? AVError
            let recoverable = error?.code == .mediaServicesWereReset
            Task { @MainActor in
                guard let self else { return }
                if recoverable {
                    let session = self.session
                    self.queue.async { session.startRunning() }
                    self.isRunning = true
                } else {
                    self.isRunning = false
                    self.problem = error?.localizedDescription
                        ?? "The camera stopped. Close this and open it again."
                }
            }
        })
    }
}

/// One photo, from `capturePhoto` to a file on disk.
///
/// A capture object per photo: AVFoundation holds its delegate only until that capture finishes,
/// and a shared one would have to reason about two shutters at once for no gain.
private final class PhotoCapture: NSObject, AVCapturePhotoCaptureDelegate, @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<URL, Error>?

    func run(on output: AVCapturePhotoOutput,
             settings: AVCapturePhotoSettings) async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock(); self.continuation = continuation; lock.unlock()
            output.capturePhoto(with: settings, delegate: self)
        }
    }

    func photoOutput(_ output: AVCapturePhotoOutput,
                     didFinishProcessingPhoto photo: AVCapturePhoto, error: Error?) {
        lock.lock()
        let continuation = self.continuation
        self.continuation = nil
        lock.unlock()
        guard let continuation else { return }

        if let error { continuation.resume(throwing: error); return }
        guard let data = photo.fileDataRepresentation() else {
            continuation.resume(throwing: FieldCameraError.photoFailed); return
        }
        // The session's own directory is where this ends up, but the move is the caller's job —
        // it is the only thing that knows which session asked.
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("field-\(UUID().uuidString).jpg")
        do {
            try data.write(to: url)
            continuation.resume(returning: url)
        } catch {
            continuation.resume(throwing: error)
        }
    }
}

/// One clip, from `startRecording` to the file it wrote.
///
/// The file arrives after `stopRecording` returns, not with it, so the wait belongs here.
private final class ClipCapture: NSObject, AVCaptureFileOutputRecordingDelegate,
                                @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<URL, Error>?
    private var finished: Result<URL, Error>?

    func fileWhenFinished() async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock()
            if let finished {
                lock.unlock()
                continuation.resume(with: finished)
                return
            }
            self.continuation = continuation
            lock.unlock()
        }
    }

    func fileOutput(_ output: AVCaptureFileOutput,
                    didFinishRecordingTo outputFileURL: URL,
                    from connections: [AVCaptureConnection], error: Error?) {
        // A clip that stopped with an error still has the frames it managed to write, and
        // AVFoundation says so with `finishedRecordingSuccessfully`. Throwing away usable
        // footage because the stop was untidy is how a recording gets lost.
        let salvageable = (error as NSError?)?
            .userInfo[AVErrorRecordingSuccessfullyFinishedKey] as? Bool ?? true
        let result: Result<URL, Error>
        if let error, !salvageable {
            result = .failure(error)
        } else {
            result = .success(outputFileURL)
        }

        lock.lock()
        let continuation = self.continuation
        self.continuation = nil
        self.finished = result
        lock.unlock()
        continuation?.resume(with: result)
    }
}

/// Compares consecutive frames and reports how much changed.
///
/// Frame differencing on a heavily downsampled grey image: crude, cheap, and honest about being
/// crude. It cannot tell a person from a curtain or a passing headlight, which is exactly why the
/// threshold is adjustable and every trigger records what fraction changed.
private final class FrameDelegate: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate,
                                   @unchecked Sendable {
    weak var owner: FieldCameraSession?

    private let lock = NSLock()
    private var continuations: [UUID: AsyncStream<SceneMotionSample>.Continuation] = [:]
    private var previous: [UInt8]?
    private var lastEmitted = Date.distantPast

    /// 32×24 cells. Small enough to compare every frame for free, large enough that somebody
    /// crossing the room lights up several cells.
    private let columns = 32
    private let rows = 24
    /// A cell counts as changed past this much brightness difference — below it is sensor noise
    /// in a dark room, which is most of what a night looks like.
    private let cellChangeThreshold = 18

    func stream() -> AsyncStream<SceneMotionSample> {
        AsyncStream { continuation in
            let id = UUID()
            lock.lock(); continuations[id] = continuation; lock.unlock()
            continuation.onTermination = { [weak self] _ in
                guard let self else { return }
                lock.lock(); continuations[id] = nil; lock.unlock()
            }
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer,
                       from connection: AVCaptureConnection) {
        // Four times a second is plenty to catch somebody walking through, and leaves the CPU
        // to the magnetometer.
        let now = Date()
        guard now.timeIntervalSince(lastEmitted) >= 0.25 else { return }
        guard let grid = Self.brightnessGrid(sampleBuffer, columns: columns, rows: rows) else { return }
        lastEmitted = now

        defer { previous = grid }
        guard let previous, previous.count == grid.count else { return }

        var changed = 0
        for index in 0..<grid.count where abs(Int(grid[index]) - Int(previous[index]))
                                            > cellChangeThreshold {
            changed += 1
        }
        let fraction = Double(changed) / Double(grid.count)

        lock.lock(); let targets = Array(continuations.values); lock.unlock()
        for continuation in targets {
            continuation.yield(SceneMotionSample(at: now, changedFraction: fraction))
        }
    }

    /// Average brightness per cell — the whole frame reduced to a few hundred numbers.
    private static func brightnessGrid(_ sampleBuffer: CMSampleBuffer,
                                       columns: Int, rows: Int) -> [UInt8]? {
        guard let pixels = CMSampleBufferGetImageBuffer(sampleBuffer) else { return nil }
        CVPixelBufferLockBaseAddress(pixels, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixels, .readOnly) }

        guard let base = CVPixelBufferGetBaseAddress(pixels) else { return nil }
        let width = CVPixelBufferGetWidth(pixels)
        let height = CVPixelBufferGetHeight(pixels)
        let bytesPerRow = CVPixelBufferGetBytesPerRow(pixels)
        guard width > columns, height > rows else { return nil }

        let buffer = base.assumingMemoryBound(to: UInt8.self)
        var grid = [UInt8](repeating: 0, count: columns * rows)

        for row in 0..<rows {
            let y = height * row / rows + height / (rows * 2)
            for column in 0..<columns {
                let x = width * column / columns + width / (columns * 2)
                let offset = y * bytesPerRow + x * 4      // BGRA
                // Rough luma. Precision here would be spent on a number that only ever gets
                // compared with itself.
                let blue = Int(buffer[offset])
                let green = Int(buffer[offset + 1])
                let red = Int(buffer[offset + 2])
                grid[row * columns + column] = UInt8((red * 2 + green * 5 + blue) / 8)
            }
        }
        return grid
    }
}

/// The live view, for aiming.
struct CameraPreview: UIViewRepresentable {
    let session: AVCaptureSession

    func makeUIView(context: Context) -> PreviewView {
        let view = PreviewView()
        view.videoPreviewLayer.session = session
        view.videoPreviewLayer.videoGravity = .resizeAspectFill
        return view
    }

    func updateUIView(_ view: PreviewView, context: Context) {
        if view.videoPreviewLayer.session !== session {
            view.videoPreviewLayer.session = session
        }
    }

    final class PreviewView: UIView {
        override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
        var videoPreviewLayer: AVCaptureVideoPreviewLayer {
            layer as! AVCaptureVideoPreviewLayer
        }
    }
}

/// The device being moved, from the accelerometer with gravity already removed — so a phone
/// propped at any angle reads near zero until something actually disturbs it.
final class LiveDeviceMovement: DeviceMovementSource, @unchecked Sendable {
    private let manager = CMMotionManager()

    var isAvailable: Bool { manager.isDeviceMotionAvailable }

    func movements(hz: Double) -> AsyncStream<DeviceMovementSample> {
        AsyncStream { continuation in
            guard manager.isDeviceMotionAvailable else { continuation.finish(); return }
            manager.deviceMotionUpdateInterval = 1 / max(1, hz)
            manager.startDeviceMotionUpdates(to: .main) { motion, _ in
                guard let motion else { return }
                let a = motion.userAcceleration
                let magnitude = (a.x * a.x + a.y * a.y + a.z * a.z).squareRoot()
                continuation.yield(DeviceMovementSample(at: Date(), magnitudeG: magnitude))
            }
            continuation.onTermination = { [weak self] _ in self?.stop() }
        }
    }

    private func stop() { manager.stopDeviceMotionUpdates() }
}
