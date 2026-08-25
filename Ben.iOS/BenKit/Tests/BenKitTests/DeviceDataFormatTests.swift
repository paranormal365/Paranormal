import Foundation
import Testing
@testable import BenKit

/// The export format is a PUBLISHED contract (`ProjectNotes/specs/DeviceDataFormat-v1.md`), not
/// an internal convenience. These tests assert the bytes, because a device-data file that only
/// this app can read defeats the entire point of the spec existing.
///
/// The document's own rules are the test names. Where a rule is enforced by the JSON Schema, the
/// companion suite `DeviceDataSchemaTests` runs the real validator over the same bytes.
@Suite("Device Data Format v1 — the wire shape")
struct DeviceDataFormatTests {

    private func encoded(_ value: some Encodable) throws -> [String: Any] {
        let data = try DeviceDataJSON.encoder.encode(value)
        return try #require(
            JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    @Test func keysAreSnakeCaseOnTheWire() throws {
        let reading = FieldReading(
            at: Date(timeIntervalSince1970: 1_787_600_000),
            triggeredBy: .manual,
            position: .init(latitude: 36.16, longitude: -86.78,
                            elevationMeters: 180, accuracyMeters: 35, floor: 2),
            motion: .init(headingDegrees: 271.5, isStationary: true),
            audioRef: .relative("media/audio-001.m4a", startOffsetSeconds: 12.5))

        let json = try encoded(reading)
        #expect(json["triggered_by"] as? String == "manual")
        #expect(json["audio_ref"] != nil)

        let position = try #require(json["position"] as? [String: Any])
        #expect(position["elevation_meters"] as? Double == 180)
        #expect(position["accuracy_meters"] as? Double == 35)

        let motion = try #require(json["motion"] as? [String: Any])
        // Heading lives in motion, not measurements — the schema says where it goes.
        #expect(motion["heading_degrees"] as? Double == 271.5)
        #expect(motion["is_stationary"] as? Bool == true)

        let audio = try #require(json["audio_ref"] as? [String: Any])
        #expect(audio["start_offset_seconds"] as? Double == 12.5)
    }

    @Test func aNumericMeasurementCannotBeBuiltWithoutAUnit() throws {
        // Rule 1: a bare number is not evidence. The type system enforces it — `number(_:unit:)`
        // has no overload without a unit — so this test pins the ENCODED result too.
        let reading = FieldReading(
            at: Date(),
            measurements: ["emf": .number(48.2, unit: "uT", accuracy: 0.5, baseline: 3.0)])

        let measurements = try #require(try encoded(reading)["measurements"] as? [String: Any])
        let emf = try #require(measurements["emf"] as? [String: Any])
        #expect(emf["value"] as? Double == 48.2)
        #expect(emf["unit"] as? String == "uT")
        // 48.2 means nothing until you know the ambient was 3.0.
        #expect(emf["baseline"] as? Double == 3.0)
    }

    @Test func aLabelMeasurementCarriesNoUnitAndThatIsLegal() throws {
        // How marker kinds travel: a string value needs no unit, so `marker` is schema-valid
        // while `triggered_by` stays inside its closed enum.
        let reading = FieldReading(at: Date(), triggeredBy: .manual,
                                   measurements: ["marker": .label("evp_question")])
        let measurements = try #require(try encoded(reading)["measurements"] as? [String: Any])
        let marker = try #require(measurements["marker"] as? [String: Any])
        #expect(marker["value"] as? String == "evp_question")
        #expect(marker["unit"] == nil)
    }

    @Test func absentPositionIsOmittedEntirelyRatherThanWrittenAsZero() throws {
        // Rule 2, and the whole reason it matters: (0, 0) is a real place in the Gulf of Guinea.
        // A reading with no fix must have NO position key at all.
        let reading = FieldReading(at: Date(), triggeredBy: .interval)
        let json = try encoded(reading)
        #expect(json["position"] == nil)
        #expect(json["motion"] == nil)
        #expect(json["audio_ref"] == nil)
        #expect(json["note"] == nil)
        #expect(json["at"] != nil)
    }

    @Test func theClockSaysHowPreciseItIs() throws {
        let json = try encoded(FieldReading(at: Date(), precision: .millisecond))
        #expect(json["precision"] as? String == "millisecond")

        let text = try #require(json["at"] as? String)
        // ISO-8601, UTC, with an offset — never local time without one.
        #expect(text.hasSuffix("Z"))
        #expect(text.contains("T"))
    }

    @Test func aFileRefRefusesPathsThatCouldEscapeTheBundle() {
        // The spec's path rules are a security boundary: an importer expanding a bundle must
        // never be steered outside its own directory. Refusing here means a bad path fails in
        // OUR code rather than at somebody else's importer.
        #expect(FieldReading.FileRef.relative("media/audio-001.m4a") != nil)
        #expect(FieldReading.FileRef.relative("/etc/passwd") == nil)
        #expect(FieldReading.FileRef.relative("../../secrets.json") == nil)
        #expect(FieldReading.FileRef.relative("media/../../out.m4a") == nil)
        #expect(FieldReading.FileRef.relative("media\\audio.m4a") == nil)
        #expect(FieldReading.FileRef.relative("") == nil)
    }

    @Test func aBooleanMeasurementStaysABooleanThroughARoundTrip() throws {
        // JSONDecoder will read `true` as 1 if asked for a Double first, which would turn a
        // motion flag into a unitless number — exactly what the schema rejects.
        let original = FieldReading(
            at: Date(timeIntervalSince1970: 1_787_600_000),
            measurements: ["motion_detected": Measurement(value: .bool(true), unit: nil)])
        let data = try DeviceDataJSON.encoder.encode(original)
        let round = try DeviceDataJSON.decoder.decode(FieldReading.self, from: data)
        #expect(round.measurements?["motion_detected"]?.value == .bool(true))
    }

    @Test func theEnvelopeNamesTheInstrumentAndItsTrigger() throws {
        let envelope = DeviceDataEnvelope(
            device: .init(manufacturer: "Apple", model: "iPhone17,1"),
            session: .init(startedAt: Date(timeIntervalSince1970: 1_787_600_000),
                           batteryPercentAtStart: 82,
                           locationLabel: "Back bedroom, north wall",
                           timezone: "America/Chicago",
                           trigger: .init(mode: .hybrid,
                                          intervalSeconds: 2,
                                          eventDescription: "field exceeds 20 mG above baseline",
                                          debounceSeconds: 3)))

        let json = try encoded(envelope)
        #expect(json["format_version"] as? String == "1.0.0")

        let session = try #require(json["session"] as? [String: Any])
        #expect(session["battery_percent_at_start"] as? Double == 82)
        #expect(session["location_label"] as? String == "Back bedroom, north wall")

        let trigger = try #require(session["trigger"] as? [String: Any])
        // Hybrid: events PLUS a heartbeat, so silence is distinguishable from a dead device.
        #expect(trigger["mode"] as? String == "hybrid")
        #expect(trigger["interval_seconds"] as? Double == 2)
        #expect(trigger["debounce_seconds"] as? Double == 3)
    }
}

private typealias Measurement = FieldReading.Measurement
