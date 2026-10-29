import Foundation
import Testing
import AVFoundation
import CoreGraphics
import simd
@testable import BenKit
import BenKitTestSupport

/// The figure: lifted, smoothed, cut into sightings and written down only while somebody is there
/// (LiDAR plan, phase 4).
@Suite("Figure tracker")
struct FigureTrackerTests {

    private func pose(_ x: Float, _ y: Float = 0.5) -> [FigurePoint?] {
        FigureJoint.allCases.map { _ in FigurePoint(x: x, y: y, z: 2, c: 1) }
    }

    @Test func aSmoothedJointNeverLeavesWhereTheLastFiveFramesPutIt() {
        var smoother = FigureSmoother()
        var raw: [Float] = []
        // Jumpy on purpose: a hand flickering between two places, and a glitch.
        let xs: [Float] = [0.2, 0.8, 0.25, 0.75, 0.3, 0.9, 0.1, 0.5, 0.95, 0.05, 0.6, 0.4]
        for x in xs {
            raw.append(x)
            let smoothed = smoother.smooth(pose(x))[0]!.x
            let window = raw.suffix(FigureSmoother.frames)
            #expect(smoothed >= window.min()! - 0.0001 && smoothed <= window.max()! + 0.0001,
                    "smoothed \(smoothed) is outside \(Array(window))")
        }
    }

    @Test func aStepIsReachedInFiveFramesAndAGlitchMovesItAFifth() {
        var smoother = FigureSmoother()
        for _ in 0..<5 { _ = smoother.smooth(pose(0.2)) }
        let first = smoother.smooth(pose(0.8))[0]!.x
        #expect(abs(first - (0.2 + 0.6 / 5)) < 0.0001)
        var last = first
        for _ in 0..<4 { last = smoother.smooth(pose(0.8))[0]!.x }
        #expect(abs(last - 0.8) < 0.0001, "after five frames the figure is where the person is")

        var steady = FigureSmoother()
        for _ in 0..<5 { _ = steady.smooth(pose(0.5)) }
        let glitch = steady.smooth(pose(1.0))[0]!.x
        #expect(abs(glitch - 0.6) < 0.0001, "one wild frame moves the figure a fifth of the way")
    }

    @Test func aJointMissingFromSomeFramesIsAveragedOverTheOthers() {
        var smoother = FigureSmoother()
        _ = smoother.smooth(pose(0.4))
        var missing = pose(0.6)
        missing[3] = nil
        let smoothed = smoother.smooth(missing)
        #expect(abs(smoothed[0]!.x - 0.5) < 0.0001)
        #expect(smoothed[3]!.x == 0.4, "a joint missing now is the frames that had it")

        var fresh = FigureSmoother()
        var none = pose(0.5)
        none[3] = nil
        #expect(fresh.smooth(none)[3] == nil)
    }

    @Test func somebodyNewIsNotAveragedWithSomebodyGone() {
        var tracker = FigureTracker()
        let left = FigureJoint.allCases.map { _ in SIMD2<Float>(0.1, 0.5) }
        let right = FigureJoint.allCases.map { _ in SIMD2<Float>(0.9, 0.5) }
        _ = tracker.track(time: 0, joints: left, depth: nil)
        let soon = tracker.track(time: 0.1, joints: right, depth: nil)!
        #expect(abs(soon.joints[0]!.x - 0.5) < 0.0001, "a tenth of a second later it is the same figure")

        var later = FigureTracker()
        _ = later.track(time: 0, joints: left, depth: nil)
        let gone = later.track(time: 1, joints: right, depth: nil)!
        #expect(gone.joints[0]!.x == 0.9, "a second later it is somebody else")
        #expect(later.track(time: 2, joints: nil, depth: nil) == nil)
        #expect(later.track(time: 2, joints: [nil, nil], depth: nil) == nil)
    }

    @Test func aJointsDepthIsTheMedianOfWhatTheSensorTrusted() {
        // A 5 × 5 depth map: the arm at 2 m, the wall behind it at 4 m, one sure-but-wrong 9 m pixel,
        // and the rest low confidence.
        var depth = [Float](repeating: 4, count: 25)
        var confidence = [UInt8](repeating: 0, count: 25)
        for i in [6, 7, 8, 11, 12, 13] { depth[i] = 2; confidence[i] = 2 }
        depth[16] = 9; confidence[16] = 1
        let frame = DepthFrame(width: 5, height: 5, depth: depth, confidence: confidence)

        let lifted = frame.lift(SIMD2(0.5, 0.5))
        #expect(lifted.depth == 2)
        #expect(abs(lifted.share - 7 / 25) < 0.0001)

        let unsure = DepthFrame(width: 5, height: 5, depth: depth, confidence: [UInt8](repeating: 0, count: 25))
        #expect(unsure.lift(SIMD2(0.5, 0.5)).depth == nil, "a depth nobody trusts is no depth")
    }

    @Test func thePinholeCameraFindsThePointItSaw() {
        let camera = PersonMapFixture.camera
        let point = SIMD3<Float>(0.4, 1.1, -0.5)
        let seen = try! #require(camera.project(point))
        let back = camera.unproject(seen.at, depth: seen.depth)
        #expect(simd_distance(back, point) < 0.001)
        #expect(camera.project(SIMD3(0, 1.2, 3)) == nil, "behind the phone is not in the picture")
    }
}

@Suite("Picture turn")
struct PictureTurnTests {

    private func held(right: SIMD3<Float>, up: SIMD3<Float>) -> simd_float4x4 {
        simd_float4x4(columns: (SIMD4(right, 0), SIMD4(up, 0), SIMD4(simd_cross(right, up), 0), SIMD4(0, 0, 0, 1)))
    }

    @Test func theWayThePhoneIsHeldIsTheWayThePictureTurns() {
        #expect(PictureTurn(cameraTransform: held(right: SIMD3(0, -1, 0), up: SIMD3(1, 0, 0))) == .clockwise)
        #expect(PictureTurn(cameraTransform: held(right: SIMD3(0, 1, 0), up: SIMD3(-1, 0, 0))) == .counterclockwise)
        #expect(PictureTurn(cameraTransform: held(right: SIMD3(1, 0, 0), up: SIMD3(0, 1, 0))) == .none)
        #expect(PictureTurn(cameraTransform: held(right: SIMD3(-1, 0, 0), up: SIMD3(0, -1, 0))) == .upsideDown)
        #expect(PictureTurn(cameraTransform: PersonMapFixture.camera.transform) == .clockwise)
    }

    /// The video's own turn and the points the person map keeps must agree, or the figure is
    /// drawn beside the person.
    @Test(arguments: PictureTurn.allCases)
    func theVideosTurnAndTheKeptPointsAgree(_ turn: PictureTurn) {
        let width = 640.0, height = 480.0
        let transform = turn.transform(width: width, height: height)
        let (uw, uh) = turn.upright(width: Int(width), height: Int(height))
        for p in [SIMD2<Float>(0.1, 0.2), SIMD2(0.9, 0.3), SIMD2(0.5, 0.5), SIMD2(0, 1)] {
            let pixel = CGPoint(x: Double(p.x) * width, y: Double(p.y) * height).applying(transform)
            let upright = turn.upright(p)
            #expect(abs(pixel.x / Double(uw) - Double(upright.x)) < 0.0001)
            #expect(abs(pixel.y / Double(uh) - Double(upright.y)) < 0.0001)
            let back = turn.captured(upright)
            #expect(simd_distance(back, p) < 0.0001)
        }
        #expect(PictureTurn(transform: transform) == turn)
    }

    @Test func thePreviewFillsTheViewAsTheCameraViewDoes() {
        // A 640 × 480 picture, upright on a portrait phone, in a 300 × 200 view: 480 wide upright,
        // so it is scaled to 300 across and cropped top and bottom.
        let screen = PictureOnScreen(turn: .clockwise, width: 640, height: 480, viewWidth: 300, viewHeight: 200)
        let middle = screen.uprightPoint(SIMD2(0.5, 0.5))
        #expect(abs(middle.x - 150) < 0.001 && abs(middle.y - 100) < 0.001)
        let topLeft = screen.uprightPoint(SIMD2(0, 0))
        #expect(abs(topLeft.x) < 0.001)
        #expect(abs(topLeft.y - (200 - 400) / 2) < 0.001)
    }
}

@Suite("Figure sidecar")
struct FigureSidecarTests {

    private func folder() -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("sidecar-\(UUID().uuidString)")
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func pose(at t: Double) -> FigurePose {
        FigurePose(time: t, joints: FigureJoint.allCases.map { joint in
            joint == .leftEye ? nil : FigurePoint(x: 0.123456, y: 0.654321, z: 2.345, c: 0.9)
        })
    }

    @Test func anEntryIsWrittenShortAndReadBack() throws {
        let entry = FigureSidecar.Entry(t: 12.4, joints: pose(at: 0).joints)
        let text = FigureSidecar.encode(entry)
        #expect(text.hasPrefix(#"{"t":12.400,"joints":[[0.123,0.654,2.35,0.90],"#))
        #expect(text.contains("null"))
        let read = try FigureSidecar.read(Data("[\(text)]".utf8))
        #expect(read.count == 1)
        #expect(read[0].joints.count == FigureJoint.allCases.count)
        #expect(read[0].joints[FigureJoint.leftEye.rawValue] == nil)
        #expect(read[0].joints[0]?.x == 0.123)
    }

    /// The plan's rule: a clip nobody walked through leaves no file at all.
    @Test func nothingIsWrittenWithNobodyInView() throws {
        let place = folder()
        let sightings = FigureSightings(folder: place, turn: .clockwise)
        for frame in 0..<300 { sightings.note(nil, at: Double(frame) / 30, date: Date()) }
        #expect(sightings.finish().isEmpty)
        #expect(try FileManager.default.contentsOfDirectory(atPath: place.path).isEmpty)
    }

    /// And somebody in view costs about six kilobytes a second, not thirty frames' worth.
    @Test func somebodyInViewCostsAboutSixKilobytesASecond() throws {
        let place = folder()
        let sightings = FigureSightings(folder: place, turn: .clockwise)
        for frame in 0..<(30 * 10) {
            let t = Double(frame) / 30
            sightings.note(pose(at: t), at: t, date: Date())
        }
        let sighting = try #require(sightings.finish().first)
        let size = try #require(try FileManager.default.attributesOfItem(atPath: sighting.file.path)[.size] as? Int)
        #expect(size < 10 * 8_000, "\(size) bytes for ten seconds")
        let entries = try FigureSidecar.read(Data(contentsOf: sighting.file))
        #expect(entries.count <= 10 * Int(FigureSidecar.entriesPerSecond) + 1)
        #expect(entries.count >= 10 * Int(FigureSidecar.entriesPerSecond) - 2)
    }

    @Test func aGapOfTwoSecondsEndsASightingAndAShorterOneDoesNot() throws {
        let place = folder()
        let sightings = FigureSightings(folder: place, turn: .none)
        func run(_ range: ClosedRange<Double>, present: Bool) {
            for t in stride(from: range.lowerBound, through: range.upperBound, by: 1.0 / 30) {
                sightings.note(present ? pose(at: t) : nil, at: t, date: Date(timeIntervalSince1970: t))
            }
        }
        run(0...2, present: true)
        run(2.05...3, present: false)      // a second out of view: the same sighting
        run(3.05...4, present: true)
        run(4.05...7, present: false)      // three seconds: it ended
        run(7.05...8, present: true)
        let all = sightings.finish()
        #expect(all.count == 2)
        #expect(all[0].from == 0)
        #expect(abs(all[0].to - 4) < 0.05)
        #expect(abs(all[1].from - 7.05) < 0.05)
        #expect(all[0].file.lastPathComponent == "figure-001.json")
        #expect(all[1].file.lastPathComponent == "figure-002.json")
        #expect(all[1].startedAt == Date(timeIntervalSince1970: 7.05))
    }

    @Test func theFigureIsInBetweenItsEntriesAndNowhereOutsideThem() {
        let a = FigureSidecar.Entry(t: 1, joints: [FigurePoint(x: 0.2, y: 0.2, z: 2, c: 1)])
        let b = FigureSidecar.Entry(t: 1.1, joints: [FigurePoint(x: 0.4, y: 0.2, z: 3, c: 1)])
        let mid = FigureSidecar.pose(at: 1.05, in: [a, b])?.first ?? nil
        #expect(abs((mid?.x ?? 0) - 0.3) < 0.0001)
        #expect(abs((mid?.z ?? 0) - 2.5) < 0.0001)
        #expect(FigureSidecar.pose(at: 0.5, in: [a, b]) == nil)
        #expect(FigureSidecar.pose(at: 2, in: [a, b]) == nil)
    }
}

@Suite("Shape watcher")
struct ShapeWatcherTests {

    private let room = PersonMapFixture.baseline
    private let camera = PersonMapFixture.camera

    /// The room's points as the stand-in phone's depth sees them, with whatever solids are in it.
    private func points(_ solids: [(min: SIMD3<Float>, max: SIMD3<Float>, shade: Float)]) -> [SIMD3<Float>] {
        let depth = PersonMapFixture.raycast(solids: solids)
        return camera.scaled(toWidth: depth.frame.width, height: depth.frame.height).points(from: depth.frame, step: 1)
    }

    private var furniture: [(min: SIMD3<Float>, max: SIMD3<Float>, shade: Float)] {
        [PersonMapFixture.box(center: SIMD3(0.5, 0.375, -1), size: SIMD3(1.2, 0.75, 0.8), shade: 0),
         PersonMapFixture.box(center: SIMD3(-0.4, 0.45, -0.6), size: SIMD3(0.45, 0.9, 0.45), shade: 0)]
    }

    private func watch(_ solids: [(min: SIMD3<Float>, max: SIMD3<Float>, shade: Float)], seconds: Double,
                       figure: [SIMD3<Float>] = []) -> (outlines: [ShapeWatcher.Outline], shapes: [ShapeSighting]) {
        let watcher = ShapeWatcher(baseline: room)
        let seen = points(solids)
        var last: [ShapeWatcher.Outline] = []
        for frame in 0...Int(seconds * 10) {
            last = watcher.note(points: seen, camera: camera.position, figure: figure,
                                at: Double(frame) / 10, date: Date())
        }
        return (last, watcher.finish())
    }

    @Test func theRoomAsScannedHasNoShapeInIt() {
        let result = watch(furniture, seconds: 3)
        #expect(result.outlines.isEmpty)
        #expect(result.shapes.isEmpty, "the room's own walls, floor and furniture are not shapes: \(result.shapes.map(\.words))")
    }

    @Test func somethingPersonSizedThatStaysASecondIsAShape() throws {
        let box = PersonMapFixture.box(center: PersonMapFixture.boxCenter, size: PersonMapFixture.boxSize, shade: 0)
        let result = watch(furniture + [box], seconds: 2)
        #expect(result.outlines.count == 1)
        let shape = try #require(result.shapes.first)
        #expect(result.shapes.count == 1)
        // The sensor sees its front: as wide as it is, nearly as tall (the bottom ten centimeters
        // are the floor's), and only a little deep.
        #expect(abs(shape.sizeCm.x - 45) <= 10)
        #expect(shape.sizeCm.y >= 140 && shape.sizeCm.y <= 165)
        #expect(abs(shape.distanceM - 2.5) < 0.4)
        #expect(simd_distance(SIMD2(shape.center.x, shape.center.z), SIMD2(0.7, -0.2)) < 0.3)
        #expect(shape.words == "\(Int(shape.sizeCm.x.rounded())) × \(Int(shape.sizeCm.y.rounded())) × \(Int(shape.sizeCm.z.rounded())) cm")
    }

    @Test func somethingThereForHalfASecondIsNot() {
        let box = PersonMapFixture.box(center: PersonMapFixture.boxCenter, size: PersonMapFixture.boxSize, shade: 0)
        let result = watch(furniture + [box], seconds: 0.5)
        #expect(result.outlines.isEmpty)
        #expect(result.shapes.isEmpty)
    }

    @Test func aShoeboxIsNotPersonSized() {
        let shoebox = PersonMapFixture.box(center: SIMD3(0.7, 0.1, 0.2), size: SIMD3(0.3, 0.2, 0.15), shade: 0)
        let low = PersonMapFixture.box(center: SIMD3(-0.8, 0.3, 0.4), size: SIMD3(0.9, 0.6, 0.5), shade: 0)
        #expect(watch(furniture + [shoebox, low], seconds: 2).shapes.isEmpty, "wide but not tall is not a shape")
    }

    @Test func aFigureInViewExplainsItsOwnShapeAndOneLostInTheDarkDoesNot() {
        let joints = PersonMapFixture.personJoints(at: 2)
        let root = joints[FigureJoint.root.rawValue]
        let person = PersonMapFixture.box(center: SIMD3(root.x, 0.875, root.z), size: SIMD3(0.45, 1.75, 0.25), shade: 0)
        #expect(watch(furniture + [person], seconds: 2, figure: joints).shapes.isEmpty,
                "the person body detection found is drawn as a figure, not called a shape")
        #expect(watch(furniture + [person], seconds: 2).shapes.count == 1,
                "the same person with no figure found — in the dark — is a shape")
    }
}

@Suite("Person map pipeline")
struct PersonMapPipelineTests {

    /// The stand-in's whole script through the pipeline, as the simulator runs it: a clip that
    /// keeps the video, one figure with its joints file and picture, and one shape.
    @Test func theStandInsScriptLeavesAVideoAFigureAndAShape() async throws {
        let pipeline = PersonMapPipeline()
        await pipeline.watchShapes(against: PersonMapFixture.baseline)
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("person-map-\(UUID().uuidString)")
        await pipeline.startClip(in: folder)

        var sawFigure = false, sawShape = false
        let start = Date(timeIntervalSince1970: 1_793_221_200)
        for frame in 0..<Int(PersonMapFixture.period * 10) {
            let t = Double(frame) / 10
            let shot = try #require(PersonMapFixture.shot(at: t, date: start.addingTimeInterval(t)))
            let overlay = await pipeline.process(shot.frame)
            if overlay.figure != nil { sawFigure = true }
            if !overlay.shapes.isEmpty { sawShape = true }
        }
        #expect(sawFigure && sawShape, "the screen draws both while they are there")

        let clip = try #require(try await pipeline.finishClip())
        let asset = AVURLAsset(url: clip.video)
        let duration = try await asset.load(.duration).seconds
        #expect(abs(duration - (PersonMapFixture.period - 0.1)) < 0.3)
        let track = try #require(try await asset.loadTracks(withMediaType: .video).first)
        #expect(PictureTurn(transform: try await track.load(.preferredTransform)) == .clockwise,
                "the phone was upright, so the video plays upright")

        let figure = try #require(clip.figures.first)
        #expect(clip.figures.count == 1)
        // In view from when a hand reaches the edge of the picture to when the last one leaves it.
        #expect(abs(figure.from - PersonMapFixture.walk.lowerBound) < 0.4)
        #expect(abs(figure.to - PersonMapFixture.walk.upperBound) < 0.4)
        #expect(figure.startedAt.timeIntervalSince(start) > 0.7)
        #expect(figure.peakConfidence > 0.5)
        let entries = try FigureSidecar.read(Data(contentsOf: figure.file))
        #expect(!entries.isEmpty)
        // Upright, the person stands: head above feet.
        let head = try #require(entries[entries.count / 2].joints[FigureJoint.head.rawValue])
        let foot = try #require(entries[entries.count / 2].joints[FigureJoint.leftFoot.rawValue])
        #expect(head.y < foot.y)
        #expect(FileManager.default.fileExists(atPath: try #require(figure.picture).path))

        let shape = try #require(clip.shapes.first)
        #expect(clip.shapes.count == 1)
        #expect(abs(shape.from - PersonMapFixture.box.lowerBound) < 0.3)
        #expect(shape.picture != nil)
    }

    @Test func withoutTheRoomsScanThereAreNoShapes() async throws {
        let pipeline = PersonMapPipeline()
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("person-map-\(UUID().uuidString)")
        await pipeline.startClip(in: folder)
        for frame in 0..<Int(PersonMapFixture.period * 5) {
            let t = Double(frame) / 5
            _ = await pipeline.process(try #require(PersonMapFixture.shot(at: t)).frame)
        }
        let clip = try #require(try await pipeline.finishClip())
        #expect(clip.shapes.isEmpty)
        #expect(clip.figures.count == 1)
    }

    @Test func noClipMeansNothingKept() async throws {
        let pipeline = PersonMapPipeline()
        for frame in 0..<20 {
            _ = await pipeline.process(try #require(PersonMapFixture.shot(at: 1.5 + Double(frame) / 10)).frame)
        }
        #expect(try await pipeline.finishClip() == nil)
        #expect(await pipeline.still() != nil, "a photo can still be taken from the person map's camera")
    }
}

@Suite("Person map in the session")
struct PersonMapSessionTests {

    @Test func aClipsFiguresAndShapesAreFiledAndRead() async throws {
        let session = FileManager.default.temporaryDirectory.appendingPathComponent("pm-session-\(UUID().uuidString)")
        let clipFolder = FileManager.default.temporaryDirectory.appendingPathComponent("pm-clip-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: clipFolder, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: session.appendingPathComponent("figures"), withIntermediateDirectories: true)
        // A figure already filed from an earlier video: the numbers carry on after it.
        try Data("[]".utf8).write(to: session.appendingPathComponent("figures/figure-001.json"))

        let joints = clipFolder.appendingPathComponent("figure-001.json")
        try Data("[]".utf8).write(to: joints)
        let picture = clipFolder.appendingPathComponent("figure-001.jpg")
        try Data([0xFF, 0xD8, 0xFF]).write(to: picture)
        let shapePicture = clipFolder.appendingPathComponent("shape-001.jpg")
        try Data([0xFF, 0xD8, 0xFF]).write(to: shapePicture)
        let start = Date(timeIntervalSince1970: 1_793_221_200)
        let clip = PersonMapPipeline.Clip(
            folder: clipFolder, video: clipFolder.appendingPathComponent("video.mov"),
            figures: [FigureSighting(file: joints, picture: picture, from: 1, to: 5, startedAt: start, peakConfidence: 0.8)],
            shapes: [ShapeSighting(from: 7.5, to: 11, startedAt: start.addingTimeInterval(7.5),
                                   sizeCm: SIMD3(45, 150, 5), distanceM: 2.46, center: SIMD3(0.7, 0.85, -0.35),
                                   picture: shapePicture)])

        let filed = PersonMapFiling.move(clip, into: session)
        #expect(filed.figures.map(\.file) == ["figures/figure-002.json"])
        #expect(filed.figures.first?.picture == "figures/figure-002.jpg")
        #expect(filed.shapes.first?.picture == "figures/shape-001.jpg")
        #expect(FileManager.default.fileExists(atPath: session.appendingPathComponent("figures/figure-002.jpg").path))
        #expect(!FileManager.default.fileExists(atPath: clipFolder.path), "the clip's own folder is cleared")

        // The readings, as the engine writes them.
        let clock = ManualClock(start.addingTimeInterval(20))
        let log = ReadingLog(fileURL: session.appendingPathComponent("readings.jsonl"))
        let engine = FieldSessionEngine(sessionId: UUID(), log: log, sensors: SensorSuite(), now: clock.nowProvider)
        await engine.beginLogging()
        let figure = try #require(filed.figures.first)
        await engine.noteFigure(file: figure.file, video: "media/video-001.mov", from: figure.from, to: figure.to,
                                peakConfidence: figure.peakConfidence, picture: figure.picture, at: figure.startedAt)
        let shape = try #require(filed.shapes.first)
        await engine.noteShape(words: shape.sighting.words, sizeCm: shape.sighting.sizeCm,
                               distanceM: Double(shape.sighting.distanceM), center: shape.sighting.center,
                               video: "media/video-001.mov", from: shape.sighting.from, to: shape.sighting.to,
                               picture: shape.picture, at: shape.sighting.startedAt)
        await engine.stop()

        let readings = try await log.readings()
        #expect(readings.count == 2)
        let seen = try #require(readings[0].measurements?["figure"])
        #expect(readings[0].at == start, "dated to when the figure was first in view, not when it was filed")
        #expect(readings[0].measurements?["marker"]?.labelValue == "figure_seen")
        #expect(seen.labelValue == "figures/figure-002.json")
        #expect(seen.details["video"] == .string("media/video-001.mov"))
        #expect(seen.details["from"] == .number(1) && seen.details["to"] == .number(5))
        #expect(seen.details["peak_confidence"] == .number(0.8))
        #expect(seen.details["picture"] == .string("figures/figure-002.jpg"))
        #expect(readings[0].note == "In view for 4 seconds.")
        #expect(readings[0].measurements?["emf"] == nil, "nothing of the room as it is now")

        let outlined = try #require(readings[1].measurements?["shape"])
        #expect(readings[1].measurements?["marker"]?.labelValue == "unexplained_shape")
        #expect(outlined.labelValue == "45 × 150 × 5 cm")
        #expect(outlined.details["size_cm"]?.numbers == [45, 150, 5])
        #expect(outlined.details["distance_m"] == .number(2.5))
        #expect(outlined.details["where"]?.numbers == [0.7, 0.85, -0.35])
        #expect(readings[1].note == "45 × 150 × 5 cm, 2.5 m away, not in the room's scan.")

        // Read back for the replay.
        let sightings = PersonMapFiling.figures(in: readings)
        #expect(sightings == [PersonMapFiling.Sighting(file: "figures/figure-002.json", video: "media/video-001.mov",
                                                       from: 1, to: 5, at: start)])
    }

    /// A figure is written when its video is filed but dated to when it was first seen. The
    /// document must still run in time order, or the server refuses the whole session.
    @Test func aFigureWrittenLateStillLandsInTimeOrder() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("pm-order-\(UUID().uuidString)")
        let files = SessionFileStore(root: folder)
        let sessionId = UUID()
        try files.createDirectories(for: sessionId)
        let start = Date(timeIntervalSince1970: 1_793_221_200)
        let clock = ManualClock(start)
        let log = ReadingLog(fileURL: files.readingLogURL(for: sessionId))
        let engine = FieldSessionEngine(sessionId: sessionId, log: log, sensors: SensorSuite(), now: clock.nowProvider)
        await engine.beginLogging()
        for second in [10.0, 20, 30] {
            clock.set(start.addingTimeInterval(second))
            await engine.mark(kind: .manual, note: "at \(Int(second))")
        }
        await engine.noteFigure(file: "figures/figure-001.json", video: "media/video-001.mov", from: 1, to: 5,
                                peakConfidence: 0.9, picture: nil, at: start.addingTimeInterval(12))
        await engine.stop()

        let request = DeviceDataExporter.Request(
            sessionId: sessionId, startedAt: start, endedAt: start.addingTimeInterval(60), locationLabel: nil,
            deviceModel: "iPhone16,1", timezone: nil, batteryPercentAtStart: nil,
            trigger: .init(mode: .event, eventDescription: "test"), includedMedia: [])
        let document = try await DeviceDataExporter(files: files).buildDocument(request, log: log)
        let envelope = try DeviceDataJSON.decoder.decode(DeviceDataEnvelope.self, from: document)
        let times = envelope.readings.map(\.at)
        #expect(times == times.sorted(), "readings must run in time order")
        #expect(envelope.readings[1].measurements?["figure"] != nil, "the figure sits where it was seen")
    }

    /// A video cut short to send keeps its figures, moved onto the cut video's clock: the figure
    /// seen 12 seconds into the recording is 2 seconds into a copy that starts at 10.
    @Test func aCutVideosFiguresAreMovedOntoItsClock() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("pm-cut-\(UUID().uuidString)")
        let files = SessionFileStore(root: root)
        let sessionId = UUID()
        let folder = files.directory(for: sessionId).appendingPathComponent("figures")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let joints: [FigurePoint?] = [FigurePoint(x: 0.5, y: 0.2, z: 2, c: 1)]
        let writer = try FigureSidecar.Writer(url: folder.appendingPathComponent("figure-001.json"))
        for t in stride(from: 8.0, through: 16.0, by: 0.5) { writer.append(FigureSidecar.Entry(t: t, joints: joints)) }
        writer.finish()

        let document = Data("""
            {"readings":[
              {"at":"2026-10-28T21:00:12Z","measurements":{"figure":{"from":12,"to":16,"picture":"figures/figure-001.jpg","value":"figures/figure-001.json","video":"media/video-001.mov"}}},
              {"at":"2026-10-28T21:00:30Z","measurements":{"shape":{"distance_m":2.4,"from":30.5,"size_cm":[45,150,5],"to":40,"value":"45 × 150 × 5 cm","video":"media/video-001.mov","where":[0.7,0.85,-0.2]}}},
              {"at":"2026-10-28T21:00:50Z","measurements":{"figure":{"from":3,"to":4,"value":"figures/figure-002.json","video":"media/video-002.mov"}}}
            ]}
            """.utf8)
        let scratch = root.appendingPathComponent("scratch")
        let moved = DeviceDataExporter.rebasePersonMap(forVideo: "media/video-001.mov", from: 10, duration: 25,
                                                        in: document, files: files, sessionId: sessionId, into: scratch)

        let root2 = try #require(try JSONSerialization.jsonObject(with: moved.document) as? [String: Any])
        let readings = try #require(root2["readings"] as? [[String: Any]])
        func channel(_ i: Int, _ name: String) -> [String: Any] {
            ((readings[i]["measurements"] as? [String: Any])?[name] as? [String: Any]) ?? [:]
        }
        #expect(channel(0, "figure")["from"] as? Double == 2)
        #expect(channel(0, "figure")["to"] as? Double == 6)
        #expect(channel(1, "shape")["from"] as? Double == 20.5)
        #expect(channel(1, "shape")["to"] as? Double == 25, "past the end of the cut is its end")
        #expect(channel(1, "shape")["size_cm"] as? [Double] == [45, 150, 5], "nothing else in it changes")
        #expect(channel(2, "figure")["from"] as? Double == 3, "another video's figure is left alone")

        let copy = try #require(moved.substitutes["figures/figure-001.json"])
        let entries = try FigureSidecar.read(Data(contentsOf: copy))
        #expect(entries.first?.t == 0, "the frames before the cut are left out")
        #expect(entries.last?.t == 6)
        #expect(moved.substitutes["figures/figure-002.json"] == nil)
        // The session's own joints file is untouched.
        let original = try FigureSidecar.read(Data(contentsOf: folder.appendingPathComponent("figure-001.json")))
        #expect(original.first?.t == 8)
    }

    @Test func theBundleCarriesAVideosFiguresAndLeavesOutOnesForVideosNotSent() throws {
        let document = Data("""
            {"readings":[
              {"measurements":{"figure":{"value":"figures/figure-001.json","video":"media/video-001.mov","picture":"figures/figure-001.jpg"}}},
              {"measurements":{"shape":{"value":"45 × 150 × 5 cm","video":"media/video-001.mov","picture":"figures/shape-001.jpg"}}},
              {"measurements":{"figure":{"value":"figures/figure-002.json","video":"media/video-002.mov"}}},
              {"measurements":{"figure":{"value":"figures/../../etc/passwd","video":"media/video-001.mov"}}}
            ]}
            """.utf8)
        #expect(DeviceDataExporter.figurePathsNamed(in: document, withVideos: ["media/video-001.mov"])
                == ["figures/figure-001.json", "figures/figure-001.jpg", "figures/shape-001.jpg"])
        #expect(DeviceDataExporter.figurePathsNamed(in: document, withVideos: []).isEmpty)
    }
}

@Suite("Export with the figure drawn")
struct FigureExportTests {

    /// A portrait video of the stand-in room, the figure burned in, and a frame read back the way a
    /// player shows it: the figure is where the person was, the right way up.
    @Test func theFigureIsDrawnWhereThePersonIsTheRightWayUp() async throws {
        let scratch = FileManager.default.temporaryDirectory.appendingPathComponent("burn-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
        let writer = try VideoClipWriter(url: scratch.appendingPathComponent("video.mov"), width: 640, height: 480,
                                         turn: .clockwise, averageBitRate: 2_000_000)
        for frame in 0..<60 {
            let shot = try #require(PersonMapFixture.shot(at: 6 + Double(frame) / 15))   // nobody walking through
            writer.append(shot.frame.image, at: Double(frame) / 15)
        }
        let video = try await writer.finish()

        // A figure standing in the upper left of the upright picture, for the whole second second.
        let joints: [FigurePoint?] = FigureJoint.allCases.map { _ in nil }
        var standing = joints
        standing[FigureJoint.head.rawValue] = FigurePoint(x: 0.25, y: 0.2, z: 2, c: 1)
        standing[FigureJoint.neck.rawValue] = FigurePoint(x: 0.25, y: 0.3, z: 2, c: 1)
        standing[FigureJoint.root.rawValue] = FigurePoint(x: 0.25, y: 0.45, z: 2, c: 1)
        let entries = stride(from: 1.0, through: 2.0, by: 1.0 / 15).map { FigureSidecar.Entry(t: $0, joints: standing) }

        let outcome = await SessionVideoConverter().drawingFigure(video, entries: entries, into: scratch)
        guard case .converted(let copy) = outcome else {
            Issue.record("no copy: \(outcome)")
            return
        }
        let generator = AVAssetImageGenerator(asset: AVURLAsset(url: copy))
        generator.appliesPreferredTrackTransform = true
        generator.requestedTimeToleranceBefore = .zero
        generator.requestedTimeToleranceAfter = .zero
        // The copy starts three seconds before the figure, or at the start: one and a half seconds in.
        let (image, _) = try await generator.image(at: CMTime(seconds: 1.5, preferredTimescale: 600))
        #expect(image.width < image.height, "the copy plays upright, as the video does")

        func violet(at x: Double, _ y: Double) -> Bool {
            let px = Int(x * Double(image.width)), py = Int(y * Double(image.height))
            var pixel = [UInt8](repeating: 0, count: 4)
            let context = CGContext(data: &pixel, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                                    space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
            context.draw(image, in: CGRect(x: -Double(px), y: -Double(image.height - 1 - py),
                                           width: Double(image.width), height: Double(image.height)))
            return pixel[2] > 180 && pixel[1] < 150 && pixel[0] < 180
        }
        #expect(violet(at: 0.25, 0.25), "the neck line is drawn where the figure stands")
        #expect(!violet(at: 0.75, 0.75), "and not turned to the other corner")
    }
}
