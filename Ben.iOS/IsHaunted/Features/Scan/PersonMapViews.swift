import SwiftUI
import RealityKit
import ARKit
import AVFoundation
import BenKit

/// The person map's switch in the viewfinder (LiDAR plan, phase 4). On a LiDAR phone only: the
/// caller leaves it out everywhere else, absent rather than disabled.
struct FigureOverlayToggle: View {
    @Binding var isOn: Bool
    let shapes: PersonMapCamera.Shapes?

    var body: some View {
        Toggle(isOn: $isOn) {
            VStack(alignment: .leading, spacing: 1) {
                Label("Person map", systemImage: "figure.stand")
                Text("Draws a stick figure over anyone the camera sees, kept with the video. With the room scanned, it also outlines anything person-sized that isn't in the scan.")
                    .font(.caption2).foregroundStyle(Theme.fog)
                if isOn, let note = note {
                    Text(note).font(.caption2).foregroundStyle(Theme.fog)
                        .accessibilityIdentifier("person-map-shapes")
                }
            }
        }
        .tint(Theme.ecto)
        .accessibilityIdentifier("figure-overlay")
    }

    private var note: String? {
        switch shapes {
        case .finding: "Finding this room's scan. Look around slowly."
        case .watching: "Watching for shapes the room's scan doesn't have."
        case .notFound(let why): why
        case .noScan, nil: nil
        }
    }
}

/// What the camera sees while the person map has it: ARKit's own view on a phone, the stand-in's
/// picture on a simulator. Either way the picture fills the frame, turned upright for the screen.
struct PersonMapPreview: View {
    let map: PersonMapCamera

    var body: some View {
        if let session = map.session {
            PersonMapCameraView(session: session)
        } else if let picture = map.fakePicture {
            Image(decorative: picture, scale: 1, orientation: .right)
                .resizable()
                .scaledToFill()
        } else {
            Color.black
        }
    }
}

/// ARKit's camera, drawing nothing over it: the figure and the shapes are SwiftUI's.
private struct PersonMapCameraView: UIViewRepresentable {
    let session: ARSession

    func makeUIView(context: Context) -> ARView {
        let view = ARView(frame: .zero, cameraMode: .ar, automaticallyConfigureSession: false)
        view.session = session
        view.renderOptions.insert(.disableMotionBlur)
        return view
    }

    func updateUIView(_ view: ARView, context: Context) {}
}

/// The stick figure and the outlined shapes, over the live picture (LiDAR plan, phase 4).
struct PersonMapOverlay: View {
    let map: PersonMapCamera

    var body: some View {
        GeometryReader { space in
            let overlay = map.overlay
            let screen = PictureOnScreen(turn: Self.turn(fake: map.session == nil), width: overlay.width,
                                         height: overlay.height, viewWidth: space.size.width,
                                         viewHeight: space.size.height)
            ZStack(alignment: .topLeading) {
                ForEach(Array(overlay.shapes.enumerated()), id: \.offset) { _, shape in
                    UnexplainedShapeLayer(shape: shape, screen: screen)
                }
                if let figure = overlay.figure {
                    FigureOverlayLayer(joints: figure) { screen.point($0) }
                        .accessibilityElement()
                        .accessibilityLabel("A figure is in view")
                        .accessibilityIdentifier("person-map-figure")
                }
            }
            .frame(width: space.size.width, height: space.size.height, alignment: .topLeading)
        }
        .allowsHitTesting(false)
    }

    /// How the preview turns the camera's picture: ARKit's view turns it for the screen; the
    /// stand-in was filmed upright.
    static func turn(fake: Bool) -> PictureTurn {
        if fake { return .clockwise }
        let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first
        switch scene?.effectiveGeometry.interfaceOrientation {
        case .landscapeRight: return .none
        case .landscapeLeft: return .upsideDown
        case .portraitUpsideDown: return .counterclockwise
        default: return .clockwise
        }
    }
}

/// The stick figure: the bones as lines, the joints as dots, the head larger.
struct FigureOverlayLayer: View {
    let joints: [FigurePoint?]
    /// Where a joint is on screen.
    let place: @Sendable (SIMD2<Float>) -> CGPoint

    var body: some View {
        let joints = joints, place = place
        return Canvas { context, _ in
            func at(_ joint: FigureJoint) -> CGPoint? {
                guard joint.rawValue < joints.count, let p = joints[joint.rawValue] else { return nil }
                return place(SIMD2(p.x, p.y))
            }
            var bones = Path()
            for (from, to) in FigureJoint.bones {
                guard let a = at(from), let b = at(to) else { continue }
                bones.move(to: a)
                bones.addLine(to: b)
            }
            context.stroke(bones, with: .color(Theme.ecto), style: StrokeStyle(lineWidth: 3, lineCap: .round, lineJoin: .round))
            for joint in FigureJoint.allCases {
                guard let p = at(joint) else { continue }
                let r: CGFloat = joint == .head ? 7 : 3
                context.fill(Path(ellipseIn: CGRect(x: p.x - r, y: p.y - r, width: r * 2, height: r * 2)), with: .color(Theme.ecto))
            }
        }
    }
}

/// A shape outlined, never jointed: its size and how far away it is.
struct UnexplainedShapeLayer: View {
    let shape: PersonMapPipeline.Overlay.Shape
    let screen: PictureOnScreen

    var body: some View {
        let a = screen.point(SIMD2(Float(shape.rect.minX), Float(shape.rect.minY)))
        let b = screen.point(SIMD2(Float(shape.rect.maxX), Float(shape.rect.maxY)))
        let rect = CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
        ZStack(alignment: .topLeading) {
            Rectangle()
                .stroke(Theme.warning, style: StrokeStyle(lineWidth: 2, dash: [6, 4]))
                .frame(width: rect.width, height: rect.height)
                .offset(x: rect.minX, y: rect.minY)
            Text("\(shape.words) · \(Self.meters(shape.distanceM))")
                .font(.caption2.bold())
                .padding(.horizontal, 5).padding(.vertical, 2)
                .background(.black.opacity(0.6), in: Capsule())
                .foregroundStyle(Theme.warning)
                .offset(x: rect.minX, y: max(0, rect.minY - 20))
        }
        .accessibilityElement()
        .accessibilityLabel("Something \(shape.words), \(Self.meters(shape.distanceM)) away, that isn't in the room's scan")
        .accessibilityIdentifier("person-map-shape")
    }

    static func meters(_ value: Float) -> String { String(format: "%.1f m", value) }
}

// MARK: - Playback

/// The figures of one session, read once for the replay (LiDAR plan, phase 4).
@MainActor
@Observable
final class ReplayFigures {
    private(set) var sightings: [PersonMapFiling.Sighting] = []
    @ObservationIgnored private var entries: [String: [FigureSidecar.Entry]] = [:]
    @ObservationIgnored private var sizes: [String: CGSize] = [:]

    func load(readings: [FieldReading]) {
        sightings = PersonMapFiling.figures(in: readings)
        entries = [:]
    }

    func sightings(in video: String) -> [PersonMapFiling.Sighting] {
        sightings.filter { $0.video == video }
    }

    /// A sighting's joints, read from its file the first time they are wanted.
    func entries(of sighting: PersonMapFiling.Sighting, in folder: URL) -> [FigureSidecar.Entry] {
        if let known = entries[sighting.file] { return known }
        let read = (try? Data(contentsOf: folder.appendingPathComponent(sighting.file)))
            .flatMap { try? FigureSidecar.read($0) } ?? []
        entries[sighting.file] = read
        return read
    }

    /// The video's picture as it plays, upright.
    func uprightSize(of url: URL) async -> CGSize? {
        if let known = sizes[url.path] { return known }
        guard let track = try? await AVURLAsset(url: url).loadTracks(withMediaType: .video).first,
              let natural = try? await track.load(.naturalSize),
              let transform = try? await track.load(.preferredTransform) else { return nil }
        let turned = natural.applying(transform)
        let size = CGSize(width: abs(turned.width), height: abs(turned.height))
        sizes[url.path] = size
        return size
    }
}

/// The figure drawn over the replay's video, from the joints kept with it.
///
/// At the player's own time while it agrees with the replay's playhead, for a figure that moves
/// as smoothly as the video does; otherwise at the playhead, which the replay moves ten times a
/// second whether or not the video is on screen.
struct ReplayFigureOverlay: View {
    let player: AVPlayer
    /// Seconds into the video at the replay's playhead.
    let offset: TimeInterval
    let entries: [[FigureSidecar.Entry]]
    let videoSize: CGSize

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 30)) { _ in
            GeometryReader { space in
                let playing = player.currentTime().seconds
                let t = playing.isFinite && abs(playing - offset) < 0.5 ? playing : offset
                let view = space.size, video = videoSize
                if t.isFinite, let joints = entries.lazy.compactMap({ FigureSidecar.pose(at: t, in: $0) }).first {
                    FigureOverlayLayer(joints: joints) { p in
                        PictureOnScreen.fitted(uprightWidth: video.width, uprightHeight: video.height,
                                               viewWidth: view.width, viewHeight: view.height, p)
                    }
                    .accessibilityElement()
                    .accessibilityLabel("The figure, as the person map saw it")
                    .accessibilityIdentifier("replay-figure")
                }
            }
        }
        .allowsHitTesting(false)
    }
}

/// **Export with the figure drawn**: a copy of the moment the figure was seen, with it burned in,
/// to send on (LiDAR plan, phase 4).
struct ExportWithFigureButton: View {
    let video: URL
    let entries: [FigureSidecar.Entry]

    @State private var working = false
    @State private var copy: URL?
    @State private var problem: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if let copy {
                ShareLink(item: copy) {
                    Label("Share the copy", systemImage: "square.and.arrow.up")
                }
                .accessibilityIdentifier("export-with-figure-share")
            } else {
                Button {
                    Task { await export() }
                } label: {
                    Label(working ? "Drawing the figure…" : "Export with the figure drawn", systemImage: "figure.stand")
                }
                .disabled(working || entries.isEmpty)
                .accessibilityIdentifier("export-with-figure")
            }
            if let problem {
                Text(problem).font(.caption).foregroundStyle(Theme.warning)
            }
        }
        .font(.callout)
    }

    private func export() async {
        working = true
        defer { working = false }
        let scratch = FileManager.default.temporaryDirectory.appendingPathComponent("figure-export", isDirectory: true)
        switch await SessionVideoConverter().drawingFigure(video, entries: entries, into: scratch) {
        case .converted(let url):
            copy = url
            problem = nil
        case .sendOriginal(let reason):
            problem = "The copy couldn't be made: \(reason)."
        }
    }
}
