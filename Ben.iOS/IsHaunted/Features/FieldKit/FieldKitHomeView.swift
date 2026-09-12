import SwiftUI
import BenKit

/// Field Kit's front door: what you've recorded, and the button that starts recording.
///
/// Works signed out on purpose. Everything here happens on the device — a group member standing
/// in a cellar with no bars must be able to start a session, and asking them to sign in first
/// would make the feature useless exactly where it matters.
struct FieldKitHomeView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    @State private var starting = false
    @State private var errorMessage: String?

    private var store: FieldSessionStore { dependencies.fieldKit }

    var body: some View {
        List {
            if case .unavailable(let reason) = store.state {
                // A store that cannot open says so. An empty list here would tell somebody
                // their sessions were gone.
                Section {
                    Label(reason, systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.danger)
                        .font(.callout)
                }
            } else {
                Section {
                    Button {
                        starting = true
                    } label: {
                        Label("Start a session", systemImage: "record.circle")
                            .font(.headline)
                    }
                    .accessibilityIdentifier("start-field-session")
                } footer: {
                    Text("Readings, photos and recordings stay on this device until you review them. No signal needed.")
                }
            }

            if let active = store.activeSessionId,
               let summary = store.summary(for: active) {
                Section(summary.isRecording ? "Recording now" : "Set up, not started") {
                    Button {
                        router.push(.fieldSession(active))
                    } label: {
                        SessionRow(summary: summary)
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("field-session-row")
                }
            }

            let finished = store.sessions.filter { !$0.isOpen }
            if finished.isEmpty {
                Section {
                    Text("Nothing recorded yet. A session logs magnetic field, sound and where you were, and holds the photos, video and audio you capture along the way.")
                        .font(.callout)
                        .foregroundStyle(Theme.fog)
                }
            } else {
                Section("Sessions") {
                    ForEach(finished) { summary in
                        Button {
                            router.push(.fieldSessionReview(summary.id))
                        } label: {
                            SessionRow(summary: summary)
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("field-session-row")
                    }
                    .onDelete { offsets in
                        for index in offsets {
                            try? store.delete(finished[index].id)
                        }
                    }
                }
            }
        }
        .navigationTitle("Field Kit")
        .sheet(isPresented: $starting) {
            StartSessionSheet { label, investigation, channels in
                await start(label: label, investigation: investigation, channels: channels)
            }
            .environment(dependencies)
        }
        .alert("Couldn't start the session",
               isPresented: Binding(get: { errorMessage != nil },
                                    set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) { errorMessage = nil }
        } message: {
            Text(errorMessage ?? "")
        }
        .onAppear { store.load() }
    }

    private func start(label: String?, investigation: MyInvestigation?,
                       channels: CaptureChannels) async {
        do {
            let id = try store.startSession(
                locationLabel: label,
                investigationId: investigation?.investigationId,
                investigationTitle: investigation?.title,
                channels: channels)
            starting = false
            router.push(.fieldSession(id))
        } catch {
            starting = false
            errorMessage = error.localizedDescription
        }
    }
}

private struct SessionRow: View {
    let summary: FieldSessionSummary

    var body: some View {
        HStack(spacing: 12) {
            if summary.isRecording {
                Image(systemName: "record.circle")
                    .foregroundStyle(Theme.danger)
                    .symbolEffect(.pulse)
            } else if summary.isPending {
                Image(systemName: "circle.dotted")
                    .foregroundStyle(Theme.fog)
            } else {
                Image(systemName: summary.outcome == .interrupted
                      ? "exclamationmark.triangle" : "waveform")
                    .foregroundStyle(summary.outcome == .interrupted ? Theme.warning : Theme.ecto)
            }

            VStack(alignment: .leading, spacing: 3) {
                // iOS-4: an unnamed session reads as unnamed, and dimmer than a real name, so a
                // list of them still scans by the date underneath rather than by a row of
                // identical grey words.
                Text(summary.title)
                    .foregroundStyle(summary.isUntitled ? Theme.fog : Theme.bone)
                    .italic(summary.isUntitled)
                Text(detail).font(.caption).foregroundStyle(Theme.fog)
                if let investigation = summary.investigationTitle, !investigation.isEmpty,
                   investigation != summary.title {
                    Text(investigation).font(.caption2).foregroundStyle(Theme.haunt)
                }
            }
            Spacer()
            Image(systemName: "chevron.right").font(.caption).foregroundStyle(Theme.fog)
        }
        .padding(.vertical, 2)
    }

    private var detail: String {
        var parts: [String] = [summary.startedAt.formatted(date: .abbreviated, time: .shortened)]
        if let duration = summary.duration {
            parts.append(Self.durationText(duration))
        } else if summary.outcome == .interrupted {
            // Honest: nobody knows when it stopped, so it does not claim a length.
            parts.append("interrupted")
        } else if summary.isPending {
            parts.append("not started")
        }
        if summary.markerCount > 0 {
            parts.append("\(summary.markerCount) marked")
        }
        if summary.captureCount > 0 {
            parts.append("\(summary.captureCount) captured")
        }
        return parts.joined(separator: " · ")
    }

    static func durationText(_ seconds: TimeInterval) -> String {
        let total = Int(seconds.rounded())
        let hours = total / 3600, minutes = (total % 3600) / 60
        if hours > 0 { return "\(hours)h \(minutes)m" }
        if minutes > 0 { return "\(minutes)m" }
        return "\(total)s"
    }
}

/// Where a session gets its name and, optionally, its investigation.
///
/// The label is asked for first because it is the thing that makes a session findable a week
/// later — "back bedroom, north wall" beats a timestamp every time.
private struct StartSessionSheet: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    var onStart: (String?, MyInvestigation?, CaptureChannels) async -> Void

    @State private var label = ""
    /// What this session will record. Chosen HERE, not hunted for on the live screen: video was
    /// off by default and its button only appears once the channel is on, so the camera was
    /// effectively invisible to anyone who did not already know where to look.
    @State private var channels: CaptureChannels = .default
    @State private var investigations: [MyInvestigation] = []
    /// Selected by id, not by value — MyInvestigation is a server-shaped record and
    /// making it Hashable to please a Picker would be the tail wagging the dog.
    @State private var chosenId: UUID?
    @State private var busy = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Where are you? (back bedroom, north wall)", text: $label)
                        .accessibilityIdentifier("session-label")
                } footer: {
                    Text("Your own words. This is what you'll recognise the session by later.")
                }

                if !investigations.isEmpty {
                    Section {
                        Picker("Investigation", selection: $chosenId) {
                            Text("Not linked").tag(UUID?.none)
                            ForEach(investigations) { investigation in
                                Text(investigation.title)
                                    .tag(UUID?.some(investigation.investigationId))
                            }
                        }
                    } footer: {
                        Text("Optional — you can link this session to an investigation later, when you review it.")
                    }
                }

                Section {
                    ForEach(CaptureChannels.orderedForDisplay, id: \.rawValue) { channel in
                        Toggle(isOn: Binding(
                            get: { channels.contains(channel) },
                            set: { isOn in
                                if isOn { channels.insert(channel) } else { channels.remove(channel) }
                            })
                        ) {
                            VStack(alignment: .leading, spacing: 1) {
                                Label(channel.title, systemImage: channel.icon)
                                Text(channel.costNote)
                                    .font(.caption2).foregroundStyle(Theme.fog)
                            }
                        }
                        .tint(Theme.ecto)
                        .accessibilityIdentifier("start-channel-\(channel.title.lowercased())")
                    }
                } header: {
                    Text("What to record")
                } footer: {
                    Text("You can change any of these while the session is running.")
                }

                Section {
                    Button {
                        Task {
                            busy = true
                            await onStart(label.trimmingCharacters(in: .whitespacesAndNewlines)
                                            .isEmpty ? nil : label, chosenInvestigation, channels)
                            busy = false
                        }
                    } label: {
                        if busy { ProgressView().frame(maxWidth: .infinity) }
                        // It opens the live screen; Start is pressed THERE, once the room is ready.
                        else { Text("Open the session").frame(maxWidth: .infinity) }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(busy)
                    .accessibilityIdentifier("confirm-start-session")
                }
            }
            .navigationTitle("New session")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(busy)
                }
            }
            .task { await loadInvestigations() }
        }
        .interactiveDismissDisabled(busy)
    }

    private var chosenInvestigation: MyInvestigation? {
        investigations.first { $0.investigationId == chosenId }
    }

    /// Best-effort: no account, no signal, no investigations — none of which should stop
    /// somebody recording. The picker simply does not appear.
    private func loadInvestigations() async {
        guard dependencies.session.me != nil else { return }
        let store = InvestigationsStore(api: dependencies.api)
        await store.load()
        investigations = store.upcoming
    }
}
