import Foundation
import Testing
import simd
@testable import BenKit
import BenKitTestSupport

/// What moved, what is missing, what is new — on rooms built by hand, so every answer is known
/// (LiDAR plan, phase 2).
@Suite("Room comparer")
struct RoomComparerTests {

    private func at(_ x: Float, _ z: Float, turned degrees: Float = 0) -> simd_float4x4 {
        var m = simd_float4x4(simd_quatf(angle: degrees * .pi / 180, axis: SIMD3(0, 1, 0)))
        m.columns.3 = SIMD4(x, 0.45, z, 1)
        return m
    }

    private func room(_ objects: [ScanRoomModel.Object]) -> ScanRoomModel {
        ScanRoomModel(surfaces: [], objects: objects)
    }

    private func chair(_ x: Float, _ z: Float, turned: Float = 0) -> ScanRoomModel.Object {
        .init(category: "chair", transform: at(x, z, turned: turned), dimensions: SIMD3(0.45, 0.9, 0.45))
    }

    private func objects(_ before: [ScanRoomModel.Object], _ after: [ScanRoomModel.Object]) -> [ScanChange] {
        RoomComparer.compare(baseline: room(before), baselineMesh: ScanMesh(), comparison: room(after), comparisonMesh: ScanMesh())
    }

    @Test func nothingChangedIsNothing() {
        #expect(objects([chair(0, 0)], [chair(0, 0)]).isEmpty)
    }

    @Test func twoCentimetresIsTheSensorNotTheChair() {
        #expect(objects([chair(0, 0)], [chair(0.02, 0)]).isEmpty)
    }

    @Test func fourCentimetresIsAMove() throws {
        let change = try #require(objects([chair(0, 0)], [chair(0.04, 0)]).first)
        #expect(change.kind == .moved)
        #expect(change.object == "chair")
        #expect(abs((change.distanceCm ?? 0) - 4) < 0.2)
        #expect(change.sentence == "The chair moved 4 cm.")
    }

    @Test func aTurnIsAMoveToo() throws {
        let change = try #require(objects([chair(0, 0)], [chair(0, 0, turned: 30)]).first)
        #expect(change.kind == .moved)
        #expect(abs((change.rotationDeg ?? 0) - 30) < 0.5)
        #expect(change.sentence == "The chair turned 30°.")
        #expect(objects([chair(0, 0)], [chair(0, 0, turned: 4)]).isEmpty)
    }

    @Test func aChairThatIsGoneIsMissing() throws {
        let change = try #require(objects([chair(0, 0)], []).first)
        #expect(change.kind == .missing)
        #expect(change.sizeCm == [45, 90, 45])
        #expect(change.sentence == "The chair is gone.")
    }

    @Test func aChairThatWasNotThereIsNew() throws {
        let change = try #require(objects([], [chair(1, 1)]).first)
        #expect(change.kind == .new)
        #expect(change.where_ == [1, 0.45, 1])
    }

    @Test func eachChairIsMatchedToItsNearestNotToTheFirst() {
        // Two chairs each nudged a centimetre: matched first-come, the first chair would pair with
        // whichever came first and both would read as moved a metre.
        let changes = objects([chair(0, 0), chair(1, 0)], [chair(1.01, 0), chair(0.01, 0)])
        #expect(changes.isEmpty)
    }

    @Test func aChairAcrossTheRoomIsAnotherChair() {
        let changes = objects([chair(0, 0)], [chair(5, 0)])
        #expect(changes.map(\.kind).sorted { $0.rawValue < $1.rawValue } == [.missing, .new])
    }

    // MARK: Surfaces

    private func floor() -> ScanMesh {
        var mesh = ScanMesh()
        mesh.appendBox(transform: .translation(SIMD3(0, -0.005, 0)), size: SIMD3(4, 0.01, 4))
        return mesh
    }

    @Test func aBoxNobodyNamedIsFoundInTheSurfaces() throws {
        var after = floor()
        after.appendBox(transform: .translation(SIMD3(1, 0.2, 1)), size: SIMD3(0.5, 0.4, 0.4))

        let changes = RoomComparer.compare(baseline: room([]), baselineMesh: floor(), comparison: room([]), comparisonMesh: after)
        let change = try #require(changes.first)
        #expect(changes.count == 1)
        #expect(change.kind == .new)
        #expect(change.object == "unclassified")
        #expect(abs(change.where_[0] - 1) < 0.1)
        #expect(change.sentence.hasPrefix("Something is new ("))
    }

    @Test func somethingSmallerThanAShoeboxIsNotAFinding() {
        var after = floor()
        after.appendBox(transform: .translation(SIMD3(1, 0.05, 1)), size: SIMD3(0.12, 0.1, 0.1))
        #expect(RoomComparer.compare(baseline: room([]), baselineMesh: floor(), comparison: room([]), comparisonMesh: after).isEmpty)
    }

    @Test func aCornerTheSecondVisitNeverLookedAtIsNotMissing() {
        var before = floor()
        before.appendBox(transform: .translation(SIMD3(1.5, 0.2, 1.5)), size: SIMD3(0.5, 0.4, 0.4))
        // The second visit saw only the other half of the room.
        var after = ScanMesh()
        after.appendBox(transform: .translation(SIMD3(-1, -0.005, -1)), size: SIMD3(2, 0.01, 2))
        #expect(RoomComparer.compare(baseline: room([]), baselineMesh: before, comparison: room([]), comparisonMesh: after).isEmpty)
    }

    @Test func aMovedChairIsOneChangeNotAChairAGapAndAShape() {
        let before = ScanRoomModel(surfaces: [], objects: [chair(0, 0)])
        let after = ScanRoomModel(surfaces: [], objects: [chair(0.4, 0)])
        var beforeMesh = floor(), afterMesh = floor()
        beforeMesh.appendBox(transform: at(0, 0), size: SIMD3(0.45, 0.9, 0.45))
        afterMesh.appendBox(transform: at(0.4, 0), size: SIMD3(0.45, 0.9, 0.45))

        let changes = RoomComparer.compare(baseline: before, baselineMesh: beforeMesh, comparison: after, comparisonMesh: afterMesh)
        #expect(changes.map(\.kind) == [.moved])
    }

    @Test func pairingIsOptimalNotGreedy() {
        // Greedy would take row 0's cheapest (column 0, cost 1) and leave row 1 with 100.
        let pairs = Hungarian.assign(cost: [[1, 2], [2, 100]], rows: 2, columns: 2)
        #expect(pairs == [1, 0])
        #expect(Hungarian.assign(cost: [[1], [2]], rows: 2, columns: 1) == [0, nil])
    }
}

/// The comparison written into a session, from the stand-in room's two visits.
@Suite("Comparison package")
struct ComparisonPackageTests {

    @Test func theSecondVisitFindsTheChairAndTheBoxWithPictures() throws {
        let store = ScanStore(root: FileManager.default.temporaryDirectory.appendingPathComponent("compare-\(UUID().uuidString)"))
        let start = Date(timeIntervalSince1970: 1_793_221_200)
        let placeId = UUID(), baselineId = UUID()

        let first = try ScanFixture.makeCapture(in: store, room: "Bedroom 1", placeId: placeId, sessionId: nil,
                                                startedAt: start, scannedAt: start)
        let baselineFile = store.packageURL(for: first.id)
        try ScanPackage.write(first, from: store.captureFolder(first.id), retention: .modelAndPictures, to: baselineFile)

        let second = try ScanFixture.makeCapture(in: store, room: "Bedroom 1", placeId: placeId, sessionId: UUID(),
                                                 startedAt: start, scannedAt: start, visit: .chairMovedAndABox)
        let session = store.root.appendingPathComponent("session", isDirectory: true)

        let result = try ComparisonPackage.write(
            capture: second, captureFolder: store.captureFolder(second.id),
            baseline: try ScanPackage.Reader(url: baselineFile), baselineScanId: baselineId, baselineVersion: 2,
            sessionFolder: session)

        #expect(result.relativePath == "scans/compare-001.scan")
        let chair = try #require(result.changes.first { $0.object == "chair" })
        #expect(chair.kind == .moved)
        #expect(abs((chair.distanceCm ?? 0) - 40) < 1)
        #expect(chair.picture?.hasPrefix("scans/compare-001.scan#changes/") == true)
        #expect(result.changes.contains { $0.kind == .new && $0.object == "unclassified" })

        let reader = try ScanPackage.Reader(url: session.appendingPathComponent(result.relativePath))
        #expect(reader.manifest.kind == .comparison)
        #expect(reader.manifest.baseline == .init(placeScanId: baselineId, version: 2))
        #expect(try ComparisonPackage.changes(in: session.appendingPathComponent(result.relativePath)) == result.changes)
        #expect(reader.zip.entries.contains { $0.path.hasPrefix("changes/") && $0.path.hasSuffix(".jpg") })
        #expect(reader.zip.entries.contains { $0.path == "patches.glb" })

        // The next comparison in the same session takes the next number.
        #expect(ComparisonPackage.nextPath(in: session) == "scans/compare-002.scan")
    }
}

/// Finding the room's scan's frame before comparing against it.
@Suite("Relocalization")
struct RelocalizationWatchTests {
    private let start = Date(timeIntervalSince1970: 1_793_221_200)

    @Test func recognizedOnceTrackingComesBackFromRelocalizing() {
        var watch = RelocalizationWatch(startedAt: start)
        watch.note(.relocalizing, at: start.addingTimeInterval(2))
        #expect(watch.outcome == .looking)
        watch.note(.normal, at: start.addingTimeInterval(9))
        #expect(watch.outcome == .recognized)
    }

    @Test func normalWithoutRelocalizingIsNotRecognition() {
        var watch = RelocalizationWatch(startedAt: start)
        watch.note(.normal, at: start.addingTimeInterval(1))
        #expect(watch.outcome == .looking)
    }

    @Test func aRoomNeverRecognizedSaysWhatToDo() {
        var watch = RelocalizationWatch(startedAt: start)
        watch.note(.relocalizing, at: start.addingTimeInterval(30))
        watch.note(.relocalizing, at: start.addingTimeInterval(61))
        #expect(watch.outcome == .failed(RelocalizationWatch.failure))
        #expect(RelocalizationWatch.failure.contains("with the lights as they were"))
    }
}

/// Painting the mesh from the keyframes.
@Suite("Texturer")
struct TexturerTests {

    @Test func theStandInRoomIsPaintedWhereItsPicturesSawIt() throws {
        let store = ScanStore(root: FileManager.default.temporaryDirectory.appendingPathComponent("texture-\(UUID().uuidString)"))
        let start = Date(timeIntervalSince1970: 1_793_221_200)
        let capture = try ScanFixture.makeCapture(in: store, room: "Bedroom 1", placeId: nil, sessionId: nil,
                                                  startedAt: start, scannedAt: start)
        let folder = store.captureFolder(capture.id)
        let contents = try MeshExporter.read(Data(contentsOf: folder.appendingPathComponent("mesh.glb")))

        let uvs = try #require(contents.mesh.uvs)
        #expect(uvs.count == contents.mesh.positions.count)
        #expect(uvs.allSatisfy { $0.x >= 0 && $0.x <= 1 && $0.y >= 0 && $0.y <= 1 })
        #expect(contents.texture?.prefix(3) == Data([0xFF, 0xD8, 0xFF]))
        // Some triangles took their color from a picture rather than the gray square in the corner.
        let gray = SIMD2<Float>(4, 4) / 256
        #expect(uvs.contains { simd_distance($0, gray) > 0.01 })
    }

    @Test func aPointBehindTheCameraIsNotInThePicture() {
        let keyframe = ScanKeyframe(file: "x.jpg", depth: nil, depthWidth: nil, depthHeight: nil,
                                    pose: matrix_identity_float4x4.columnMajor, intrinsics: [100, 0, 0, 0, 100, 0, 50, 50, 1],
                                    imageWidth: 100, imageHeight: 100, at: 0)
        let projection = KeyframeProjection(keyframe)
        #expect(projection.project(SIMD3(0, 0, 1)) == nil)
        let ahead = projection.project(SIMD3(0.1, 0.1, -1))
        #expect(ahead?.depth == 1)
        // Up in the room is up in the picture: a smaller row.
        #expect(ahead.map { $0.pixel.y < 50 && $0.pixel.x > 50 } == true)
    }
}

/// A comparison's readings in the session.
@Suite("Comparison readings")
struct ComparisonReadingTests {

    @Test func aComparisonIsMarkedAndEachChangeIsItsOwnReading() async throws {
        let clock = ManualClock(Date(timeIntervalSince1970: 1_793_221_200))
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("compare-readings-\(UUID().uuidString)").appendingPathComponent("readings.jsonl")
        let log = ReadingLog(fileURL: url)
        let engine = FieldSessionEngine(sessionId: UUID(), log: log, sensors: SensorSuite(), now: clock.nowProvider)
        await engine.beginLogging()

        let scanId = UUID()
        let changes = [
            ScanChange(kind: .moved, object: "chair", distanceCm: 42, rotationDeg: 0, where_: [1.5, 0.45, -2.25],
                       picture: "scans/compare-001.scan#changes/01.jpg"),
            ScanChange(kind: .new, object: "unclassified", sizeCm: [50, 40, 40], where_: [1.2, 0.2, 1.4]),
        ]
        await engine.noteComparison(room: "Bedroom 1", comparisonPath: "scans/compare-001.scan",
                                    placeScanId: scanId, version: 3, changes: changes)
        await engine.stop()

        let readings = try await log.readings()
        #expect(readings.count == 3)
        #expect(readings[0].measurements?["marker"]?.labelValue == "room_compared")
        #expect(readings[0].measurements?["scan"]?.labelValue == "scans/compare-001.scan")
        #expect(readings[0].measurements?["scan"]?.details["changes"] == .number(2))
        #expect(readings[0].note == "Compared Bedroom 1: 2 changed")

        let moved = try #require(readings[1].measurements?["scan_change"])
        #expect(readings[1].measurements?["marker"]?.labelValue == "scan_change")
        #expect(moved.labelValue == "moved")
        #expect(moved.details["distance_cm"] == .number(42))
        #expect(moved.details["where"]?.numbers == [1.5, 0.45, -2.25])
        #expect(moved.details["picture"] == .string("scans/compare-001.scan#changes/01.jpg"))
        #expect(moved.details["place_scan_id"] == .string(scanId.uuidString.lowercased()))
        #expect(readings[1].note == "The chair moved 42 cm.")
        #expect(readings[2].measurements?["scan_change"]?.details["size_cm"]?.numbers == [50, 40, 40])
    }

    @Test func theBundleCarriesTheComparisonsItsReadingsName() {
        let document = Data(#"{"readings":[{"measurements":{"scan":{"value":"scans/compare-001.scan"},"scan_change":{"value":"moved","picture":"scans/compare-002.scan#changes/01.jpg"}}}]}"#.utf8)
        #expect(DeviceDataExporter.comparisonPathsNamed(in: document) == ["scans/compare-001.scan", "scans/compare-002.scan"])
        #expect(DeviceDataExporter.comparisonPathsNamed(in: Data(#"{"x":"scans/compare-001.scan/../../etc"}"#.utf8)).isEmpty)
    }
}
