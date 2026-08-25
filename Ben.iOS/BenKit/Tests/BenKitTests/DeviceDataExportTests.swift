import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Exporting a session to the published format.
///
/// The claim being tested is that SOMEBODY ELSE can read what this app writes — so the checks
/// run the spec's own JSON Schema over the output and `unzip` over the archive, rather than
/// asserting that our own decoder likes our own encoder.
@Suite("Device data export")
@MainActor
struct DeviceDataExportTests {

    private let start = Date(timeIntervalSince1970: 1_787_600_000)

    private func fixture() async throws -> (DeviceDataExporter, DeviceDataExporter.Request,
                                            ReadingLog, SessionFileStore, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("export-\(UUID().uuidString)", isDirectory: true)
        let files = SessionFileStore(root: root)
        let id = UUID()
        try files.createDirectories(for: id)

        // A real file, so the digest and the archive have something to work on.
        let audio = files.fileURL(for: id, relativePath: "media/audio-001.m4a")
        try Data(repeating: 0xAB, count: 4_096).write(to: audio)

        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        try await log.append(FieldReading(
            at: start, precision: .millisecond, sequence: 1, triggeredBy: .interval,
            measurements: ["emf": .number(48.2, unit: "uT", accuracy: 0.5, baseline: 48.0),
                           "battery": .number(82, unit: "percent")],
            position: .init(latitude: 36.1627, longitude: -86.7816, accuracyMeters: 30),
            motion: .init(headingDegrees: 271.5, isStationary: true)))
        try await log.append(FieldReading(
            at: start.addingTimeInterval(20), precision: .millisecond, sequence: 2,
            triggeredBy: .event,
            measurements: ["marker": .label("sentry_emf"),
                           "emf": .number(53.0, unit: "uT", baseline: 48.0)],
            note: "field moved 50 mG from base"))
        try await log.append(FieldReading(
            at: start.addingTimeInterval(40), precision: .millisecond, sequence: 3,
            triggeredBy: .manual,
            measurements: ["marker": .label("evp_question")],
            audioRef: .relative("media/audio-001.m4a", mediaType: "audio/mp4",
                                startOffsetSeconds: 40),
            note: "Is anyone here?"))
        try await log.close()

        let request = DeviceDataExporter.Request(
            sessionId: id, startedAt: start, endedAt: start.addingTimeInterval(120),
            locationLabel: "Back bedroom, north wall", deviceModel: "iPhone17,1",
            timezone: "America/Chicago", batteryPercentAtStart: 82,
            trigger: SamplingPolicy.default.trigger(),
            includedMedia: ["media/audio-001.m4a"])

        return (DeviceDataExporter(files: files), request, log, files, root)
    }

    /// Runs the repo's own validator. Skips where jsonschema isn't installed.
    private func schemaProblems(_ document: Data) throws -> [String]? {
        var directory = URL(fileURLWithPath: #filePath)
        var schema: URL?
        for _ in 0..<10 {
            directory.deleteLastPathComponent()
            let candidate = directory
                .appendingPathComponent("ProjectNotes/specs/device-data-v1.schema.json")
            if FileManager.default.fileExists(atPath: candidate.path) { schema = candidate; break }
        }
        guard let schema else { return nil }

        let scratch = FileManager.default.temporaryDirectory
            .appendingPathComponent("export-check-\(UUID().uuidString).json")
        try document.write(to: scratch)
        defer { try? FileManager.default.removeItem(at: scratch) }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/env")
        process.arguments = ["python3", "-c", """
            import json, sys
            try:
                from jsonschema import Draft202012Validator
            except ImportError:
                sys.exit(99)
            schema = json.load(open(sys.argv[1]))
            doc = json.load(open(sys.argv[2]))
            for error in Draft202012Validator(schema).iter_errors(doc):
                print(f"{list(error.path)}: {error.message}")
            """, schema.path, scratch.path]

        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = Pipe()
        try process.run()
        let output = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        if process.terminationStatus == 99 { return nil }
        return String(decoding: output, as: UTF8.self)
            .split(separator: "\n").map(String.init)
    }

    @Test func theExportedDocumentValidatesAgainstThePublishedSchema() async throws {
        let (exporter, request, log, _, root) = try await fixture()
        defer { try? FileManager.default.removeItem(at: root) }

        let document = try await exporter.buildDocument(request, log: log)
        guard let problems = try schemaProblems(document) else { return }
        #expect(problems.isEmpty, "schema rejected the export:\n\(problems.joined(separator: "\n"))")
    }

    @Test func readingsAreSplicedInWholeAndInOrder() async throws {
        let (exporter, request, log, _, root) = try await fixture()
        defer { try? FileManager.default.removeItem(at: root) }

        let document = try await exporter.buildDocument(request, log: log)
        let parsed = try #require(
            try JSONSerialization.jsonObject(with: document) as? [String: Any])
        let readings = try #require(parsed["readings"] as? [[String: Any]])

        #expect(readings.count == 3)
        #expect(readings.map { $0["sequence"] as? Int } == [1, 2, 3])
        // Oldest first, which the format requires.
        let times = readings.compactMap { $0["at"] as? String }
        #expect(times == times.sorted())
    }

    @Test func theArchiveIsReadableByAnythingThatReadsZips() async throws {
        let (exporter, request, log, _, root) = try await fixture()
        defer { try? FileManager.default.removeItem(at: root) }

        let result = try await exporter.export(request, log: log, to: root)
        #expect(result.mediaCount == 1)
        #expect(result.readingCount == 3)

        // `unzip -t` is the point: our own reader agreeing with our own writer would prove
        // nothing about whether anybody else can open this.
        let test = Process()
        test.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        test.arguments = ["-t", result.url.path]
        test.standardOutput = Pipe()
        test.standardError = Pipe()
        try test.run()
        test.waitUntilExit()
        #expect(test.terminationStatus == 0, "unzip could not verify the archive")

        // And the document inside is the one we built.
        let listing = Process()
        listing.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        listing.arguments = ["-p", result.url.path, "data.json"]
        let pipe = Pipe()
        listing.standardOutput = pipe
        listing.standardError = Pipe()
        try listing.run()
        let extracted = pipe.fileHandleForReading.readDataToEndOfFile()
        listing.waitUntilExit()

        let parsed = try #require(
            try JSONSerialization.jsonObject(with: extracted) as? [String: Any])
        #expect(parsed["format_version"] as? String == "1.0.0")
        #expect((parsed["readings"] as? [[String: Any]])?.count == 3)
    }

    @Test func anIncludedFileGetsItsDigestStampedIntoTheReadingThatNamesIt() async throws {
        // Audio attached to the wrong reading is worse than no audio, so a reader can check.
        let (exporter, request, log, files, root) = try await fixture()
        defer { try? FileManager.default.removeItem(at: root) }

        let result = try await exporter.export(request, log: log, to: root)

        let listing = Process()
        listing.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        listing.arguments = ["-p", result.url.path, "data.json"]
        let pipe = Pipe()
        listing.standardOutput = pipe
        listing.standardError = Pipe()
        try listing.run()
        let extracted = pipe.fileHandleForReading.readDataToEndOfFile()
        listing.waitUntilExit()

        let parsed = try #require(
            try JSONSerialization.jsonObject(with: extracted) as? [String: Any])
        let readings = try #require(parsed["readings"] as? [[String: Any]])
        let withAudio = try #require(readings.first { $0["audio_ref"] != nil })
        let ref = try #require(withAudio["audio_ref"] as? [String: Any])
        let digest = try #require(ref["sha256"] as? String)

        let expected = try DeviceDataExporter.sha256(
            of: files.fileURL(for: request.sessionId, relativePath: "media/audio-001.m4a"))
        #expect(digest == expected)
        #expect(digest.count == 64)

        // Stamping must not have broken the document.
        guard let problems = try schemaProblems(extracted) else { return }
        #expect(problems.isEmpty, "stamping the digest broke the document:\n\(problems)")
    }

    @Test func leavingAFileOutIsReportedRatherThanSilentlyDroppingIt() async throws {
        // Somebody picks what to hand over. A document that still refers to a file nobody
        // included has to say so, or a reader is left hunting for something that was never sent.
        var (exporter, request, log, _, root) = try await fixture()
        defer { try? FileManager.default.removeItem(at: root) }
        request.includedMedia = []

        let result = try await exporter.export(request, log: log, to: root)
        #expect(result.mediaCount == 0)
        #expect(result.omittedMedia == ["media/audio-001.m4a"])
    }

    @Test func aSessionWithNoReadingsStillProducesAValidDocument() async throws {
        // An empty night is a real outcome — the device was set down and nothing happened.
        let (exporter, request, _, files, root) = try await fixture()
        defer { try? FileManager.default.removeItem(at: root) }

        let emptyLog = ReadingLog(fileURL: files.readingLogURL(for: UUID()))
        let document = try await exporter.buildDocument(request, log: emptyLog)
        let parsed = try #require(
            try JSONSerialization.jsonObject(with: document) as? [String: Any])
        #expect((parsed["readings"] as? [Any])?.isEmpty == true)

        guard let problems = try schemaProblems(document) else { return }
        #expect(problems.isEmpty)
    }
}
