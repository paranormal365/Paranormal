import Foundation
import Testing
@testable import BenKit

/// Validates what this app EXPORTS against the published JSON Schema, using the schema itself.
///
/// The other suite asserts the keys I intended to write. This one asserts the document a
/// stranger's importer will accept — which is the only claim that matters for a format whose
/// whole purpose is that other people can read it. Hand-checking a schema in my head is exactly
/// how `triggered_by` nearly shipped with invented values: it is a CLOSED enum
/// (`interval | event | manual`), and a document using "heartbeat" or "evp_question" there is
/// rejected outright.
///
/// Runs the repo's own validator (`python3` + `jsonschema`, the snippet printed in
/// `DeviceDataFormatFormat-v1.md`). Skips cleanly where that isn't installed rather than
/// failing, in the same spirit as the opt-in live suite — a fresh checkout still runs green.
@Suite("Device Data Format v1 — the real schema")
struct DeviceDataSchemaTests {

    /// Repo root, found by walking up from this file — the same trick the .NET source-scan
    /// guards use, so the test works from any working directory.
    private static var specDirectory: URL? {
        var directory = URL(fileURLWithPath: #filePath)
        for _ in 0..<10 {
            directory.deleteLastPathComponent()
            let candidate = directory
                .appendingPathComponent("ProjectNotes/specs/device-data-v1.schema.json")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return candidate.deletingLastPathComponent()
            }
        }
        return nil
    }

    /// Returns the validator's complaints, or nil when the tooling isn't available here.
    private func validate(_ document: Data) throws -> [String]? {
        guard let specs = Self.specDirectory else { return nil }

        let scratch = FileManager.default.temporaryDirectory
            .appendingPathComponent("device-data-\(UUID().uuidString).json")
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
            errors = sorted(Draft202012Validator(schema).iter_errors(doc), key=lambda e: list(e.path))
            for error in errors:
                print(f"{list(error.path)}: {error.message}")
            """,
            specs.appendingPathComponent("device-data-v1.schema.json").path,
            scratch.path]

        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = Pipe()
        try process.run()
        let output = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()

        if process.terminationStatus == 99 { return nil }   // no jsonschema installed
        let text = String(decoding: output, as: UTF8.self)
            .split(separator: "\n").map(String.init)
        return text
    }

    /// A session shaped the way the app really writes one: a heartbeat, a sentry event, an EVP
    /// question with audio, a capture, and a reading taken with no GPS fix.
    private func realisticDocument() throws -> Data {
        let start = Date(timeIntervalSince1970: 1_787_600_000)
        let position = FieldReading.Position(
            latitude: 36.1627, longitude: -86.7816,
            elevationMeters: 182.4, accuracyMeters: 35, floor: 2)

        let readings: [FieldReading] = [
            // Heartbeat — an interval record, so a gap means a missed sample.
            FieldReading(at: start, precision: .millisecond, sequence: 1, triggeredBy: .interval,
                         measurements: [
                            "emf": .number(3.1, unit: "uT", accuracy: 0.5, baseline: 3.0),
                            "sound_level": .number(-52.4, unit: "dBFS", baseline: -54.0),
                            "battery": .number(82, unit: "percent"),
                         ],
                         position: position,
                         motion: .init(headingDegrees: 271.5, speedMps: 0, isStationary: true)),

            // Sentry auto-marker — an EVENT record. The kind rides in `marker`, because
            // `triggered_by` may only ever say "event".
            FieldReading(at: start.addingTimeInterval(41.2), precision: .millisecond,
                         sequence: 2, triggeredBy: .event,
                         measurements: [
                            "marker": .label("sentry_emf"),
                            "emf": .number(9.7, unit: "uT", accuracy: 0.5, baseline: 3.0),
                         ],
                         position: position,
                         motion: .init(isStationary: true),
                         note: "field exceeded 20 mG above baseline"),

            // EVP question, tied to the running recording by file + offset.
            FieldReading(at: start.addingTimeInterval(120), precision: .millisecond,
                         sequence: 3, triggeredBy: .manual,
                         measurements: ["marker": .label("evp_question")],
                         position: position,
                         audioRef: .relative("media/audio-001.m4a",
                                             mediaType: "audio/mp4",
                                             startOffsetSeconds: 120,
                                             sha256: String(repeating: "a", count: 64)),
                         note: "Is anyone here with us?"),

            // A capture. Photos are not in v1, so the file is named in the note.
            FieldReading(at: start.addingTimeInterval(180), precision: .millisecond,
                         sequence: 4, triggeredBy: .manual,
                         measurements: ["marker": .label("photo")],
                         position: position,
                         note: "photo: media/photo-001.jpg"),

            // No fix — `position` is absent ENTIRELY, which is the honest thing indoors.
            FieldReading(at: start.addingTimeInterval(240), precision: .millisecond,
                         sequence: 5, triggeredBy: .interval,
                         measurements: ["emf": .number(3.0, unit: "uT", baseline: 3.0)]),
        ]

        let envelope = DeviceDataEnvelope(
            device: .init(manufacturer: "Apple", model: "iPhone17,1",
                          firmwareVersion: "iOS 18.2"),
            session: .init(startedAt: start,
                           endedAt: start.addingTimeInterval(300),
                           batteryPercentAtStart: 82,
                           locationLabel: "Back bedroom, north wall",
                           timezone: "America/Chicago",
                           trigger: .init(mode: .hybrid,
                                          intervalSeconds: 2,
                                          eventDescription: "field exceeds 20 mG above baseline",
                                          debounceSeconds: 3)),
            readings: readings)

        return try DeviceDataJSON.encoder.encode(envelope)
    }

    @Test func whatThisAppWritesValidatesAgainstThePublishedSchema() throws {
        guard let problems = try validate(realisticDocument()) else { return }   // no validator here
        #expect(problems.isEmpty, "schema rejected our export:\n\(problems.joined(separator: "\n"))")
    }

    @Test func theValidatorWouldHaveCaughtAnInventedTriggerValue() throws {
        // Proof this suite discriminates, and a record of the bug it caught before it existed:
        // `triggered_by: "heartbeat"` reads perfectly sensibly and is not allowed.
        guard var document = try JSONSerialization.jsonObject(
            with: try realisticDocument()) as? [String: Any],
              var readings = document["readings"] as? [[String: Any]]
        else { Issue.record("could not reshape the document"); return }

        readings[0]["triggered_by"] = "heartbeat"
        document["readings"] = readings
        let broken = try JSONSerialization.data(withJSONObject: document)

        guard let problems = try validate(broken) else { return }
        #expect(!problems.isEmpty, "the schema should reject an invented triggered_by")
    }

    @Test func theSpecsOwnExampleDecodesThroughOurTypes() throws {
        // The other direction: a document written by somebody ELSE against this spec must load
        // here, unknown keys and all (rule 6).
        guard let specs = Self.specDirectory else { return }
        let example = specs.appendingPathComponent("examples/01-emf-meter-with-audio.json")
        guard let data = try? Data(contentsOf: example) else { return }

        let envelope = try DeviceDataJSON.decoder.decode(DeviceDataEnvelope.self, from: data)
        #expect(envelope.formatVersion == "1.0.0")
        #expect(!envelope.device.manufacturer.isEmpty)
        #expect(!envelope.readings.isEmpty)
        // Readings are oldest first, per the spec's ordering rule.
        let times = envelope.readings.map(\.at)
        #expect(times == times.sorted())
    }
}
