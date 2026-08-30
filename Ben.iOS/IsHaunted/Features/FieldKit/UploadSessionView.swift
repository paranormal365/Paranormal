import SwiftUI
import BenKit

/// Sending a session to the group, once there is signal to do it with.
///
/// Recording happens offline; this does not. The whole flow assumes somebody is home, on wifi,
/// deciding what to hand over — so it asks which investigation it belongs to, sends the files
/// one at a time so a dropped connection costs one of them, and only then offers to clear the
/// phone.
struct UploadSessionView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    let sessionId: UUID

    @State private var investigations: [MyInvestigation] = []
    @State private var chosenInvestigationId: UUID?
    @State private var captures: [CaptureMark] = []
    @State private var chosen: Set<UUID> = []
    @State private var progress: [UUID: FileState] = [:]
    @State private var busy = false
    @State private var finished = false
    @State private var errorMessage: String?
    @State private var offeringCleanup = false
    @State private var offeringArchive = false

    private enum FileState: Equatable {
        case waiting, sending, sent, failed(String)
    }

    private var store: FieldSessionStore { dependencies.fieldKit }
    private var summary: FieldSessionSummary? { store.summary(for: sessionId) }

    var body: some View {
        NavigationStack {
            Form {
                if dependencies.session.me == nil {
                    Section {
                        Label("Sign in to send this session", systemImage: "person.crop.circle")
                            .font(.callout).foregroundStyle(Theme.warning)
                        Text("Recording never needs an account. Sending does — the session has to belong to somebody.")
                            .font(.caption).foregroundStyle(Theme.fog)
                    }
                } else {
                    destinationSection
                    filesSection
                    sendSection
                    archiveSection
                }
            }
            .navigationTitle("Send session")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Done") { dismiss() }.disabled(busy)
                }
            }
            .alert("Free up the phone?", isPresented: $offeringCleanup) {
                Button("Keep them", role: .cancel) { dismiss() }
                Button("Delete the recordings", role: .destructive) {
                    try? store.deleteLocalMedia(for: sessionId)
                    dismiss()
                }
            } message: {
                Text("Everything is on the server now. The readings, marks and where you were stay on this phone either way — only the photos, video and audio would be cleared.")
            }
            .task { await load() }
        }
    }

    // MARK: - Sections

    @ViewBuilder
    private var destinationSection: some View {
        Section {
            Picker("Investigation", selection: $chosenInvestigationId) {
                Text("Just my own").tag(UUID?.none)
                ForEach(investigations) { investigation in
                    // The case comes with it — picking the investigation IS picking the case.
                    Text(label(for: investigation)).tag(UUID?.some(investigation.investigationId))
                }
            }
            .accessibilityIdentifier("upload-investigation")
        } header: {
            Text("Where it belongs")
        } footer: {
            Text(chosenInvestigationId == nil
                 ? "Kept against your account only. You can send it to an investigation later."
                 : "The group working this investigation will be able to see it.")
        }
    }

    @ViewBuilder
    private var filesSection: some View {
        if !captures.isEmpty {
            Section {
                ForEach(captures) { capture in
                    HStack {
                        Toggle(isOn: Binding(
                            get: { chosen.contains(capture.id) },
                            set: { on in
                                if on { chosen.insert(capture.id) } else { chosen.remove(capture.id) }
                            })
                        ) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(capture.relativePath
                                        .replacingOccurrences(of: "media/", with: ""))
                                Text(stateText(for: capture))
                                    .font(.caption2)
                                    .foregroundStyle(stateColour(for: capture))
                            }
                        }
                        .tint(Theme.ecto)
                        .disabled(busy || !store.hasLocalFile(capture.relativePath, in: sessionId))
                        .accessibilityIdentifier("upload-file-toggle")

                        if progress[capture.id] == .sending { ProgressView() }
                    }
                    .swipeActions {
                        // The ones somebody simply does not want to keep.
                        Button(role: .destructive) {
                            try? store.deleteCapture(capture.id, in: sessionId)
                            captures = store.captures(for: sessionId)
                        } label: {
                            Label("Delete", systemImage: "trash")
                        }
                        .disabled(busy)
                    }
                }
            } header: {
                Text("Recordings")
            } footer: {
                Text("Sent one at a time, so a dropped connection costs one file rather than the night. Swipe to delete anything you don't want to keep.")
            }
        }
    }

    @ViewBuilder
    private var sendSection: some View {
        if let errorMessage {
            Section {
                Label(errorMessage, systemImage: "exclamationmark.triangle")
                    .font(.callout).foregroundStyle(Theme.danger)
            }
        }

        Section {
            Button {
                Task { await send() }
            } label: {
                if busy { ProgressView().frame(maxWidth: .infinity) }
                else {
                    Text(finished ? "Send anything still waiting" : "Send")
                        .frame(maxWidth: .infinity)
                }
            }
            .buttonStyle(.borderedProminent)
            .disabled(busy)
            .accessibilityIdentifier("send-session")
        } footer: {
            if let uploadedAt = summary?.uploadedAt {
                Text("Last sent \(uploadedAt.formatted(date: .abbreviated, time: .shortened)).")
            }
        }
    }

    /// Offered only once the session is actually on the server, because publishing is a thing
    /// that happens to an uploaded row — and only for sessions with no investigation, since a
    /// group's work reaches a place page through the investigation it belongs to.
    @ViewBuilder
    private var archiveSection: some View {
        if summary?.isUploaded == true, chosenInvestigationId == nil,
           let serverId = summary?.serverSessionId {
            Section {
                Button {
                    offeringArchive = true
                } label: {
                    Label("Add to the public archive", systemImage: "building.columns")
                }
                .disabled(busy)
                .accessibilityIdentifier("add-to-archive")
            } header: {
                Text("Share what you found")
            } footer: {
                Text("Puts your readings on the location's own page, next to everyone else who "
                   + "has recorded there. Photos and audio stay private. You can undo it later.")
            }
            .sheet(isPresented: $offeringArchive) {
                PublishToArchiveView(serverSessionId: serverId, localSessionId: sessionId)
                    .environment(dependencies)
            }
        }
    }

    // MARK: - Doing it

    private func load() async {
        captures = store.captures(for: sessionId)
        chosen = Set(captures.filter { store.hasLocalFile($0.relativePath, in: sessionId) }
                        .map(\.id))
        chosenInvestigationId = summary?.investigationId

        guard dependencies.session.me != nil else { return }
        let roster = InvestigationsStore(api: dependencies.api)
        await roster.load()
        investigations = roster.investigations
    }

    private func send() async {
        busy = true
        errorMessage = nil
        defer { busy = false }

        do {
            // The document first: it creates the record everything else attaches to.
            let document = try await buildDocument()
            let me = dependencies.session.me

            let result = await dependencies.fieldUpload.submitDocument(
                document,
                deviceSessionId: sessionId,
                investigationId: chosenInvestigationId,
                recordedByAppUserId: me?.userId,
                // The server resolves the name from the account — the app knows who signed in,
                // not what everyone else calls them.
                recordedByName: nil)

            guard case .success(let server) = result else {
                if case .failure(let error) = result { errorMessage = error.message }
                return
            }
            store.markUploaded(sessionId, serverSessionId: server.id)

            // Then the files, one at a time. A failure here is recorded against that file and
            // the rest carry on — losing the night because file three dropped would be absurd.
            for capture in captures where chosen.contains(capture.id) {
                guard store.hasLocalFile(capture.relativePath, in: sessionId) else { continue }
                progress[capture.id] = .sending

                let url = store.files.fileURL(for: sessionId, relativePath: capture.relativePath)
                let digest = try? DeviceDataExporter.sha256(of: url)
                let outcome = await dependencies.fieldUpload.submitFile(
                    sessionId: server.id, fileURL: url,
                    relativePath: capture.relativePath,
                    contentType: contentType(for: capture.relativePath),
                    sha256: digest)

                switch outcome {
                case .success(let file):
                    progress[capture.id] = file.digestMatched
                        ? .sent
                        : .failed("Arrived damaged — send it again.")
                    store.markFileUploaded(
                        capture.id, in: sessionId,
                        problem: file.digestMatched ? nil : "The file arrived damaged.")
                case .failure(let error):
                    progress[capture.id] = .failed(error.message)
                    store.markFileUploaded(capture.id, in: sessionId, problem: error.message)
                }
            }

            finished = true
            captures = store.captures(for: sessionId)
            // Only offered when there is genuinely nothing left to lose.
            if store.isFullyUploaded(sessionId),
               captures.contains(where: { store.hasLocalFile($0.relativePath, in: sessionId) }) {
                offeringCleanup = true
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func buildDocument() async throws -> Data {
        guard let summary else { throw FieldSessionError.unavailable }
        let request = DeviceDataExporter.Request(
            sessionId: sessionId,
            startedAt: summary.startedAt,
            endedAt: summary.endedAt,
            locationLabel: summary.locationLabel,
            deviceModel: DeviceModel.identifier(),
            timezone: TimeZone.current.identifier,
            batteryPercentAtStart: nil,
            trigger: SamplingPolicy.default.trigger(),
            includedMedia: captures.filter { chosen.contains($0.id) }.map(\.relativePath))

        return try await DeviceDataExporter(files: store.files).buildDocument(
            request, log: ReadingLog(fileURL: store.files.readingLogURL(for: sessionId)))
    }

    private func label(for investigation: MyInvestigation) -> String {
        if let reference = investigation.caseReference, !reference.isEmpty {
            return "\(reference) · \(investigation.title)"
        }
        return investigation.title
    }

    private func stateText(for capture: CaptureMark) -> String {
        if !store.hasLocalFile(capture.relativePath, in: sessionId) {
            return "cleared from this phone"
        }
        switch progress[capture.id] {
        case .sending: return "sending…"
        case .sent: return "sent"
        case .failed(let reason): return reason
        default: return capture.at.formatted(date: .omitted, time: .standard)
        }
    }

    private func stateColour(for capture: CaptureMark) -> Color {
        switch progress[capture.id] {
        case .sent: Theme.ecto
        case .failed: Theme.danger
        default: Theme.fog
        }
    }

    private func contentType(for path: String) -> String {
        switch (path as NSString).pathExtension.lowercased() {
        case "jpg", "jpeg": "image/jpeg"
        case "mov": "video/quicktime"
        case "mp4": "video/mp4"
        case "m4a": "audio/mp4"
        default: "application/octet-stream"
        }
    }
}
