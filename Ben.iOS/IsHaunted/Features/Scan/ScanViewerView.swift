import SwiftUI
import RealityKit
import CoreMotion
import BenKit

/// Standing inside a scanned room (LiDAR plan, phase 1, §5).
///
/// The room's mesh in RealityKit, seen from eye height in the middle of the room. A drag turns the
/// head; the joystick walks, and stops short of the walls; **Look around** turns the room with the
/// phone itself. A place with several scanned rooms can be walked room to room from the menu.
struct ScanViewerView: View {
    let source: ScanViewerSource
    let flow: ScanFlow

    @Environment(\.dismiss) private var dismiss
    @State private var showing: ScanViewerSource?
    @State private var loaded: LoadedRoom?
    @State private var problem: String?
    @State private var camera = ScanViewerCamera(roomMin: .zero, roomMax: SIMD3(1, 1, 1))
    @State private var lookAround = LookAround()
    @State private var dragStart: CGSize = .zero

    private var current: ScanViewerSource { showing ?? source }

    var body: some View {
        NavigationStack {
            ZStack {
                Theme.ink.ignoresSafeArea()
                if let loaded {
                    roomView(loaded)
                        .gesture(DragGesture()
                            .onChanged { value in
                                let dx = value.translation.width - dragStart.width
                                let dy = value.translation.height - dragStart.height
                                dragStart = value.translation
                                camera.look(yawBy: Float(dx) * 0.005, pitchBy: Float(dy) * 0.005)
                            }
                            .onEnded { _ in dragStart = .zero })
                        .accessibilityElement()
                        .accessibilityLabel("The scanned room, \(current.room)")
                        .accessibilityIdentifier("scan-viewer")
                    controls
                } else if let problem {
                    VStack(spacing: 12) {
                        Text(problem).foregroundStyle(Theme.warning).multilineTextAlignment(.center)
                        Button("Try again") { Task { await load() } }.buttonStyle(.signalSecondary)
                    }
                    .padding(24)
                } else {
                    ProgressView("Fetching the scan of \(current.room)…")
                        .foregroundStyle(Theme.bone)
                }
            }
            .navigationTitle(current.room)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { lookAround.stop(); dismiss() }
                }
                if current.others.count > 1 {
                    ToolbarItem(placement: .primaryAction) {
                        Menu {
                            ForEach(current.others, id: \.1) { name, id in
                                Button(name) {
                                    showing = ScanViewerSource(placeId: current.placeId, scanId: id, room: name, others: current.others)
                                    Task { await load() }
                                }
                            }
                        } label: {
                            Label("Rooms", systemImage: "square.grid.2x2")
                        }
                        .accessibilityIdentifier("scan-viewer-rooms")
                    }
                }
            }
        }
        .task { await load() }
        .onDisappear { lookAround.stop() }
    }

    // MARK: The room

    private func roomView(_ room: LoadedRoom) -> some View {
        RealityView { content in
            content.camera = .virtual
            let model = ModelEntity(mesh: room.mesh, materials: [room.material])
            model.name = "room"
            content.add(model)

            let light = DirectionalLight()
            light.light.intensity = 2_000
            light.look(at: .zero, from: SIMD3(1, 4, 2), relativeTo: nil)
            content.add(light)

            let eye = PerspectiveCamera()
            eye.name = "eye"
            eye.camera.fieldOfViewInDegrees = 70
            eye.transform = Transform(matrix: camera.transform)
            content.add(eye)
        } update: { content in
            if let eye = content.entities.first(where: { $0.name == "eye" }) {
                eye.transform = Transform(matrix: camera.transform)
            }
        }
    }

    private var controls: some View {
        VStack {
            HStack {
                Toggle(isOn: Binding(get: { lookAround.isOn }, set: { on in
                    if on { lookAround.start(from: camera) { yaw, pitch in camera.face(yaw: yaw, pitch: pitch) } }
                    else { lookAround.stop() }
                })) {
                    Label("Look around", systemImage: "gyroscope")
                }
                .toggleStyle(.button)
                .disabled(!lookAround.isAvailable)
                .accessibilityIdentifier("scan-look-around")
                Spacer()
            }
            .padding(12)
            Spacer()
            HStack {
                Joystick { forward, right in camera.walk(forward: forward, right: right) }
                    .accessibilityIdentifier("scan-joystick")
                Spacer()
            }
            .padding(24)
        }
    }

    // MARK: Loading

    private func load() async {
        loaded = nil
        problem = nil
        switch await flow.fetch(placeId: current.placeId, scanId: current.scanId) {
        case .problem(let why):
            problem = why
        case .file(let url):
            do {
                let reader = try ScanPackage.Reader(url: url)
                var contents = try reader.mesh()
                if contents.mesh.triangleCount == 0, let model = try reader.roomModel() {
                    contents = MeshExporter.Contents(mesh: model.boxMesh(), texture: nil)
                }
                let room = try LoadedRoom(contents)
                if let bounds = contents.mesh.bounds {
                    camera = ScanViewerCamera(roomMin: bounds.min, roomMax: bounds.max)
                }
                loaded = room
            } catch {
                problem = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
            }
        }
    }
}

/// A scan's mesh, ready for RealityKit.
struct LoadedRoom {
    let mesh: MeshResource
    let material: any RealityKit.Material

    @MainActor
    init(_ contents: MeshExporter.Contents) throws {
        var descriptor = MeshDescriptor(name: "room")
        descriptor.positions = MeshBuffers.Positions(contents.mesh.positions)
        if contents.mesh.normals.count == contents.mesh.positions.count {
            descriptor.normals = MeshBuffers.Normals(contents.mesh.normals)
        }
        if let uvs = contents.mesh.uvs {
            // glTF's v runs down the picture; RealityKit's runs up.
            descriptor.textureCoordinates = MeshBuffers.TextureCoordinates(uvs.map { SIMD2($0.x, 1 - $0.y) })
        }
        descriptor.primitives = .triangles(contents.mesh.indices)
        mesh = try MeshResource.generate(from: [descriptor])

        if let jpeg = contents.texture, contents.mesh.uvs != nil,
           let image = UIImage(data: jpeg)?.cgImage,
           let texture = try? TextureResource(image: image, options: .init(semantic: .color)) {
            var textured = UnlitMaterial()
            textured.color = .init(texture: .init(texture))
            material = textured
        } else {
            // Both sides drawn: from inside a room every wall is seen from its back.
            var plain = SimpleMaterial(color: .init(white: 0.78, alpha: 1), isMetallic: false)
            plain.faceCulling = .none
            material = plain
        }
    }
}

/// Turns the room with the phone: the change in the phone's heading since this was switched on.
@MainActor
@Observable
final class LookAround {
    private let motion = CMMotionManager()
    private(set) var isOn = false

    var isAvailable: Bool { motion.isDeviceMotionAvailable }

    func start(from camera: ScanViewerCamera, _ apply: @escaping @MainActor (Float, Float) -> Void) {
        guard motion.isDeviceMotionAvailable else { return }
        isOn = true
        var reference: Float?
        let startYaw = camera.yaw
        motion.deviceMotionUpdateInterval = 1.0 / 30
        motion.startDeviceMotionUpdates(using: .xArbitraryZVertical, to: .main) { data, _ in
            guard let q = data?.attitude.quaternion else { return }
            let facing = ScanViewerCamera.facing(attitude: simd_quatd(ix: q.x, iy: q.y, iz: q.z, r: q.w))
            MainActor.assumeIsolated {
                if reference == nil { reference = facing.yaw }
                apply(startYaw + facing.yaw - (reference ?? 0), facing.pitch)
            }
        }
    }

    func stop() {
        motion.stopDeviceMotionUpdates()
        isOn = false
    }
}

/// A thumb pad: drag from the middle to walk, further for faster; let go to stop.
private struct Joystick: View {
    let walk: (_ forward: Float, _ right: Float) -> Void

    @State private var offset: CGSize = .zero
    @State private var mover: Task<Void, Never>?

    private let radius: CGFloat = 52
    /// Meters a second at full tilt — a careful walk.
    private let speed: Float = 1.2

    var body: some View {
        ZStack {
            Circle().fill(.black.opacity(0.45)).frame(width: radius * 2, height: radius * 2)
            Circle().fill(Theme.ecto.opacity(0.85)).frame(width: 40, height: 40).offset(offset)
        }
        .contentShape(Circle())
        .gesture(DragGesture(minimumDistance: 0)
            .onChanged { value in
                let length = max(1, hypot(value.translation.width, value.translation.height))
                let scale = min(1, radius / length)
                offset = CGSize(width: value.translation.width * scale, height: value.translation.height * scale)
                if mover == nil { mover = Task { await move() } }
            }
            .onEnded { _ in
                offset = .zero
                mover?.cancel()
                mover = nil
            })
        .accessibilityElement()
        .accessibilityLabel("Walk")
        .accessibilityHint("Drag up to walk forward, to the sides to step sideways.")
    }

    private func move() async {
        let step: Float = 1.0 / 30
        while !Task.isCancelled {
            let forward = Float(-offset.height / radius) * speed * step
            let right = Float(offset.width / radius) * speed * step
            walk(forward, right)
            try? await Task.sleep(for: .milliseconds(33))
        }
    }
}
