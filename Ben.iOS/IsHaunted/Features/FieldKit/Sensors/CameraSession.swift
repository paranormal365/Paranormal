import Foundation
// @preconcurrency: AVCaptureSession predates Sendable and is not marked, but start/stopRunning
// are documented as callable from any thread — hopping them through a queue is exactly what
// Apple's own sample code does. This treats the module's missing annotations as the legacy they
// are instead of decorating every capture with its own unsafe marker.
@preconcurrency import AVFoundation
import CoreMotion
import CoreImage
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
    case photoTimedOut
    case clipFailed(String)

    var errorDescription: String? {
        switch self {
        case .notRunning:
            "The camera isn't running yet. Give it a moment and try again."
        case .noClipRunning:
            "No clip is recording."
        case .photoFailed:
            "The photo came back empty and wasn't saved."
        case .photoTimedOut:
            "The camera didn't answer. Try again, or close the camera and open it again."
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
    /// Whether the camera is running: this session's own capture, or the person map's while it
    /// has the camera (LiDAR plan, phase 4).
    var isRunning: Bool { personMap?.isRunning ?? captureRunning }
    var problem: String? { personMap.map(\.problem) ?? captureProblem }
    private var captureRunning = false
    private var captureProblem: String?

    /// The clip being recorded now, and when it started, so the screen can count it up.
    var clipStartedAt: Date? { personMap.map(\.clipStartedAt) ?? captureClipStartedAt }
    private var captureClipStartedAt: Date?
    var isRecordingClip: Bool { clipStartedAt != nil }
    /// A clip that has the microphone. The whole-session video has none — the session's own
    /// recording keeps the sound — so only this one stands the audio controls down.
    var isRecordingClipWithSound: Bool { isRecordingClip && audioInput != nil }

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
        if let personMap {
            if !personMap.isRunning { personMap.start(motion: motionSink) }
            return
        }
        guard !captureRunning else { return }

        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            configureAndRun()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { [weak self] granted in
                Task { @MainActor in
                    if granted { self?.configureAndRun() }
                    else { self?.captureProblem = "Camera access was declined." }
                }
            }
        default:
            captureProblem = "Camera access is off for this app. Turn it on in Settings to use video."
        }
    }

    /// Stops the camera. A person map that had it stays chosen, and comes back on `start`.
    func stop() {
        personMapStart = nil
        personMap?.stop()
        guard captureRunning else { return }
        let session = session
        queue.async { session.stopRunning() }
        captureRunning = false
    }

    // MARK: - Taking something

    /// Whether a photo can be taken without the shutter sound here.
    ///
    /// Ben, 10/20/2026: "it made the shutter sound when taking a photo ... it would be rude in a
    /// room full of people recording." iOS 18 lets an app turn the sound off for each photo with
    /// `isShutterSoundSuppressionEnabled`, and says through `isShutterSoundSuppressionSupported`
    /// where that is allowed: it is false only in the few countries whose rules require the sound.
    var canTakeSilentPhotos: Bool { photoOutput.isShutterSoundSuppressionSupported }

    /// One photo, written to a file of its own. The caller moves it into the session.
    ///
    /// Silent wherever iOS allows it. Where the local rules require the shutter sound, a photo
    /// somebody takes by hand makes it, as the system decides.
    func capturePhoto() async throws -> URL {
        if let personMap { return try await personMap.still() }
        guard captureRunning else { throw FieldCameraError.notRunning }

        let capture = PhotoCapture()
        photoCaptures.append(capture)
        defer { photoCaptures.removeAll { $0 === capture } }

        let settings = AVCapturePhotoSettings()
        // Setting it where it is not supported raises an exception, so it is asked every time.
        if photoOutput.isShutterSoundSuppressionSupported {
            settings.isShutterSoundSuppressionEnabled = true
        }
        if let connection = photoOutput.connection(with: .video),
           let angle = rotation?.videoRotationAngleForHorizonLevelCapture,
           connection.isVideoRotationAngleSupported(angle) {
            connection.videoRotationAngle = angle
        }
        return try await capture.run(on: photoOutput, settings: settings)
    }

    /// The photo Watch for Motion takes on its own when the camera sees something move.
    ///
    /// A full photo with the sound turned off where iOS allows that. Where it does not, the photo
    /// is the camera's next video frame instead: the session is already filming the room, and a
    /// frame of that video is not a separate shutter press. Smaller than a photo (the size the
    /// video records at), and it makes no sound.
    func captureMotionPhoto() async throws -> URL {
        if let personMap { return try await personMap.still() }
        guard captureRunning else { throw FieldCameraError.notRunning }
        if canTakeSilentPhotos { return try await capturePhoto() }
        let angle = rotation?.videoRotationAngleForHorizonLevelCapture ?? 0
        return try await delegate.nextStill(rotationAngle: angle)
    }

    /// Starts a clip that records the sound as well as the picture.
    ///
    /// The microphone must already have been lent to this by the session — see the type's
    /// remarks. `withSound: false` exists for a session recording no audio at all.
    func startClip(withSound: Bool = true) throws {
        // The person map's clips are picture only, like the session's video always is.
        if let personMap { try personMap.startClip(); return }
        guard captureRunning else { throw FieldCameraError.notRunning }
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
                captureProblem = "The microphone wasn't available, so this clip has no sound."
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
        captureClipStartedAt = Date()
        movieOutput.startRecording(to: url, recordingDelegate: capture)
    }

    /// Ends the clip and hands back the file it wrote.
    func finishClip() async throws -> URL {
        if let personMap { return try await personMap.finishClip() }
        guard let capture = clipCapture else { throw FieldCameraError.noClipRunning }
        defer {
            clipCapture = nil
            captureClipStartedAt = nil
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

    // MARK: - The person map (LiDAR plan, phase 4)

    /// The person map, while it has the camera. ARKit and this session cannot both hold the camera,
    /// so while this is set the session's own capture is stopped, and the clips, photos and motion
    /// above come from the person map's frames: the session's video carries on, with the figure's
    /// joints kept beside it.
    private(set) var personMap: PersonMapCamera?
    var isPersonMapOn: Bool { personMap != nil }
    /// A person map waiting for this session to let the camera go; cleared by `stop`.
    @ObservationIgnored private var personMapStart: UUID?

    /// Hands the camera to the person map. The caller closes any clip first, as it does before
    /// the camera stops.
    @discardableResult
    func turnOnPersonMap() -> PersonMapCamera {
        if let personMap { return personMap }
        delegate.forgetLastFrame()
        let map = PersonMapCamera()
        personMap = map
        let sink = motionSink
        if captureRunning {
            // ARKit starts once this session has let the camera go, not while it still holds it.
            captureRunning = false
            let session = session, token = UUID()
            personMapStart = token
            queue.async { [weak self] in
                session.stopRunning()
                Task { @MainActor [weak self] in
                    guard let self, self.personMapStart == token else { return }
                    self.personMapStart = nil
                    if self.personMap === map, !map.isRunning { map.start(motion: sink) }
                }
            }
        } else {
            map.start(motion: sink)
        }
        return map
    }

    /// Takes the camera back from the person map. The caller closes any clip first.
    func turnOffPersonMap() {
        guard let personMap else { return }
        personMap.stop()
        self.personMap = nil
        delegate.forgetLastFrame()
        configureAndRun()
    }

    /// The person map's brightness grids, into the motion feed this session's own frames go to.
    private var motionSink: @Sendable ([UInt8]) -> Void {
        let delegate = delegate
        return { grid in delegate.note(grid: grid, at: Date()) }
    }

    private func configureAndRun() {
        guard !captureRunning else { return }
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
                captureProblem = "No camera is available on this device."
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
        captureRunning = true
        captureProblem = nil
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
        ) { [weak self] notification in
            // iOS takes the camera from any app that is not on screen. That is not "something
            // else using it", and saying so read as a fault to fix (Ben, 2026-09-17). The reason
            // rides on the notification, so the sentence can be the true one.
            let reason = (notification.userInfo?[AVCaptureSessionInterruptionReasonKey] as? Int)
                .flatMap(AVCaptureSession.InterruptionReason.init(rawValue:))
            Task { @MainActor in
                guard let self else { return }
                self.captureRunning = false
                self.captureProblem = reason == .videoDeviceNotAvailableInBackground
                    ? "The camera pauses while the app is put away, and comes back when you do."
                    : "Something else is using the camera."
            }
        })

        interruptionObservers.keep(centre.addObserver(
            forName: AVCaptureSession.interruptionEndedNotification,
            object: session, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                // Not while the person map has the camera: ARKit took it, and ARKit gives it back.
                guard let self, self.personMap == nil else { return }
                self.captureProblem = nil
                let session = self.session
                self.queue.async { session.startRunning() }
                self.captureRunning = true
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
                    guard self.personMap == nil else { return }
                    let session = self.session
                    self.queue.async { session.startRunning() }
                    self.captureRunning = true
                } else {
                    self.captureRunning = false
                    self.captureProblem = error?.localizedDescription
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

    /// How long a shutter waits before it gives up. A capture that never called back left the
    /// shutter disabled and the button reading "Taking…" for the rest of the night — a freeze,
    /// from the outside (Ben, 2026-09-17).
    private static let patience: TimeInterval = 8

    func run(on output: AVCapturePhotoOutput,
             settings: AVCapturePhotoSettings) async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock(); self.continuation = continuation; lock.unlock()
            output.capturePhoto(with: settings, delegate: self)
            DispatchQueue.global().asyncAfter(deadline: .now() + Self.patience) { [weak self] in
                self?.giveUp()
            }
        }
    }

    /// Answers the waiting shutter with a sentence if the camera never did. Whichever of the
    /// two arrives first takes the continuation; the other finds it gone and does nothing.
    private func giveUp() {
        lock.lock()
        let continuation = self.continuation
        self.continuation = nil
        lock.unlock()
        continuation?.resume(throwing: FieldCameraError.photoTimedOut)
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
/// Frame differencing on a heavily downsampled gray image: crude, cheap, and honest about being
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

    /// Forgets the last frame, so the first from the other camera is not compared with it: the
    /// change of hands would read as the whole room moving.
    func forgetLastFrame() {
        lock.lock(); previous = nil; lock.unlock()
    }

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

    // MARK: - A still from the video

    private var stillWaiter: (continuation: CheckedContinuation<URL, Error>, angle: CGFloat)?
    private static let stillContext = CIContext()

    /// The next frame the camera delivers, written as a JPEG. Silent: no photo is taken.
    func nextStill(rotationAngle: CGFloat) async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock()
            let previous = stillWaiter
            stillWaiter = (continuation, rotationAngle)
            lock.unlock()
            previous?.continuation.resume(throwing: FieldCameraError.photoFailed)
            // A camera that has stopped delivering frames answers with a sentence, not a hang.
            DispatchQueue.global().asyncAfter(deadline: .now() + 3) { [weak self] in
                self?.giveUpOnStill()
            }
        }
    }

    private func giveUpOnStill() {
        lock.lock()
        let waiter = stillWaiter
        stillWaiter = nil
        lock.unlock()
        waiter?.continuation.resume(throwing: FieldCameraError.photoTimedOut)
    }

    private func answerStillIfWanted(_ sampleBuffer: CMSampleBuffer) {
        lock.lock()
        let waiter = stillWaiter
        stillWaiter = nil
        lock.unlock()
        guard let waiter else { return }
        guard let pixels = CMSampleBufferGetImageBuffer(sampleBuffer) else {
            waiter.continuation.resume(throwing: FieldCameraError.photoFailed); return
        }
        let image = CIImage(cvPixelBuffer: pixels).oriented(Self.orientation(for: waiter.angle))
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("field-\(UUID().uuidString).jpg")
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let data = Self.stillContext.jpegRepresentation(of: image, colorSpace: space)
        else {
            waiter.continuation.resume(throwing: FieldCameraError.photoFailed); return
        }
        do {
            try data.write(to: url)
            waiter.continuation.resume(returning: url)
        } catch {
            waiter.continuation.resume(throwing: error)
        }
    }

    /// The back camera's frames arrive sideways; the angle the rotation coordinator gives for a
    /// level picture says which way to turn them.
    private static func orientation(for angle: CGFloat) -> CGImagePropertyOrientation {
        switch Int(angle.rounded()) % 360 {
        case 90: .right
        case 180: .down
        case 270: .left
        default: .up
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer,
                       from connection: AVCaptureConnection) {
        answerStillIfWanted(sampleBuffer)
        // Four times a second is plenty to catch somebody walking through, and leaves the CPU
        // to the magnetometer.
        let now = Date()
        lock.lock(); let last = lastEmitted; lock.unlock()
        guard now.timeIntervalSince(last) >= 0.25 else { return }
        guard let grid = Self.brightnessGrid(sampleBuffer, columns: columns, rows: rows) else { return }
        note(grid: grid, at: now)
    }

    /// One frame's brightness compared with the last: from this session's frames, or from the
    /// person map's while it has the camera.
    func note(grid: [UInt8], at now: Date) {
        lock.lock()
        let previous = self.previous
        self.previous = grid
        lastEmitted = now
        lock.unlock()
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
///
/// From the app's one shared device-motion stream (`SharedDeviceMotion`): its own manager, started
/// in a different frame, stopped the magnetometer reading when Watch for Motion was on (1.1.1).
final class LiveDeviceMovement: DeviceMovementSource, @unchecked Sendable {
    private let motion: SharedDeviceMotion

    init(motion: SharedDeviceMotion = .shared) { self.motion = motion }

    var isAvailable: Bool { motion.isAvailable }

    func movements(hz: Double) -> AsyncStream<DeviceMovementSample> {
        AsyncStream { continuation in
            guard motion.isAvailable else { continuation.finish(); return }
            let id = motion.subscribe(hz: hz) { reading in
                let a = reading.userAcceleration
                let magnitude = (a.x * a.x + a.y * a.y + a.z * a.z).squareRoot()
                continuation.yield(DeviceMovementSample(at: Date(), magnitudeG: magnitude))
            }
            continuation.onTermination = { [motion] _ in motion.unsubscribe(id) }
        }
    }
}
