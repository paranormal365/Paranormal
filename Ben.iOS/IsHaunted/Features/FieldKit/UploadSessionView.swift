import SwiftUI
import AVFoundation
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

    /// The in and out points (item 210). Nil until the session's span is known.
    @State private var trim: SessionTrimRange?
    /// The same replay the review screen uses, here so a dragged handle shows what is under it
    /// and Play runs exactly the stretch that will be sent.
    @State private var replay = SessionReplay()
    @State private var source: ReplaySource?
    /// How long each recording runs, from the capture rows. A recording whose length was never
    /// stored is sent whole rather than guessed at — see SessionTrimPlan.
    @State private var durations: [String: TimeInterval] = [:]
    @State private var readingTimes: [Date] = []
    @State private var markerTimes: [Date] = []

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
                    trimSection
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

    /// Choosing the stretch worth sending (item 210).
    ///
    /// **Before the upload, never after.** Ben chose this on 2026-09-04 over trimming on the
    /// server, and the sentence at the bottom of the section is the reason: the full recording
    /// stays on this phone as a matter of fact, because the rest of it is simply never sent.
    /// Nothing on the server is ever destroyed and there is nothing to undo.
    @ViewBuilder
    private var trimSection: some View {
        if let plan = currentPlan {
            Section {
                SessionTrimSlider(range: Binding(
                    get: { trim ?? SessionTrimRange(startedAt: summary?.startedAt ?? .now,
                                                    endedAt: summary?.endedAt,
                                                    lastReadingAt: readingTimes.last) },
                    set: { trim = $0 }))
                .disabled(busy)
                // Dragging either handle parks the preview there.
                .onChange(of: trim?.inPoint) { _, moment in if let moment { replay.pause(); replay.seek(to: moment) } }
                .onChange(of: trim?.outPoint) { _, moment in if let moment { replay.pause(); replay.seek(to: moment) } }

                if let source, let trimBinding = Binding($trim) {
                    TrimPreview(replay: replay, source: source, range: trimBinding) { path in
                        store.files.fileURL(for: sessionId, relativePath: path)
                    }
                    .disabled(busy)
                }

                if !plan.isWholeSession {
                    VStack(alignment: .leading, spacing: 4) {
                        Label("\(plan.readingCount) reading\(plan.readingCount == 1 ? "" : "s")"
                            + " · \(plan.markerCount) mark\(plan.markerCount == 1 ? "" : "s")",
                              systemImage: "waveform.path.ecg")
                            .font(.caption)

                        // Said file by file, because "3 recordings" hides the one that is about to
                        // be left behind. Somebody deciding needs to see which.
                        ForEach(plan.media, id: \.media.relativePath) { decision in
                            Label(Self.describe(decision), systemImage: Self.icon(decision))
                                .font(.caption2)
                                .foregroundStyle(decision.outcome == .leftOut ? Theme.fog : Theme.bone)
                        }
                    }
                    // A container, for the same reason the track is one: a bare identifier on a
                    // stack is inherited by every label inside it and names no element itself.
                    .accessibilityElement(children: .contain)
                    .accessibilityIdentifier("trim-summary")

                    Button("Send the whole session") { trim?.reset() }
                        .font(.caption)
                        .disabled(busy)
                        .accessibilityIdentifier("trim-reset")
                }
            } header: {
                Text("What to send")
            } footer: {
                Text(plan.isWholeSession
                     ? "Drag the green and red dots to send only part of the night. An hour of recording usually matters for ten seconds, and only what you choose is uploaded."
                     : "The full recording stays on this phone — nothing outside the window is sent, and nothing already sent is changed. Clearing the phone afterwards is a separate choice, and it is the only thing that removes the original.")
            }
        }
    }

    /// What the current in and out points would actually send.
    private var currentPlan: SessionTrimPlan? {
        guard let summary, let trim else { return nil }
        return SessionTrimPlan.plan(
            window: trim.window,
            startedAt: summary.startedAt,
            endedAt: summary.endedAt,
            readingTimes: readingTimes,
            markerTimes: markerTimes,
            media: captures
                .filter { chosen.contains($0.id) }
                .map { TrimmableMedia(relativePath: $0.relativePath, kind: $0.kind,
                                      startedAt: $0.at,
                                      duration: durations[$0.relativePath]) })
    }

    private static func describe(_ decision: SessionTrimPlan.MediaDecision) -> String {
        let name = decision.media.relativePath.replacingOccurrences(of: "media/", with: "")
        switch decision.outcome {
        case .sentWhole: return "\(name) — sent whole"
        case .leftOut:   return "\(name) — not sent"
        case .cut(_, let duration):
            return "\(name) — cut to \(SessionTrimSlider.duration(duration))"
        }
    }

    private static func icon(_ decision: SessionTrimPlan.MediaDecision) -> String {
        switch decision.outcome {
        case .sentWhole: "checkmark.circle"
        case .cut:       "scissors"
        case .leftOut:   "minus.circle"
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

        // The trimmer's track, and the numbers it reports (item 210). Read once: a five-hour
        // session's readings are tens of thousands of lines and re-reading them on every drag
        // would make the handle stutter.
        await loadTrimData()

        guard dependencies.session.me != nil else { return }
        let roster = InvestigationsStore(api: dependencies.api)
        await roster.load()
        investigations = roster.investigations
    }

    /// The session's span, its reading times, and how long each recording runs.
    ///
    /// Durations come from AVFoundation rather than from the capture row, which has never stored
    /// one. A file whose length will not load stays absent from the map, and the plan then sends
    /// it whole rather than guessing at what it overlaps.
    private func loadTrimData() async {
        guard let summary else { return }

        // One source for everything the trimmer needs: the capture rows already hold each
        // recording's length, the markers already carry their times, and the replay is the
        // preview. Probing every file with AVFoundation, which the first version did, was slower
        // and answered a question the store had already answered.
        store.load()
        guard let source = store.replayData(for: sessionId) else { return }
        self.source = source

        await replay.load(readingLog: source.log, markers: source.markers,
                          media: source.media, baselines: source.baselines,
                          startedAt: source.startedAt, endedAt: source.endedAt)

        readingTimes = replay.timeline.readings.map(\.at)
        markerTimes = source.markers.map(\.at)
        durations = Dictionary(source.media.map { ($0.relativePath, $0.duration) },
                               uniquingKeysWith: { first, _ in first })

        trim = SessionTrimRange(startedAt: summary.startedAt,
                                endedAt: summary.endedAt,
                                lastReadingAt: readingTimes.last)
    }

    private func send() async {
        busy = true
        errorMessage = nil
        defer { busy = false }

        // The scratch directory every cut copy is written into, and cleared afterwards. Never
        // the session's own media directory — a cut written beside the original is one rename
        // away from replacing the thing this feature promises to leave alone.
        let scratch = FileManager.default.temporaryDirectory
            .appendingPathComponent("trim-\(sessionId.uuidString.lowercased())", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: scratch) }

        do {
            let plan = currentPlan
            let window = (plan?.isWholeSession == false) ? plan?.window : nil

            // The document first: it creates the record everything else attaches to.
            var document = try await buildDocument(window: window)

            // Each cut recording's readings still count their offsets from a beginning that will
            // not be in the uploaded file. Left alone, the player would place the audio as far
            // from its readings as the amount cut off the front.
            for decision in plan?.cut ?? [] {
                if case .cut(let from, _) = decision.outcome {
                    document = DeviceDataExporter.rebaseAudioOffsets(
                        forFilename: decision.media.relativePath, by: from, in: document)
                }
            }

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

                // A file the window excludes is not sent at all, and is not reported as a
                // failure either — it was left out on purpose.
                let decision = plan?.media.first { $0.media.relativePath == capture.relativePath }
                if decision?.outcome == .leftOut {
                    progress[capture.id] = nil
                    continue
                }

                progress[capture.id] = .sending

                let original = store.files.fileURL(for: sessionId, relativePath: capture.relativePath)
                var url = original

                // Cut into scratch, never over the original. Every failure inside the trimmer
                // sends the whole file rather than nothing — losing a recording to a failed cut
                // would be far worse than uploading more than was asked for.
                if case .cut(let from, let duration) = decision?.outcome {
                    let result = await SessionMediaTrimmer().cut(
                        original, from: from, duration: duration, into: scratch)
                    if case .cut(let trimmed) = result { url = trimmed }
                }

                // The digest is of what is actually SENT. Sending the original's digest with a
                // cut file would make the server report every trimmed recording as damaged.
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

    private func buildDocument(window: SessionWindow? = nil) async throws -> Data {
        guard let summary else { throw FieldSessionError.unavailable }
        // A clip is named for what it is — "back bedroom (20:00–30:00)" — on the server's list,
        // in the player's title and in the report. A whole session keeps its name.
        let label = window.map {
            SessionTrimPlan.clipLabel(base: summary.locationLabel, window: $0,
                                      sessionStart: summary.startedAt, isWholeSession: false)
        } ?? summary.locationLabel
        let request = DeviceDataExporter.Request(
            sessionId: sessionId,
            startedAt: summary.startedAt,
            endedAt: summary.endedAt,
            locationLabel: label,
            deviceModel: DeviceModel.identifier(),
            timezone: TimeZone.current.identifier,
            batteryPercentAtStart: nil,
            trigger: SamplingPolicy.default.trigger(),
            includedMedia: captures.filter { chosen.contains($0.id) }.map(\.relativePath),
            window: window)

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
