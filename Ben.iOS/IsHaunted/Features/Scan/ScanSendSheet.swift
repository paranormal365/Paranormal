import SwiftUI
import BenKit

/// Everything after the scanner closes, in one sheet: what to keep, the upload, a room that turned
/// out to be scanned already, and what the scan became (LiDAR plan, phase 1).
///
/// One sheet whose content follows the flow's step, rather than a sheet per step — sheets
/// replacing sheets is where SwiftUI drops presentations.
struct ScanSendSheet: View {
    let flow: ScanFlow
    let storageLeftBytes: Int64?

    var body: some View {
        NavigationStack {
            Group {
                switch flow.step {
                case .choosingRetention(let capture):
                    ScanRetentionSheet(flow: flow, capture: capture, storageLeftBytes: storageLeftBytes)
                case .uploading(let capture, let retention):
                    ScanUploadView(capture: capture, bytes: flow.estimatedBytes(capture, retention))
                case .conflict(_, _, let conflict):
                    ScanConflictSheet(flow: flow, conflict: conflict)
                case .done(let result):
                    ScanResultView(flow: flow, result: result)
                case .kept(let capture, let why):
                    ScanKeptView(flow: flow, capture: capture, why: why)
                case .compared(let room, let result, let baseline):
                    CompareResultsView(flow: flow, room: room, result: result, baseline: baseline)
                default:
                    ProgressView()
                }
            }
            .navigationBarTitleDisplayMode(.inline)
        }
        .interactiveDismissDisabled(isBusy)
    }

    private var isBusy: Bool {
        if case .uploading = flow.step { true } else { false }
    }
}

/// "Model and pictures" or "Model only", with what each weighs (LiDAR plan, §2).
struct ScanRetentionSheet: View {
    let flow: ScanFlow
    let capture: ScanCapture
    let storageLeftBytes: Int64?

    var body: some View {
        SignalForm {
            Section {
                choice(.modelAndPictures, "Model and pictures",
                       "The room and \(capture.keyframeCount) pictures of it — what a walkthrough shows close up.",
                       identifier: "scan-retention-pictures")
                choice(.modelOnly, "Model only",
                       "The room's shape, without the pictures. Smaller, for an account short of room.",
                       identifier: "scan-retention-model")
            } header: {
                Text("What to keep of \(capture.room)")
            } footer: {
                if let left = storageLeftBytes {
                    Text("Your account has room for \(Self.size(left)) more. A scan that doesn't fit can't be sent until something is removed.")
                } else {
                    Text("The scan is kept with the place, for everybody who comes after you to compare against.")
                }
            }
            if let problem = flow.problem {
                Section { Text(problem).foregroundStyle(Theme.warning) }
            }
            Section {
                Button("Discard this scan", role: .destructive) { Task { await flow.discard(capture) } }
                    .accessibilityIdentifier("scan-retention-discard")
            }
        }
        .navigationTitle("Keep what?")
    }

    private func choice(_ retention: ScanManifest.Retention, _ title: String, _ detail: String,
                        identifier: String) -> some View {
        let bytes = flow.estimatedBytes(capture, retention)
        let fits = storageLeftBytes.map { bytes <= $0 } ?? true
        return Button {
            Task { await flow.send(capture, retention: retention) }
        } label: {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).foregroundStyle(Theme.bone)
                    Text(detail).font(.caption).foregroundStyle(Theme.fog)
                    if !fits {
                        Text("Needs \(Self.size(bytes)); your account has \(Self.size(storageLeftBytes ?? 0)) left.")
                            .font(.caption)
                            .foregroundStyle(Theme.warning)
                    }
                }
                Spacer()
                Text(Self.size(bytes)).font(.callout.monospacedDigit()).foregroundStyle(Theme.fog)
            }
        }
        .disabled(!fits)
        .accessibilityIdentifier(identifier)
    }

    static func size(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}

/// The upload under way. One file: on failure it is sent again from the start, and the phone keeps
/// it until the server has it.
struct ScanUploadView: View {
    let capture: ScanCapture
    let bytes: Int64

    var body: some View {
        VStack(spacing: 16) {
            ProgressView()
                .controlSize(.large)
            Text("Sending \(capture.room)")
                .font(.headline)
                .foregroundStyle(Theme.bone)
            Text("\(ScanRetentionSheet.size(bytes)). Keep the app open until it's sent.")
                .font(.callout)
                .foregroundStyle(Theme.fog)
        }
        .padding(32)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Theme.ink)
        .accessibilityIdentifier("scan-uploading")
        .navigationTitle("Sending")
    }
}

/// The room already has a scan this person may not replace — or one landed while they were
/// scanning. The scan is still on the phone; the server said what may be done with it.
struct ScanConflictSheet: View {
    let flow: ScanFlow
    let conflict: PlaceScanConflict

    var body: some View {
        SignalForm {
            Section {
                Text(conflict.says).foregroundStyle(Theme.bone)
                if let why = conflict.replaceReason {
                    Text(why).font(.caption).foregroundStyle(Theme.fog)
                }
            }
            Section {
                if conflict.choices.contains("replace") {
                    Button("Replace the room's scan") { Task { await flow.resolve("replace") } }
                        .accessibilityIdentifier("scan-conflict-replace")
                }
                if conflict.choices.contains("compare") {
                    Button("Keep it as a comparison") { Task { await flow.resolve("compare") } }
                        .accessibilityIdentifier("scan-conflict-compare")
                }
                if conflict.choices.contains("offer") {
                    Button("Offer it to replace the room's scan") { Task { await flow.resolve("offer") } }
                        .accessibilityIdentifier("scan-conflict-offer")
                }
                Button("Discard it", role: .destructive) { Task { await flow.resolve("discard") } }
                    .accessibilityIdentifier("scan-conflict-discard")
            } footer: {
                Text("An offer goes to whoever looks after this place's scans. Nobody else sees it unless they accept it.")
            }
        }
        .navigationTitle("\(conflict.roomName) is scanned")
    }
}

/// What the scan became, in the server's words, and the way to look at it.
struct ScanResultView: View {
    let flow: ScanFlow
    let result: PlaceScanUploadResult

    var body: some View {
        SignalForm {
            Section {
                Label(result.says, systemImage: result.outcome == "offered" ? "paperplane" : "checkmark.circle")
                    .foregroundStyle(Theme.bone)
                    .accessibilityIdentifier("scan-result")
            }
            Section {
                if result.outcome != "offered" {
                    Button("View the scan") {
                        let room = result.scan.roomName
                        flow.finish()
                        flow.viewing = ScanViewerSource(placeId: result.scan.placeId, scanId: result.scan.id, room: room,
                                                        others: flow.rooms?.rooms.compactMap { r in r.current.map { (r.roomName, $0.id) } } ?? [])
                    }
                    .accessibilityIdentifier("scan-result-view")
                }
                Button("Done") { flow.finish() }
                    .accessibilityIdentifier("scan-result-done")
            }
        }
        .navigationTitle("Sent")
    }
}

/// A scan kept on the phone — no signal, or kept as a comparison.
struct ScanKeptView: View {
    let flow: ScanFlow
    let capture: ScanCapture
    let why: String

    var body: some View {
        SignalForm {
            Section { Text(why).foregroundStyle(Theme.bone) }
            Section {
                Button("Try sending it now") { Task { await flow.sendAgain(capture) } }
                Button("Done") { flow.finish() }
                    .accessibilityIdentifier("scan-kept-done")
            }
        }
        .navigationTitle("Kept on this phone")
    }
}
