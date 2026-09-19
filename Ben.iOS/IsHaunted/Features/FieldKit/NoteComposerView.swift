import SwiftUI
import BenKit

/// Writing a note during a session — spoken, typed, or recorded.
///
/// **Dictation is the default where the device can do it offline**, because in the field the
/// alternative is stopping, taking a glove off and typing in the dark. Where it cannot be done
/// offline the option is not shown at all: a button that works in the car park and fails in the
/// cellar is worse than no button.
///
/// Recording the voice itself stays available regardless. A transcription is a machine's reading
/// of what somebody said; sometimes the reading is not the thing worth keeping.
struct NoteComposerView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    let session: ActiveFieldSession
    /// Set when this note is being attached to a marker kind other than a plain mark.
    var markerKind: MarkerKind = .manual

    @State private var kind: NoteKind = .typed
    @State private var text = ""
    @State private var isListening = false
    @State private var canDictate = false
    @State private var problem: String?
    @State private var listener: Task<Void, Never>?

    private var dictation: DictationService? { dependencies.fieldKit.sensors().dictation }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("How", selection: $kind) {
                        ForEach(available, id: \.self) { option in
                            Label(option.title, systemImage: option.icon).tag(option)
                        }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("note-kind")
                } footer: {
                    Text(footerText)
                }

                switch kind {
                case .dictated: dictationSection
                case .typed: typingSection
                case .audio: recordingSection
                }

                if let problem {
                    Section {
                        Label(problem, systemImage: "exclamationmark.triangle")
                            .font(.callout).foregroundStyle(Theme.danger)
                    }
                }

                Section {
                    Button {
                        Task { await save() }
                    } label: {
                        Text("Save the note").frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(isListening || (kind != .audio && trimmed.isEmpty))
                    .accessibilityIdentifier("save-note")
                }
            }
            .navigationTitle("Note")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { stopListening(); dismiss() }
                }
            }
            .task { await prepare() }
            .onDisappear { stopListening() }
        }
    }

    // MARK: - Sections

    @ViewBuilder
    private var dictationSection: some View {
        Section {
            Button {
                if isListening { Task { await finishDictating() } } else { startDictating() }
            } label: {
                HStack {
                    Image(systemName: isListening ? "stop.circle.fill" : "mic.circle.fill")
                        .font(.title)
                    Text(isListening ? "Stop" : "Start speaking")
                        .font(.headline)
                }
                .frame(maxWidth: .infinity, minHeight: 56)
            }
            .buttonStyle(.bordered)
            .tint(isListening ? Theme.danger : Theme.ecto)
            .accessibilityIdentifier("dictate-toggle")

            // Editable afterwards: a transcription is a machine's best guess, and the person who
            // said it is the authority on what they said.
            TextField("What you said appears here", text: $text, axis: .vertical)
                .lineLimit(3...10)
                .accessibilityIdentifier("note-text")
        } footer: {
            Text(isListening
                 ? "Listening. Everything stays on this device."
                 : "You can correct the wording before saving.")
        }
    }

    @ViewBuilder
    private var typingSection: some View {
        Section {
            TextField("What happened?", text: $text, axis: .vertical)
                .lineLimit(3...10)
                .accessibilityIdentifier("note-text")
        }
    }

    @ViewBuilder
    private var recordingSection: some View {
        Section {
            if session.recording == nil {
                Text("Audio isn't recording in this session, so there'd be nothing to attach the note to. Switch audio on, or type the note instead.")
                    .font(.callout).foregroundStyle(Theme.fog)
            } else {
                Label("The mark will point at this moment in the recording.",
                      systemImage: "waveform")
                    .font(.callout).foregroundStyle(Theme.fog)
                TextField("A word about it, optional", text: $text)
                    .accessibilityIdentifier("note-text")
            }
        }
    }

    private var available: [NoteKind] {
        // Dictation appears ONLY where the device can transcribe with no connection.
        canDictate ? [.dictated, .typed, .audio] : [.typed, .audio]
    }

    private var footerText: String {
        canDictate
            ? "Dictation happens on this phone — nothing is sent anywhere."
            : "This device can't turn speech into text without a connection, so dictation isn't offered."
    }

    private var trimmed: String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    // MARK: - Doing it

    private func prepare() async {
        canDictate = await dictation?.isAvailableOffline ?? false
        if canDictate { kind = .dictated }
    }

    private func startDictating() {
        guard let dictation else { return }
        problem = nil
        isListening = true
        listener = Task {
            do {
                for await update in try await dictation.start() {
                    text = update.text
                }
            } catch {
                problem = error.localizedDescription
            }
            isListening = false
        }
    }

    private func finishDictating() async {
        guard let dictation else { return }
        let final = await dictation.stop()
        if !final.isEmpty { text = final }
        isListening = false
        listener?.cancel()
        listener = nil
    }

    private func stopListening() {
        listener?.cancel()
        listener = nil
        guard isListening, let dictation else { return }
        isListening = false
        Task { await dictation.stop() }
    }

    private func save() async {
        if isListening { await finishDictating() }
        let note = trimmed.isEmpty ? nil : trimmed
        await session.mark(kind: markerKind, note: note)
        dismiss()
    }
}
