import SwiftUI
import AVFoundation
import BenKit

// The investigation's own controls, down the side of the screen (Ben, 10/15/2026).
//
// On the iPhone Duo opened, iOS puts a screen's toolbar buttons in a column at the right edge. The ☰
// at the top of that column gets around the app; while a session runs, the buttons below it run the
// investigation: mark what was noticed, log an experience, ask and listen, note, change room, reset
// the base, the flashlight. The page itself keeps the gauge, the clock and the readings.

extension EnvironmentValues {
    /// True where the investigation's controls go in the side column rather than on the page: a
    /// regular-width window too narrow for a standing sidebar, which is the iPhone Duo opened.
    @Entry var investigationRail = false
}

/// The phone's light. Only the back camera has one; where there is none the button is not shown.
@MainActor
@Observable
final class Flashlight {
    private(set) var isOn = false

    var isAvailable: Bool { AVCaptureDevice.default(for: .video)?.hasTorch == true }

    func toggle() {
        guard let device = AVCaptureDevice.default(for: .video), device.hasTorch else { return }
        do {
            try device.lockForConfiguration()
            defer { device.unlockForConfiguration() }
            if isOn {
                device.torchMode = .off
            } else {
                try device.setTorchModeOn(level: AVCaptureDevice.maxAvailableTorchLevel)
            }
            isOn.toggle()
        } catch {
            isOn = false
        }
    }

    func off() {
        guard isOn, let device = AVCaptureDevice.default(for: .video) else { return }
        try? device.lockForConfiguration()
        device.torchMode = .off
        device.unlockForConfiguration()
        isOn = false
    }
}

/// What somebody felt, saw or heard, logged at the moment as a mark with these words.
enum Experience: String, CaseIterable, Identifiable {
    case coldSpot = "Cold spot"
    case touched = "Touched"
    case heardVoice = "Heard a voice"
    case heardSound = "Heard a sound"
    case sawSomething = "Saw something"
    case smell = "Smelled something"
    case uneasy = "Felt uneasy"

    var id: String { rawValue }

    var symbol: String {
        switch self {
        case .coldSpot: "thermometer.snowflake"
        case .touched: "hand.point.up.left"
        case .heardVoice: "waveform"
        case .heardSound: "ear"
        case .sawSomething: "eye"
        case .smell: "nose"
        case .uneasy: "exclamationmark.triangle"
        }
    }
}

/// A line that says what a button in the side column just did, then fades: the column's buttons
/// change nothing else on screen, and in the dark a press that leaves no trace reads as a press that
/// missed (found 10/15/2026 on the Duo walk-through).
@MainActor
@Observable
final class RailNotice {
    private(set) var text: String?
    private var clearing: Task<Void, Never>?

    func show(_ text: String) {
        withAnimation { self.text = text }
        clearing?.cancel()
        clearing = Task { [weak self] in
            try? await Task.sleep(for: .seconds(2.5))
            guard !Task.isCancelled else { return }
            withAnimation { self?.text = nil }
        }
    }
}

struct RailNoticeView: View {
    let notice: RailNotice

    var body: some View {
        if let text = notice.text {
            Label(text, systemImage: "checkmark.circle.fill")
                .font(.callout.weight(.semibold))
                .foregroundStyle(Theme.bone)
                .padding(.horizontal, 16).padding(.vertical, 10)
                .background(Theme.mist, in: Capsule())
                .overlay(Capsule().strokeBorder(Theme.ecto.opacity(0.5)))
                .transition(.move(edge: .top).combined(with: .opacity))
                .accessibilityIdentifier("rail-notice")
        }
    }
}

extension MarkerKind {
    /// Whether details can be added to this kind of mark after the session. Everything a person or
    /// the phone marked, except the app being put away and coming back, which are bookkeeping.
    var takesDetails: Bool { self != .appBackgrounded && self != .appReturned }
}

/// Writing more about one mark, after the session (Ben, 10/15/2026). The mark's own time, kind and
/// words are shown above and can't be changed; only the text below can.
struct MarkDetailsSheet: View {
    let marker: FieldMarkerRecord
    let startedAt: Date
    let save: (String) async throws -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var text = ""
    @State private var problem: String?
    @State private var saving = false
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            SignalForm {
                Section {
                    LabeledContent("Marked") {
                        Text("\(marker.kind.title) at \(SessionClock.elapsed(from: startedAt, to: marker.at))")
                    }
                    if let room = marker.room { LabeledContent("Room", value: room) }
                    if let note = marker.note { LabeledContent("At the time", value: note) }
                } footer: {
                    Text("This is what was recorded, and it stays as it is.")
                }
                Section {
                    TextEditor(text: $text)
                        .frame(minHeight: 140)
                        .focused($focused)
                        .accessibilityIdentifier("marker-details-field")
                } header: {
                    Text("Details")
                } footer: {
                    Text("What happened, who else noticed, how long it lasted. Saved with the mark, marked as added after the session, and sent with it.")
                }
                if let problem {
                    Text(problem).foregroundStyle(Theme.warning)
                }
            }
            .navigationTitle("Add details")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        saving = true
                        Task {
                            do { try await save(text); dismiss() } catch { problem = error.localizedDescription }
                            saving = false
                        }
                    }
                    .disabled(saving)
                    .accessibilityIdentifier("marker-details-save")
                }
            }
            .onAppear { text = marker.details ?? ""; focused = true }
        }
    }
}
