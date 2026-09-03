#if DEBUG
import Foundation
import BenKit

/// Instruments for a simulator, which has no magnetometer, no barometer and no compass.
///
/// Enabled by the launch argument `-fieldKitFakeSensors`, alongside the existing `-autoSignIn`.
/// Without it there is no way to see a needle move on a Mac, and no way for a UI test to reach
/// any of this — the screens would be verifiable only by hand, on hardware, which is how screens
/// stop being verified at all.
///
/// It plays a scripted night rather than random noise: a quiet room, one clear excursion, and a
/// slow walk. Random data cannot demonstrate a threshold being crossed exactly once.
enum FakeSensors {

    static var isEnabled: Bool {
        ProcessInfo.processInfo.arguments.contains("-fieldKitFakeSensors")
    }

    static func suite() -> SensorSuite {
        SensorSuite(
            magnetometer: DriftingMagnetometer(),
            audio: DriftingAudio(),
            recorder: SilentRecorder(),
            location: WalkingLocation(),
            altitude: DriftingAltimeter(),
            batteryPercent: { 82 })
    }
}

/// Writes a real (if silent) file, so the capture path is exercised end to end rather than
/// mocked away: the session directory, the naming, the size check, the reading that names it.
private final class SilentRecorder: AudioRecording, @unchecked Sendable {
    private let lock = NSLock()
    private var startedAt: Date?
    private var url: URL?

    var isRecording: Bool {
        get async { running() }
    }

    // Locks live in synchronous helpers: Swift 6 refuses an NSLock held across an await.
    private func running() -> Bool {
        lock.lock(); defer { lock.unlock() }
        return startedAt != nil
    }

    private func begin(_ url: URL) {
        lock.lock(); defer { lock.unlock() }
        startedAt = Date()
        self.url = url
    }

    private func finish() -> Date? {
        lock.lock(); defer { lock.unlock() }
        let started = startedAt
        startedAt = nil
        return started
    }

    func beginRecording(to url: URL) async throws {
        // ~2 KB that BEGINS like an M4A: an ISO base-media `ftyp` box, then nothing. Past the
        // "did this produce anything" floor, so the capture is treated as real — and past the
        // server's first-bytes check, which refuses a file whose header is not the kind its name
        // claims. Two kilobytes of zeros used to be refused there as "not an M4A file", which was
        // the server being right about a placeholder. No browser can decode this either; the web
        // player says "won't play" for it, which is the honest answer for a simulator.
        var bytes = Data([0x00, 0x00, 0x00, 0x20])              // box size 32
        bytes.append(contentsOf: Array("ftypM4A ".utf8))         // type + major brand
        bytes.append(contentsOf: [0x00, 0x00, 0x00, 0x00])       // minor version
        bytes.append(contentsOf: Array("M4A mp42isom".utf8))     // compatible brands
        bytes.append(Data(count: 2_048 - bytes.count))
        try bytes.write(to: url)
        begin(url)
    }

    @discardableResult
    func endRecording() async -> TimeInterval {
        // Long enough to clear the empty-recording floor even when a test stops it instantly.
        max(1.0, finish().map { Date().timeIntervalSince($0) } ?? 1.0)
    }
}

/// A quiet 48 uT room that swings to 54 uT — well past a 20 mG report level — for three seconds
/// out of every twenty, then settles.
private struct DriftingMagnetometer: MagnetometerSource {
    var isAvailable: Bool { true }

    func samples(hz: Double) -> AsyncStream<MagneticFieldSample> {
        AsyncStream { continuation in
            let task = Task {
                let interval = 1 / max(1, hz)
                var tick = 0
                while !Task.isCancelled {
                    let phase = Double(tick) * interval
                    let inExcursion = phase.truncatingRemainder(dividingBy: 20) < 3
                    // A little wobble either way, so the needle looks like an instrument
                    // rather than a switch.
                    let wobble = sin(phase * 2.1) * 0.25
                    let value = (inExcursion ? 54.0 : 48.0) + wobble
                    continuation.yield(MagneticFieldSample(
                        at: Date(), x: value, y: 0, z: 0, calibration: .high))
                    tick += 1
                    try? await Task.sleep(for: .seconds(interval))
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }
}

private struct DriftingAudio: AudioLevelSource {
    var isAvailable: Bool { true }

    func levels() -> AsyncStream<AudioLevelSample> {
        AsyncStream { continuation in
            let task = Task {
                var tick = 0
                while !Task.isCancelled {
                    let phase = Double(tick) * 0.1
                    let knock = phase.truncatingRemainder(dividingBy: 31) < 0.6
                    let level = knock ? -30.0 : -52.0 + sin(phase) * 1.5
                    continuation.yield(AudioLevelSample(at: Date(), averageDbfs: level,
                                                        peakDbfs: level + 4))
                    tick += 1
                    try? await Task.sleep(for: .milliseconds(100))
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }
}

/// A slow walk around a property in Nashville, with the poor accuracy a building really gives.
private struct WalkingLocation: LocationSource {
    var isAvailable: Bool { true }

    func authorizationState() async -> LocationAuthorization { .authorized }
    func requestWhenInUse() async -> LocationAuthorization { .authorized }

    func positions() -> AsyncStream<PositionSample> {
        AsyncStream { continuation in
            let task = Task {
                var tick = 0.0
                while !Task.isCancelled {
                    continuation.yield(PositionSample(
                        at: Date(),
                        latitude: 36.1627 + sin(tick / 12) * 0.00025,
                        longitude: -86.7816 + cos(tick / 12) * 0.00025,
                        altitudeMeters: 182,
                        accuracyMeters: 28,     // honest indoor GPS: most of a building
                        speedMps: 0.4))
                    tick += 1
                    try? await Task.sleep(for: .seconds(2))
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    func headings() -> AsyncStream<HeadingSample> {
        AsyncStream { continuation in
            let task = Task {
                var degrees = 0.0
                while !Task.isCancelled {
                    continuation.yield(HeadingSample(at: Date(), degrees: degrees))
                    degrees = (degrees + 7).truncatingRemainder(dividingBy: 360)
                    try? await Task.sleep(for: .seconds(1))
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }
}

private struct DriftingAltimeter: AltitudeSource {
    var isAvailable: Bool { true }

    func relativeAltitudes() -> AsyncStream<RelativeAltitudeSample> {
        AsyncStream { continuation in
            let task = Task {
                var tick = 0.0
                while !Task.isCancelled {
                    continuation.yield(RelativeAltitudeSample(
                        at: Date(), metersSinceStart: sin(tick / 20) * 3))
                    tick += 1
                    try? await Task.sleep(for: .seconds(2))
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }
}
#endif
