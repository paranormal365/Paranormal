import Foundation
import Testing
import simd
@testable import BenKit

/// A room's `.scan`: written from a capture folder, read back, and kept to the names the server
/// accepts (LiDAR plan, phase 1).
@Suite("Room scan package")
struct ScanPackageTests {

    private func store() -> ScanStore {
        ScanStore(root: FileManager.default.temporaryDirectory
            .appendingPathComponent("scans-\(UUID().uuidString)", isDirectory: true))
    }

    private let start = Date(timeIntervalSince1970: 1_793_221_200)

    private func capture(_ store: ScanStore, placeId: UUID? = UUID()) throws -> ScanCapture {
        try ScanFixture.makeCapture(in: store, room: "Bedroom 1", placeId: placeId, sessionId: UUID(),
                                    startedAt: start, scannedAt: start.addingTimeInterval(720))
    }

    /// The names the server's `PlaceScanManifest` accepts, and no others.
    private static func isAllowed(_ path: String) -> Bool {
        ["manifest.json", "mesh.glb", "room.usdz", "room.json", "worldmap.arworldmap", "preview.jpg",
         "keyframes/index.json"].contains(path)
            || path.range(of: #"^keyframes/\d{4}\.(jpg|depth)$"#, options: .regularExpression) != nil
    }

    @Test func aScanWithItsPicturesReadsBackAsWhatWasScanned() throws {
        let store = store()
        let placeId = UUID()
        let capture = try capture(store, placeId: placeId)
        let file = store.packageURL(for: capture.id)

        let written = try ScanPackage.write(capture, from: store.captureFolder(capture.id),
                                            retention: .modelAndPictures, to: file)
        let reader = try ScanPackage.Reader(url: file)

        #expect(written == (try FileManager.default.attributesOfItem(atPath: file.path)[.size] as? NSNumber)?.int64Value)
        #expect(reader.manifest.format == "ishaunted-scan/1")
        #expect(reader.manifest.kind == .baseline)
        #expect(reader.manifest.room == "Bedroom 1")
        #expect(reader.manifest.placeId == placeId)
        #expect(reader.manifest.scannedAt == start.addingTimeInterval(720))
        #expect(reader.manifest.retention == .modelAndPictures)
        #expect(reader.manifest.entries.keyframes == 3)
        #expect(reader.manifest.entries.mesh == "mesh.glb")
        #expect(reader.manifest.stats?.floorAreaM2 == 20)
        // The manifest goes first, so a reader finds what the file is before anything else.
        #expect(reader.zip.entries.first?.path == "manifest.json")
        #expect(reader.zip.entries.allSatisfy { Self.isAllowed($0.path) })

        let mesh = try reader.mesh().mesh
        #expect(mesh.triangleCount == capture.triangleCount)
        #expect(try reader.keyframes().map(\.file) == ["0001.jpg", "0002.jpg", "0003.jpg"])
        #expect(try reader.roomModel()?.objects.map(\.category) == ["table", "chair"])
    }

    @Test func aModelOnlyScanLeavesThePicturesOut() throws {
        let store = store()
        let capture = try capture(store)
        let file = store.packageURL(for: capture.id)

        try ScanPackage.write(capture, from: store.captureFolder(capture.id), retention: .modelOnly, to: file)
        let reader = try ScanPackage.Reader(url: file)

        #expect(reader.manifest.retention == .modelOnly)
        #expect(reader.manifest.entries.keyframes == 0)
        #expect(!reader.zip.entries.contains { $0.path.hasPrefix("keyframes/") })
        #expect(try reader.keyframes().isEmpty)
    }

    @Test func theSizesShownBeforeSendingAreTheSizesSent() throws {
        let store = store()
        let capture = try capture(store)
        let folder = store.captureFolder(capture.id)

        for retention in ScanManifest.Retention.allCases {
            let file = folder.appendingPathComponent("\(retention.rawValue).scan")
            let actual = try ScanPackage.write(capture, from: folder, retention: retention, to: file)
            let estimated = ScanPackage.estimatedBytes(of: folder, retention: retention)
            // Within the manifest's few hundred bytes either way: the number beside a choice is
            // for deciding, and a person deciding between 112 MB and 31 MB is not misled by 1 KB.
            #expect(abs(estimated - actual) < 2_048, "\(retention): estimated \(estimated), wrote \(actual)")
        }
        #expect(ScanPackage.estimatedBytes(of: folder, retention: .modelOnly)
                < ScanPackage.estimatedBytes(of: folder, retention: .modelAndPictures))
    }

    @Test func aFolderWithoutAMeshIsNotPacked() throws {
        let store = store()
        let capture = try capture(store)
        let folder = store.captureFolder(capture.id)
        try FileManager.default.removeItem(at: folder.appendingPathComponent("mesh.glb"))

        #expect(throws: ScanPackage.PackageError.missingMesh) {
            try ScanPackage.write(capture, from: folder, retention: .modelAndPictures,
                                  to: folder.appendingPathComponent("room.scan"))
        }
    }

    @Test func aCaptureIsKeptUntilItIsForgotten() throws {
        let store = store()
        let capture = try capture(store)

        #expect(store.capture(capture.id) == capture)
        #expect(store.pending().map(\.id) == [capture.id])
        #expect(capture.durationSeconds == 720)

        store.remove(capture.id)
        #expect(store.capture(capture.id) == nil)
        #expect(store.pending().isEmpty)
    }

    @Test func aFileThatIsNotAScanIsRefusedInWords() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).scan")
        try Data("not a zip".utf8).write(to: file)
        #expect(throws: ScanPackage.PackageError.self) { _ = try ScanPackage.Reader(url: file) }
    }
}

/// The mesh as glTF binary, and the arithmetic that builds it.
@Suite("Room scan mesh")
struct MeshExporterTests {

    @Test func aMeshSurvivesTheRoundTripThroughGlb() throws {
        var mesh = ScanMesh()
        mesh.appendBox(transform: .translation(SIMD3(1, 2, 3)), size: SIMD3(2, 1, 4))

        let data = MeshExporter.glb(mesh)
        #expect(data.prefix(4) == Data("glTF".utf8))
        #expect(data.count % 4 == 0)

        let read = try MeshExporter.read(data).mesh
        #expect(read.positions == mesh.positions)
        #expect(read.normals == mesh.normals)
        #expect(read.indices == mesh.indices)
        #expect(read.triangleCount == 12)
        #expect(read.bounds?.min == SIMD3(0, 1.5, 1))
        #expect(read.bounds?.max == SIMD3(2, 2.5, 5))
    }

    @Test func aTexturedMeshCarriesItsPicture() throws {
        var mesh = ScanMesh()
        mesh.appendBox(transform: matrix_identity_float4x4, size: SIMD3(1, 1, 1))
        mesh.uvs = mesh.positions.map { SIMD2($0.x + 0.5, $0.y + 0.5) }
        let jpeg = try ScanFixture.jpeg(width: 8, height: 8, shade: 0.5)

        let read = try MeshExporter.read(MeshExporter.glb(mesh, texture: jpeg))
        #expect(read.mesh.uvs == mesh.uvs)
        #expect(read.texture == jpeg)
    }

    @Test func weldingMergesTheSeamsBetweenChunks() {
        // Two triangles sharing an edge, as two ARKit chunks hand them over: four corners each
        // side of the seam, written twice.
        var mesh = ScanMesh()
        let normal = SIMD3<Float>(0, 1, 0)
        mesh.append(positions: [SIMD3(0, 0, 0), SIMD3(1, 0, 0), SIMD3(0, 0, 1)],
                    normals: [normal, normal, normal], indices: [0, 1, 2], transform: matrix_identity_float4x4)
        mesh.append(positions: [SIMD3(1, 0, 0), SIMD3(1, 0, 1), SIMD3(0, 0, 1)],
                    normals: [normal, normal, normal], indices: [0, 1, 2], transform: matrix_identity_float4x4)

        let welded = mesh.welded()
        #expect(mesh.positions.count == 6)
        #expect(welded.positions.count == 4)
        #expect(welded.triangleCount == 2)
    }

    @Test func aChunkIsMovedIntoTheScansFrame() {
        var mesh = ScanMesh()
        let turn = simd_float4x4(simd_quatf(angle: .pi / 2, axis: SIMD3(0, 1, 0)))
        var moved = turn
        moved.columns.3 = SIMD4(10, 0, 0, 1)
        mesh.append(positions: [SIMD3(1, 0, 0)], normals: [SIMD3(1, 0, 0)], indices: [], transform: moved)

        #expect(simd_distance(mesh.positions[0], SIMD3(10, 0, -1)) < 1e-5)
        #expect(simd_distance(mesh.normals[0], SIMD3(0, 0, -1)) < 1e-5)
    }

    @Test func somethingThatIsNotGlbIsRefused() {
        #expect(throws: MeshExporter.GLBError.notGLB) { _ = try MeshExporter.read(Data(repeating: 0, count: 64)) }
    }
}

/// When the scanner keeps a picture: every half metre or fifteen degrees, only while tracking is good.
@Suite("Keyframe policy")
struct KeyframePolicyTests {

    private func pose(x: Float = 0, turned degrees: Float = 0) -> simd_float4x4 {
        var m = simd_float4x4(simd_quatf(angle: degrees * .pi / 180, axis: SIMD3(0, 1, 0)))
        m.columns.3 = SIMD4(x, 1.5, 0, 1)
        return m
    }

    @Test func theFirstGoodFrameIsKept() {
        var policy = KeyframePolicy()
        do { let kept = policy.shouldKeep(pose: pose(), trackingIsNormal: true); #expect(kept) }
        #expect(policy.kept == 1)
    }

    @Test func standingStillKeepsNothingMore() {
        var policy = KeyframePolicy()
        _ = policy.shouldKeep(pose: pose(), trackingIsNormal: true)
        do { let kept = policy.shouldKeep(pose: pose(x: 0.49), trackingIsNormal: true); #expect(!kept) }
        do { let kept = policy.shouldKeep(pose: pose(turned: 14), trackingIsNormal: true); #expect(!kept) }
    }

    @Test func halfAMetreOrFifteenDegreesKeepsOne() {
        var policy = KeyframePolicy()
        _ = policy.shouldKeep(pose: pose(), trackingIsNormal: true)
        do { let kept = policy.shouldKeep(pose: pose(x: 0.5), trackingIsNormal: true); #expect(kept) }
        // Measured from the last one kept, not from the first.
        do { let kept = policy.shouldKeep(pose: pose(x: 0.9), trackingIsNormal: true); #expect(!kept) }
        do { let kept = policy.shouldKeep(pose: pose(x: 0.9, turned: 16), trackingIsNormal: true); #expect(kept) }
        #expect(policy.kept == 3)
    }

    @Test func aFrameTakenWhileTrackingIsUnsureIsNeverKept() {
        var policy = KeyframePolicy()
        do { let kept = policy.shouldKeep(pose: pose(), trackingIsNormal: false); #expect(!kept) }
        #expect(policy.kept == 0)
    }

    @Test func noMoreThanTheServerKeeps() {
        var policy = KeyframePolicy(mostKeyframes: 2)
        do { let kept = policy.shouldKeep(pose: pose(x: 0), trackingIsNormal: true); #expect(kept) }
        do { let kept = policy.shouldKeep(pose: pose(x: 1), trackingIsNormal: true); #expect(kept) }
        do { let kept = policy.shouldKeep(pose: pose(x: 2), trackingIsNormal: true); #expect(!kept) }
        #expect(KeyframePolicy().mostKeyframes == 500)
    }

    @Test func aTurnIsMeasuredWhicheverWayItGoes() {
        #expect(abs(KeyframePolicy.degreesBetween(pose(turned: 10), pose(turned: -20)) - 30) < 0.01)
        #expect(abs(KeyframePolicy.degreesBetween(pose(turned: 170), pose(turned: -170)) - 20) < 0.01)
    }
}

/// A keyframe's depth map: half precision, zlib, unknown where ARKit was unsure.
@Suite("Depth codec")
struct DepthCodecTests {

    @Test func depthSurvivesAtHalfPrecision() throws {
        let meters: [Float] = [0.25, 1.0, 2.3456, 4.999, 9.75]
        let back = try DepthCodec.decode(DepthCodec.encode(meters))
        for (a, b) in zip(meters, back) {
            // Half precision keeps about three significant digits: a few millimeters at five meters.
            #expect(abs(a - b) <= a * 0.001)
        }
    }

    @Test func whatIsNotAMeasurementBecomesUnknown() throws {
        let back = try DepthCodec.decode(DepthCodec.encode([.nan, .infinity, -1, 0]))
        #expect(back == [0, 0, 0, 0])
    }

    @Test func anEmptyWallCompresses() throws {
        let wall = [Float](repeating: 2.5, count: 256 * 192)
        #expect(try DepthCodec.encode(wall).count < 1_024)
    }
}

/// The viewer's camera: standing in the room, looking, and walking without leaving it.
@Suite("Scan viewer camera")
struct ScanViewerCameraTests {

    private func camera() -> ScanViewerCamera {
        ScanViewerCamera(roomMin: SIMD3(-2, 0, -2.5), roomMax: SIMD3(2, 2.5, 2.5))
    }

    @Test func itStartsInTheMiddleAtEyeHeight() {
        let c = camera()
        #expect(c.position == SIMD3(0, 1.6, 0))
        #expect(simd_distance(c.forward, SIMD3(0, 0, -1)) < 1e-5)
    }

    @Test func walkingForwardGoesTheWayItFaces() {
        var c = camera()
        c.walk(forward: 1, right: 0)
        #expect(simd_distance(c.position, SIMD3(0, 1.6, -1)) < 1e-5)

        c.look(yawBy: .pi / 2, pitchBy: 0)       // a quarter turn left
        c.walk(forward: 1, right: 0)
        #expect(simd_distance(c.position, SIMD3(-1, 1.6, -1)) < 1e-5)

        c.walk(forward: 0, right: 1)             // facing −x, the right hand is −z
        #expect(simd_distance(c.position, SIMD3(-1, 1.6, -2)) < 1e-5)
    }

    @Test func lookingUpDoesNotWalkIntoTheCeiling() {
        var c = camera()
        c.look(yawBy: 0, pitchBy: 1)
        c.walk(forward: 1, right: 0)
        #expect(c.position.y == 1.6)
    }

    @Test func aWalkStopsShortOfTheWall() {
        var c = camera()
        c.walk(forward: 100, right: 100)
        #expect(c.position.z == -2.5 + ScanViewerCamera.wallMargin)
        #expect(c.position.x == 2 - ScanViewerCamera.wallMargin)
    }

    @Test func aPhoneHeldUprightLooksAtTheHorizonAndTurningLeftAddsYaw() {
        // Upright in portrait: the back camera points along the horizon.
        let upright = simd_quatd(angle: .pi / 2, axis: SIMD3(1, 0, 0))
        let level = ScanViewerCamera.facing(attitude: upright)
        #expect(abs(level.pitch) < 1e-5)

        // The same phone turned a quarter left about the vertical.
        let turned = simd_quatd(angle: .pi / 2, axis: SIMD3(0, 0, 1)) * upright
        let left = ScanViewerCamera.facing(attitude: turned)
        var delta = left.yaw - level.yaw
        if delta < -.pi { delta += 2 * .pi }
        #expect(abs(delta - .pi / 2) < 1e-4)

        // Tipped back to look up.
        let up = ScanViewerCamera.facing(attitude: simd_quatd(angle: .pi / 2 + 0.3, axis: SIMD3(1, 0, 0)))
        #expect(abs(up.pitch - 0.3) < 1e-4)
    }

    @Test func nobodyLooksPastStraightUp() {
        var c = camera()
        c.look(yawBy: 0, pitchBy: 3)
        #expect(c.pitch == ScanViewerCamera.mostPitch)
        c.face(yaw: 7, pitch: -3)
        #expect(c.pitch == -ScanViewerCamera.mostPitch)
        #expect(abs(c.yaw - (7 - 2 * .pi)) < 1e-4)
    }
}
