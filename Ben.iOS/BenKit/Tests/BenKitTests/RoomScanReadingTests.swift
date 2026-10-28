import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// A room scanned during a session leaves a reading, and the fields it carries survive being read
/// and written again (LiDAR plan, phase 1; spec 1.2).
@Suite("Room scan reading")
struct RoomScanReadingTests {

    private let start = Date(timeIntervalSince1970: 1_793_221_200)

    @Test func aScannedRoomIsMarkedWithWhatItBecame() async throws {
        let clock = ManualClock(start)
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("room-scan-\(UUID().uuidString)").appendingPathComponent("readings.jsonl")
        let log = ReadingLog(fileURL: url)
        let engine = FieldSessionEngine(sessionId: UUID(), log: log, sensors: SensorSuite(), now: clock.nowProvider)
        await engine.beginLogging()
        await engine.setRoom("Bedroom 1")

        let placeId = UUID(), scanId = UUID()
        await engine.noteRoomScan(room: "Bedroom 1", placeId: placeId, placeScanId: scanId, version: 3,
                                  outcome: "replaced", durationSeconds: 721.6)
        await engine.stop()

        let readings = try await log.readings()
        let reading = try #require(readings.last)
        #expect(reading.triggeredBy == .manual)
        #expect(reading.measurements?["marker"]?.labelValue == "room_scan")
        let scan = try #require(reading.measurements?["room_scan"])
        #expect(scan.labelValue == "Bedroom 1")
        #expect(scan.unit == nil)
        #expect(scan.details["place_scan_id"] == .string(scanId.uuidString.lowercased()))
        #expect(scan.details["place_id"] == .string(placeId.uuidString.lowercased()))
        #expect(scan.details["version"] == .number(3))
        #expect(scan.details["outcome"] == .string("replaced"))
        #expect(scan.details["duration_seconds"] == .number(722))
    }

    @Test func fieldsBesideAValueAreKeptThroughARoundTrip() throws {
        let json = Data("""
        {"value":"moved","object":"chair","distance_cm":42,"where":[1.5,0,-2.25],
         "extra":{"nested":true},"picture":"scans/compare-001.scan#changes/01.jpg"}
        """.utf8)
        let measurement = try JSONDecoder().decode(FieldReading.Measurement.self, from: json)

        #expect(measurement.labelValue == "moved")
        #expect(measurement.details["distance_cm"]?.number == 42)
        #expect(measurement.details["where"]?.numbers == [1.5, 0, -2.25])
        #expect(measurement.details["extra"] == .object(["nested": .bool(true)]))

        let again = try JSONDecoder().decode(FieldReading.Measurement.self, from: JSONEncoder().encode(measurement))
        #expect(again == measurement)
    }

    @Test func anOrdinaryMeasurementIsWrittenExactlyAsBefore() throws {
        let emf = FieldReading.Measurement.number(4.8, unit: "uT", baseline: 0.3)
        let encoder = JSONEncoder()
        encoder.outputFormatting = .sortedKeys
        #expect(String(decoding: try encoder.encode(emf), as: UTF8.self) == #"{"baseline":0.3,"unit":"uT","value":4.8}"#)
    }

    @Test func theFourNewMarksHaveTheirKinds() {
        #expect(MarkerKind(rawValue: "room_scan") == .roomScan)
        #expect(MarkerKind.roomScan.trigger == .manual && !MarkerKind.roomScan.isAutomatic)
        for kind in [MarkerKind.scanChange, .figureSeen, .unexplainedShape] {
            #expect(kind.trigger == .event && kind.isAutomatic, "\(kind)")
        }
        #expect(MarkerKind.figureSeen.title == "Figure seen")
    }
}
