import Foundation
import AVFoundation
import CoreMotion
import UIKit
import SwiftUI
import BenKit

/// The camera as an instrument: a live view you can aim, and a judgement about whether anything
/// in front of it moved.
///
/// A phone left in a corner is useless if you could not see what it was pointing at when you put
/// it down, so the preview exists to be aimed by. The motion detection is deliberately crude —
/// it compares how much of a downsampled frame changed — because anything cleverer would make
/// promises about WHAT moved that a phone in the dark cannot keep.
@MainActor
@Observable
final class FieldCameraSession {

    let session = AVCaptureSession()
    private(set) var isRunning = false
    private(set) var problem: String?

    private let output = AVCaptureVideoDataOutput()
    private let delegate = FrameDelegate()
    private let queue = DispatchQueue(label: "com.ishaunted.field.camera")

    init() {
        delegate.owner = self
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

    private func configureAndRun() {
        guard !isRunning else { return }
        session.beginConfiguration()
        // Low, on purpose: this feed is for aiming and for spotting movement, not for the
        // recording. High resolution here would cost battery all night for nothing.
        session.sessionPreset = .medium

        if session.inputs.isEmpty {
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
        }

        if session.outputs.isEmpty {
            output.alwaysDiscardsLateVideoFrames = true
            output.videoSettings = [
                kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
            ]
            output.setSampleBufferDelegate(delegate, queue: queue)
            if session.canAddOutput(output) { session.addOutput(output) }
        }

        session.commitConfiguration()

        let session = session
        queue.async { session.startRunning() }
        isRunning = true
        problem = nil
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
