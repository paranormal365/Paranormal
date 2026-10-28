import SwiftUI
import ARKit
import AVFoundation
import BenKit

/// Comparing a room with the place's scan of it (LiDAR plan, phase 2).
///
/// First the phone finds its place in the room's scan: it loads that scan's world map and the
/// person looks around until ARKit recognizes the room — only then is a new scan in the same frame
/// as the old, and "the chair moved 42 cm" means anything. Then the room is scanned as usual, and
/// what changed is worked out on the phone and kept in the session.
///
/// With `-scanFakeRoom` the stand-in room is recognized at once and "scanned" with its chair moved
/// and a box on the floor.
struct CompareRoomView: View {
    let flow: ScanFlow
    let room: String
    let baseline: PlaceScanSummary
    let sessionId: UUID
    let scans: ScanStore

    private enum Phase: Equatable {
        case fetching
        case recognizing
        case scanning
        case finishing
        case failed(String)

        var isFailure: Bool { if case .failed = self { true } else { false } }
    }

    @State private var phase: Phase = .fetching
    @State private var session = ARSession()
    @State private var scanner: RoomScanner?
    @State private var captureId = UUID()
    @State private var startedAt = Date()
    @State private var watching: Task<Void, Never>?

    var body: some View {
        ZStack {
            Theme.ink.ignoresSafeArea()
            if !ScanFlow.usesFakeRoom, RoomScanner.isSupported {
                ScanCameraView(session: session).ignoresSafeArea()
            }
            VStack {
                Text(message)
                    .font(.callout)
                    .foregroundStyle(phase.isFailure ? Theme.warning : Theme.bone)
                    .multilineTextAlignment(.center)
                    .padding(12)
                    .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: Theme.Radius.card))
                    .padding(.top, 12)
                    .accessibilityIdentifier(phase.isFailure ? "compare-not-recognized" : "compare-status")
                Spacer()
                HStack(spacing: 20) {
                    Button("Cancel") { cancel() }
                        .buttonStyle(.signalSecondary)
                        .accessibilityIdentifier("compare-cancel")
                    if phase == .scanning || phase == .finishing {
                        Button(phase == .finishing ? "Comparing…" : "Done") { Task { await finish() } }
                            .buttonStyle(.signalPrimary)
                            .disabled(phase == .finishing)
                            .accessibilityIdentifier("compare-done")
                    }
                }
                .padding(.bottom, 24)
            }
            .padding(.horizontal, 16)
        }
        .task { await begin() }
        .onDisappear { watching?.cancel() }
    }

    private var message: String {
        switch phase {
        case .fetching: "Fetching the scan of \(room) from \(baseline.scannedBy == "" ? "the place" : "\(baseline.scannedBy)'s visit")…"
        case .recognizing: "Look around slowly until the room is recognized. Stand near where the first scan began."
        case .scanning: scanner?.instruction ?? "Recognized. Now walk the room as you would to scan it."
        case .finishing: "Working out what changed…"
        case .failed(let why): why
        }
    }

    private func begin() async {
        startedAt = Date()
        if ScanFlow.usesFakeRoom || !RoomScanner.isSupported {
            phase = ScanFlow.usesFakeRoom ? .scanning : .failed(LidarSupport.reason)
            return
        }
        guard let placeId = flow.placeId else { phase = .failed("The session doesn't know its place."); return }
        let file: URL
        switch await flow.fetch(placeId: placeId, scanId: baseline.id) {
        case .problem(let why): phase = .failed(why); return
        case .file(let url): file = url
        }
        guard let reader = try? ScanPackage.Reader(url: file),
              let compressed = try? reader.data(ScanPackage.worldMapEntry),
              let archived = try? (compressed as NSData).decompressed(using: .zlib) as Data,
              let map = try? NSKeyedUnarchiver.unarchivedObject(ofClass: ARWorldMap.self, from: archived)
        else {
            phase = .failed("This room's scan has no map to find the room by, so it can't be compared. Scan it again to replace it, or offer a new scan.")
            return
        }

        let configuration = ARWorldTrackingConfiguration()
        configuration.initialWorldMap = map
        configuration.sceneReconstruction = .meshWithClassification
        configuration.frameSemantics = [.sceneDepth]
        session.run(configuration, options: [.resetTracking, .removeExistingAnchors])
        phase = .recognizing

        var watch = RelocalizationWatch(startedAt: Date())
        watching = Task {
            while !Task.isCancelled, watch.outcome == .looking {
                if let tracking = session.currentFrame?.camera.trackingState {
                    watch.note(Self.tracking(tracking), at: Date())
                }
                try? await Task.sleep(for: .milliseconds(250))
            }
            switch watch.outcome {
            case .recognized: startScanning()
            case .failed(let why): phase = .failed(why); session.pause()
            case .looking: break
            }
        }
    }

    private static func tracking(_ state: ARCamera.TrackingState) -> RelocalizationWatch.Tracking {
        switch state {
        case .notAvailable: .notAvailable
        case .normal: .normal
        case .limited(.relocalizing): .relocalizing
        case .limited: .limited
        }
    }

    private func startScanning() {
        do {
            let folder = try scans.newCaptureFolder(captureId)
            let scanner = RoomScanner(folder: folder, session: session)
            self.scanner = scanner
            scanner.start()
            phase = .scanning
        } catch {
            phase = .failed(error.localizedDescription)
        }
    }

    private func finish() async {
        phase = .finishing
        do {
            let capture: ScanCapture
            if let scanner {
                capture = try await scanner.finish(room: room, placeId: flow.placeId, sessionId: sessionId)
                try scans.save(capture)
            } else {
                capture = try ScanFixture.makeCapture(in: scans, room: room, placeId: flow.placeId, sessionId: sessionId,
                                                      startedAt: startedAt, scannedAt: Date(), visit: .chairMovedAndABox)
            }
            await flow.compared(capture, room: room, baseline: baseline)
        } catch {
            phase = .failed((error as? LocalizedError)?.errorDescription ?? error.localizedDescription)
        }
    }

    private func cancel() {
        watching?.cancel()
        scanner?.cancel()
        session.pause()
        scans.remove(captureId)
        flow.finish()
    }
}

/// What a comparison found, each change in words, read aloud on a tap (LiDAR plan, phase 2).
struct CompareResultsView: View {
    let flow: ScanFlow
    let room: String
    let result: ComparisonPackage.Result
    let baseline: PlaceScanSummary

    @State private var speech = AVSpeechSynthesizer()
    @State private var pictures: [String: UIImage] = [:]

    var body: some View {
        SignalForm {
            Section {
                if result.changes.isEmpty {
                    Text("Nothing in \(room) has moved since its scan of \(EventClock.dayOnly(baseline.scannedUtc, nil, inMyTime: true)).")
                        .foregroundStyle(Theme.bone)
                        .accessibilityIdentifier("compare-nothing")
                } else {
                    ForEach(Array(result.changes.enumerated()), id: \.offset) { index, change in
                        Button { say(change.sentence) } label: {
                            HStack(spacing: 12) {
                                if let picture = change.picture, let image = pictures[picture] {
                                    Image(uiImage: image).resizable().scaledToFill()
                                        .frame(width: 56, height: 56).clipShape(RoundedRectangle(cornerRadius: Theme.Radius.small))
                                } else {
                                    Image(systemName: icon(change.kind)).font(.title2).foregroundStyle(Theme.ecto).frame(width: 56)
                                }
                                Text(change.sentence).foregroundStyle(Theme.bone)
                                Spacer()
                                Image(systemName: "speaker.wave.2").foregroundStyle(Theme.fog)
                            }
                        }
                        .accessibilityIdentifier("scan-change-pin")
                        .accessibilityHint("Reads it aloud.")
                    }
                }
            } header: {
                Text("Since \(EventClock.dayOnly(baseline.scannedUtc, nil, inMyTime: true))")
            } footer: {
                Text("Kept in this session, with a picture of each change. Moves under 3 cm or 5° are the sensor, not the room, and are left out.")
            }
            Section {
                Button("See them in the room") {
                    let changes = result.changes
                    flow.finish()
                    if let placeId = flow.placeId {
                        flow.viewing = ScanViewerSource(placeId: placeId, scanId: baseline.id, room: room,
                                                        others: [], changes: changes)
                    }
                }
                .accessibilityIdentifier("compare-view-in-room")
                Button("Done") { flow.finish() }
                    .accessibilityIdentifier("compare-done-results")
            }
        }
        .navigationTitle("What changed in \(room)")
        .task { loadPictures() }
    }

    private func icon(_ kind: ScanChange.Kind) -> String {
        switch kind {
        case .moved: "arrow.up.and.down.and.arrow.left.and.right"
        case .missing: "questionmark.square.dashed"
        case .new: "plus.square.dashed"
        }
    }

    private func say(_ sentence: String) {
        speech.stopSpeaking(at: .immediate)
        speech.speak(AVSpeechUtterance(string: sentence))
    }

    private func loadPictures() {
        guard let reader = try? ScanPackage.Reader(url: flow.comparisonFile(result)) else { return }
        for change in result.changes {
            guard let picture = change.picture, let member = picture.split(separator: "#").last,
                  let data = try? reader.data(String(member)), let image = UIImage(data: data) else { continue }
            pictures[picture] = image
        }
    }
}
