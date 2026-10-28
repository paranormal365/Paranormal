import SwiftUI
import RealityKit
import ARKit
import BenKit

/// Scanning one room: the camera with the room taking shape over it, RoomPlan's coaching, how much
/// has been found, and Done (LiDAR plan, phase 1).
///
/// On a simulator started with `-scanFakeRoom` there is no sensor, so a stand-in room is "scanned"
/// instead and everything after Done — what to keep, the upload, the viewer — runs for real.
struct RoomScanView: View {
    let room: String
    let placeId: UUID?
    let sessionId: UUID
    let scans: ScanStore
    let onFinished: (ScanCapture) -> Void
    let onCancel: () -> Void

    @State private var scanner: RoomScanner?
    @State private var captureId = UUID()
    @State private var fakeProgress: Double = 0
    @State private var finishing = false
    @State private var problem: String?
    @State private var startedAt = Date()

    private var coverage: Double { scanner?.coverage ?? fakeProgress }

    var body: some View {
        ZStack {
            Theme.ink.ignoresSafeArea()
            if let scanner {
                ScanCameraView(session: scanner.arSession).ignoresSafeArea()
            } else {
                VStack(spacing: 12) {
                    Image(systemName: "cube.transparent")
                        .font(.system(size: 56))
                        .foregroundStyle(Theme.ecto)
                    Text(ScanFlow.usesFakeRoom
                         ? "Scanning a stand-in room — this simulator has no LiDAR."
                         : LidarSupport.reason)
                        .font(.callout)
                        .foregroundStyle(Theme.fog)
                        .multilineTextAlignment(.center)
                }
                .padding(32)
            }

            VStack {
                VStack(spacing: 4) {
                    Text(room).font(.headline).foregroundStyle(Theme.bone)
                    Text(scanner?.instruction ?? "Walk the room slowly. Point at every wall, the floor, and what's in the room.")
                        .font(.callout)
                        .foregroundStyle(Theme.bone)
                        .multilineTextAlignment(.center)
                }
                .padding(12)
                .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: Theme.Radius.card))
                .padding(.top, 12)

                Spacer()

                if let problem {
                    Text(problem)
                        .font(.callout)
                        .foregroundStyle(Theme.warning)
                        .padding(10)
                        .background(.black.opacity(0.6), in: RoundedRectangle(cornerRadius: Theme.Radius.small))
                }

                HStack(spacing: 20) {
                    Button("Cancel") {
                        scanner?.cancel()
                        scans.remove(captureId)
                        onCancel()
                    }
                    .buttonStyle(.signalSecondary)
                    .accessibilityIdentifier("scan-cancel")

                    ZStack {
                        Circle().stroke(Theme.fog.opacity(0.3), lineWidth: 6)
                        Circle()
                            .trim(from: 0, to: coverage)
                            .stroke(Theme.ecto, style: StrokeStyle(lineWidth: 6, lineCap: .round))
                            .rotationEffect(.degrees(-90))
                        Text("\(Int((coverage * 100).rounded()))%")
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(Theme.bone)
                    }
                    .frame(width: 56, height: 56)
                    .accessibilityElement()
                    .accessibilityLabel("How much of the room has been found")
                    .accessibilityValue("\(Int((coverage * 100).rounded())) percent")

                    Button(finishing ? "Saving…" : "Done") { Task { await finish() } }
                        .buttonStyle(.signalPrimary)
                        .disabled(finishing || coverage < 0.05)
                        .accessibilityIdentifier("scan-done")
                }
                .padding(.bottom, 24)
            }
            .padding(.horizontal, 16)
        }
        .task { await begin() }
    }

    private func begin() async {
        startedAt = Date()
        if ScanFlow.usesFakeRoom || !RoomScanner.isSupported {
            // The stand-in room comes in quickly, so a test is not waiting on an animation.
            for step in 1...10 {
                try? await Task.sleep(for: .milliseconds(80))
                fakeProgress = Double(step) / 10
            }
            return
        }
        do {
            let folder = try scans.newCaptureFolder(captureId)
            let scanner = RoomScanner(folder: folder)
            self.scanner = scanner
            scanner.start()
        } catch {
            problem = error.localizedDescription
        }
    }

    private func finish() async {
        finishing = true
        defer { finishing = false }
        do {
            let capture: ScanCapture
            if let scanner {
                capture = try await scanner.finish(room: room, placeId: placeId, sessionId: sessionId)
                try scans.save(capture)
            } else if ScanFlow.usesFakeRoom {
                capture = try ScanFixture.makeCapture(in: scans, room: room, placeId: placeId, sessionId: sessionId,
                                                      startedAt: startedAt, scannedAt: Date())
            } else {
                problem = LidarSupport.reason
                return
            }
            onFinished(capture)
        } catch {
            problem = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        }
    }
}

/// The camera, with the mesh ARKit is building drawn over it.
struct ScanCameraView: UIViewRepresentable {
    let session: ARSession

    func makeUIView(context: Context) -> ARView {
        let view = ARView(frame: .zero, cameraMode: .ar, automaticallyConfigureSession: false)
        view.session = session
        view.debugOptions.insert(.showSceneUnderstanding)
        return view
    }

    func updateUIView(_ view: ARView, context: Context) {}
}
