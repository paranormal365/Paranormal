import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Writes a complete, realistic `.ben` for walking the phone's review and the website's player by
/// hand and in the browser tests: sound for the whole night, a video inside it, photographs, motion
/// seen while the phone was still, a mark, rooms and a walked path.
///
/// **Written by the app's own code** — the engine on a manual clock and the exporter the Export
/// button uses — so the document, the notes, the offsets and the seal are exactly what a phone
/// writes. A hand-built file would test what somebody believed the format to be.
///
/// Opt-in, because it needs media files the repository does not carry:
///
///     FIELD_TEST_BEN_MEDIA=<dir with audio-001.m4a video-001.mov photo-00{1,2,3}.jpg> \
///     FIELD_TEST_BEN_OUT=<dir> swift test --package-path BenKit --filter TestSessionBundleBuilder
@Suite("Test session bundle (opt-in)")
struct TestSessionBundleBuilder {
    static let media = ProcessInfo.processInfo.environment["FIELD_TEST_BEN_MEDIA"]
    static let out = ProcessInfo.processInfo.environment["FIELD_TEST_BEN_OUT"]

    /// When things happen, in seconds from Start. `long` is for walking by hand; `short` is the
    /// compact fixture the browser tests carry (FIELD_TEST_BEN_PROFILE=short).
    struct Profile {
        var length: Int, stillFrom: Int
        var photos: [Int], motion: [Int], videoEnd: Int, videoLength: Double
        var roomChange: Int, mark: Int, label: String
    }
    static let long = Profile(length: 90, stillFrom: 25, photos: [25, 50, 70], motion: [30, 50],
                              videoEnd: 40, videoLength: 20, roomChange: 45, mark: 60,
                              label: "The Old Mill (test)")
    static let short = Profile(length: 18, stillFrom: 1, photos: [2, 8, 15], motion: [5, 8],
                               videoEnd: 12, videoLength: 8, roomChange: 9, mark: 10,
                               label: "Stage check")
    static var profile: Profile {
        ProcessInfo.processInfo.environment["FIELD_TEST_BEN_PROFILE"] == "short" ? short : long
    }

    @Test(.enabled(if: media != nil && out != nil))
    func writeTheTestSession() async throws {
        let mediaDir = URL(fileURLWithPath: Self.media!, isDirectory: true)
        let outDir = URL(fileURLWithPath: Self.out!, isDirectory: true)
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("bundle-\(UUID().uuidString)")
        let files = SessionFileStore(root: root)
        let id = UUID()
        try files.createDirectories(for: id)

        // Copied in under the names the phone gives them.
        for name in ["audio-001.m4a", "video-001.mov", "photo-001.jpg", "photo-002.jpg", "photo-003.jpg"] {
            try FileManager.default.copyItem(at: mediaDir.appendingPathComponent(name),
                                             to: files.mediaDirectory(for: id).appendingPathComponent(name))
        }

        // An hour ago, so it reads as a real night on the list.
        let start = Date().addingTimeInterval(-3600).rounded(to: 1)
        let clock = ManualClock(start)
        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        let engine = FieldSessionEngine(sessionId: id, log: log, sensors: SensorSuite(),
                                        policy: SamplingPolicy(heartbeatSeconds: 1, debounceSeconds: 3),
                                        channels: .default, now: clock.nowProvider)
        await engine.beginLogging()
        await engine.setBaselines()
        await engine.setRoom("North room")
        await engine.setWatchForMotion(true)
        await engine.setRecording(filename: "media/audio-001.m4a", startedAt: start)

        // The Old Mill test place — near the coordinates the simulator is set to.
        let origin = (lat: 36.16270, lon: -86.78160)
        let p = Self.profile
        for second in 0...p.length {
            let now = clock.advance(by: second == 0 ? 0 : 1)
            let t = Double(second)
            // A slow walk north-east across the building, then standing still from 25 s.
            let walk = min(t, Double(p.stillFrom)) / Double(max(p.stillFrom, 1))
            await engine.ingest(position: PositionSample(
                at: now, latitude: origin.lat + walk * 0.0003, longitude: origin.lon + walk * 0.0002,
                accuracyMeters: 12, courseDegrees: 35))
            await engine.ingest(heading: HeadingSample(at: now, degrees: 35 + t))
            // The field sits near 48 µT with a spike at 1:00.
            let spike = abs(second - p.mark) <= 2 ? 3.0 : 0
            await engine.ingest(magnetic: MagneticFieldSample(at: now, x: 30 + spike, y: 20, z: 30))
            await engine.ingest(audio: AudioLevelSample(at: now, averageDbfs: -52 + (t.truncatingRemainder(dividingBy: 7)),
                                                        peakDbfs: -40))
            // Walking until 25 s, still afterwards.
            await engine.ingest(movement: DeviceMovementSample(at: now, magnitudeG: second < p.stillFrom ? 0.2 : 0.004))

            if let index = p.photos.firstIndex(of: second) {
                await engine.noteCapture(kind: .photo, relativePath: "media/photo-00\(index + 1).jpg")
            }
            if p.motion.contains(second) {
                await engine.ingest(scene: SceneMotionSample(at: now, changedFraction: 0.3))
            }
            if second == p.videoEnd {
                await engine.noteCapture(kind: .video, relativePath: "media/video-001.mov",
                                         durationSeconds: p.videoLength)
            }
            if second == p.roomChange { await engine.setRoom("Stairwell") }
            if second == p.mark { _ = await engine.mark(kind: .manual, note: "Cold spot by the stairs") }
        }
        await engine.setRecording(filename: nil, startedAt: nil)
        await engine.noteCapture(kind: .audio, relativePath: "media/audio-001.m4a",
                                 durationSeconds: Double(p.length))
        await engine.stop()

        let request = DeviceDataExporter.Request(
            sessionId: id, startedAt: start, endedAt: start.addingTimeInterval(Double(p.length)),
            locationLabel: p.label, deviceModel: "iPhone16,2", timezone: "America/Chicago",
            batteryPercentAtStart: 88, trigger: SamplingPolicy.default.trigger(),
            includedMedia: ["media/audio-001.m4a", "media/video-001.mov",
                            "media/photo-001.jpg", "media/photo-002.jpg", "media/photo-003.jpg"],
            deviceId: "TEST-DEVICE")
        try FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)
        let result = try await DeviceDataExporter(files: files).export(request, log: log, to: outDir)
        print("TEST BEN:", result.url.path, result.readingCount, "readings", result.mediaCount, "media")
        #expect(result.mediaCount == 5)
        #expect(result.omittedMedia.isEmpty)
    }
}

private extension Date {
    func rounded(to seconds: TimeInterval) -> Date {
        Date(timeIntervalSince1970: (timeIntervalSince1970 / seconds).rounded(.down) * seconds)
    }
}
