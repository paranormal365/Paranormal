import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// The phone's replay against the shared rules for what a moment of a session shows.
///
/// The fixtures in `Fixtures/replay-conformance/` are the contract between the phone and the
/// website (`ProjectNotes/specs/replay-semantics.md`): a session document as the phone uploads it,
/// and what each player must show at chosen moments. The website runs the SAME files through its
/// own reader. So "the web player matches the Field Kit" is a test both sides pass, rather than a
/// claim one side makes about the other.
///
/// Each document goes through the path a real one takes on the phone: `SessionImporter.rebuild`
/// for the markers, captures and base levels, the clip lengths the importer reads from the files
/// (given here by the fixture's `durations`, as the files are not in the fixture), then
/// `SessionReplay`.
@Suite("Replay conformance")
@MainActor
struct ReplayConformanceTests {

    nonisolated static let names = ["walk", "media", "interrupted"]

    @Test(arguments: names)
    func theReplayShowsWhatTheRulesSay(_ name: String) async throws {
        let fixture = try Self.load(name)
        let loaded = try await Self.replay(fixture)
        let replay = loaded.replay
        let start = fixture.startedAt

        // The session's bounds (rules S1, S2).
        let bounds = try #require(fixture.json["bounds"] as? [String: Any])
        #expect(ms(replay.timeline.startedAt, from: start) == (bounds["start_ms"] as? NSNumber)?.int64Value,
                "\(name): start")
        #expect(ms(replay.timeline.endedAt, from: start) == (bounds["end_ms"] as? NSNumber)?.int64Value,
                "\(name): end")

        for moment in try #require(fixture.json["moments"] as? [[String: Any]]) {
            let atMs = try #require((moment["at_ms"] as? NSNumber)?.int64Value)
            let at = start.addingTimeInterval(Double(atMs) / 1000)
            let frame = replay.makeFrame(at: at)
            let expect = try #require(moment["expect"] as? [String: Any])
            let here = "\(name) @ \(atMs) ms"

            for (key, value) in expect {
                switch key {
                case "emf_ut":
                    expectNumber(frame.magneticMicrotesla, value, here, key)
                case "emf_base_ut":
                    expectNumber(frame.magneticBaseMicrotesla, value, here, key)
                case "field_mg":
                    expectNumber(frame.magneticDeviationMilligauss(from: replay.timeline.baselines), value, here, key)
                case "sound_dbfs":
                    expectNumber(frame.soundDbfs, value, here, key)
                case "room":
                    #expect(frame.room == value as? String, "\(here): room")
                case "lat":
                    expectNumber(frame.position?.latitude, value, here, key, tolerance: 1e-6)
                case "lon":
                    expectNumber(frame.position?.longitude, value, here, key, tolerance: 1e-6)
                case "accuracy_m":
                    expectNumber(frame.position?.accuracyMeters, value, here, key)
                case "facing":
                    if let expected = value as? [String: Any] {
                        #expect(frame.facing?.what == expected["kind"] as? String, "\(here): facing kind")
                        expectNumber(frame.facing?.degrees, expected["deg"] ?? NSNull(), here, "facing deg", tolerance: 0.5)
                    } else {
                        #expect(frame.facing == nil, "\(here): facing should be none, is \(String(describing: frame.facing))")
                    }
                case "audio":
                    expectClip(frame.activeAudio, value, here, key)
                case "video":
                    expectClip(frame.activeVideo, value, here, key)
                case "motion":
                    #expect((frame.motionDetected != nil) == (value as? Bool), "\(here): motion")
                case "glowing_photo":
                    let glowing = ReplayPhotos.taken(loaded.stills)
                        .last { ReplayPhotos.isGlowing($0, at: at) }?.relativePath
                    #expect(glowing == value as? String, "\(here): glowing photo")
                case "nearest_marker_ms":
                    let nearest = frame.nearestMarker.map { ms($0.at, from: start) }
                    #expect(nearest == (value as? NSNumber)?.int64Value, "\(here): nearest marker")
                default:
                    Issue.record("\(here): the fixture names \(key), which this test does not know")
                }
            }
        }

        // Stepping between markers (rule T4).
        for jump in fixture.json["jumps"] as? [[String: Any]] ?? [] {
            let from = try #require((jump["from_ms"] as? NSNumber)?.int64Value)
            let lands = try #require((jump["lands_ms"] as? NSNumber)?.int64Value)
            let forward = jump["forward"] as? Bool ?? true
            replay.seek(to: start.addingTimeInterval(Double(from) / 1000))
            replay.stepMarker(forward: forward)
            #expect(ms(replay.playhead, from: start) == lands,
                    "\(name): stepping \(forward ? "forward" : "back") from \(from) ms")
        }
    }

    // MARK: - Reading a fixture

    struct Fixture {
        var json: [String: Any]
        var envelope: DeviceDataEnvelope
        var durations: [String: Double]
        var startedAt: Date { envelope.session.startedAt }
    }

    static func load(_ name: String) throws -> Fixture {
        guard let url = Bundle.module.url(forResource: name, withExtension: "json",
                                          subdirectory: "Fixtures/replay-conformance") else {
            throw CocoaError(.fileNoSuchFile, userInfo: [NSFilePathErrorKey: "replay-conformance/\(name).json"])
        }
        let json = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        let document = try JSONSerialization.data(withJSONObject: try #require(json["document"]))
        let envelope = try DeviceDataJSON.decoder.decode(DeviceDataEnvelope.self, from: document)
        let durations = (json["durations"] as? [String: NSNumber] ?? [:]).mapValues(\.doubleValue)
        return Fixture(json: json, envelope: envelope, durations: durations)
    }

    /// The phone's own path from a document to a replay, as `FieldSessionStore` takes it on import.
    static func replay(_ fixture: Fixture) async throws -> (replay: SessionReplay, stills: [CaptureMark]) {
        let readings = fixture.envelope.readings.sorted { $0.at < $1.at }
        let rebuilt = SessionImporter.rebuild(from: readings)

        // A clip noted without its length: the importer reads the length from the file and moves
        // the start back by it. The files are not in the fixture, so their lengths are.
        var captures = rebuilt.captures
        for index in captures.indices
        where captures[index].kind != .photo && captures[index].durationSeconds == nil {
            guard let seconds = fixture.durations[captures[index].relativePath] else { continue }
            captures[index].durationSeconds = seconds
            captures[index].at = captures[index].at.addingTimeInterval(-seconds)
        }

        let media = captures
            .filter { $0.kind != .photo }
            .compactMap { capture -> MediaSegment? in
                guard let duration = capture.durationSeconds, duration > 0 else { return nil }
                return MediaSegment(kind: capture.kind, relativePath: capture.relativePath,
                                    startedAt: capture.at, duration: duration)
            }
        let stills = captures
            .filter { $0.kind == .photo }
            .map { CaptureMark(id: UUID(), at: $0.at, kind: $0.kind, relativePath: $0.relativePath) }

        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("conformance-\(UUID().uuidString)")
            .appendingPathComponent("readings.jsonl")
        let log = ReadingLog(fileURL: url)
        for reading in readings { try await log.append(reading) }
        try await log.close()

        let replay = SessionReplay()
        await replay.load(readingLog: log, markers: rebuilt.markers, media: media,
                          baselines: rebuilt.baselines,
                          startedAt: fixture.envelope.session.startedAt,
                          endedAt: fixture.envelope.session.endedAt)
        return (replay, stills)
    }

    // MARK: - Comparing

    private func ms(_ date: Date, from start: Date) -> Int64 {
        Int64((date.timeIntervalSince(start) * 1000).rounded())
    }

    /// A number within tolerance, or "none" when the fixture says null.
    private func expectNumber(_ actual: Double?, _ expected: Any, _ here: String, _ key: String,
                              tolerance: Double = 1e-6) {
        if let number = expected as? NSNumber {
            guard let actual else {
                Issue.record("\(here): \(key) should be \(number), is none")
                return
            }
            #expect(abs(actual - number.doubleValue) <= tolerance,
                    "\(here): \(key) should be \(number), is \(actual)")
        } else {
            #expect(actual == nil, "\(here): \(key) should be none, is \(String(describing: actual))")
        }
    }

    private func expectClip(_ actual: (segment: MediaSegment, offset: TimeInterval)?, _ expected: Any,
                            _ here: String, _ key: String) {
        if let clip = expected as? [String: Any] {
            #expect(actual?.segment.relativePath == clip["path"] as? String, "\(here): \(key) clip")
            let offset = actual.map { Int64(($0.offset * 1000).rounded()) }
            #expect(offset == (clip["offset_ms"] as? NSNumber)?.int64Value, "\(here): \(key) offset")
        } else {
            #expect(actual == nil, "\(here): \(key) should be none, is \(actual?.segment.relativePath ?? "")")
        }
    }
}
