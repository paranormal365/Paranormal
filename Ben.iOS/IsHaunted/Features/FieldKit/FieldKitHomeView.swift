import SwiftUI
import UniformTypeIdentifiers
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
    /// The session a new one follows, so the sheet opens with its place, investigation and
    /// channels already chosen. Nil for a fresh start.
    @State private var startingLike: FieldSessionSummary?
    /// Asked when Start is pressed with a session still open — see `askToStart`.
    @State private var confirmingNewSession = false

    /// Opening a `.ben` from the Files app — the door for a bundle somebody else handed over.
    @State private var choosingBundle = false
    /// This account's sessions on the server that are not on this phone, for pulling back down.
    /// Everything the server holds for this account, as last read. Filtered against the phone's
    /// own sessions at draw time, so a session deleted from this phone appears here at once
    /// rather than after the tab is left and come back to.
    @State private var serverSessions: [FieldUploadClient.ServerSession] = []
    @State private var showingAllOnServer = false
    /// How many server sessions are listed before "Show all".
    private static let serverRowsShown = 3
    @State private var downloading: UUID?
    /// Group sessions a lead has launched for this person that are still open (item 252).
    @State private var happening: [FieldLaunchRecord] = []
    /// Whether this person leads anything that could be launched now.
    @State private var mayLaunch = false

    private var store: FieldSessionStore { dependencies.fieldKit }

    var body: some View {
        SignalList {
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
                        askToStart()
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

            // Item 252: the lead's own button, above what is happening — it is why they opened this.
            if mayLaunch {
                Section {
                    Button {
                        router.push(.launchForGroup)
                    } label: {
                        Label("Launch a session for your group", systemImage: "megaphone")
                    }
                    .accessibilityIdentifier("open-launch-for-group")
                } footer: {
                    Text("Something you lead is on now. Launching it lets everybody registered join from their feed.")
                }
            }

            // Item 252: a lead launched these. Join opens a session already set up for it; nobody is
            // started automatically (Ben: "instead of forcing them to join").
            if !happening.isEmpty {
                Section {
                    ForEach(happening) { launch in
                        HStack(spacing: 12) {
                            Button {
                                router.push(.launchDetail(launch.id, sent: nil))
                            } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(launch.title).font(.subheadline.weight(.semibold)).foregroundStyle(Theme.bone)
                                    Text("\(launch.organizationName) · started by \(launch.launchedByName)")
                                        .font(.caption).foregroundStyle(Theme.fog)
                                }
                                .frame(maxWidth: .infinity, alignment: .leading)
                            }
                            .buttonStyle(.plain)
                            Button("Join") { router.push(.joinLaunch(launch.id)) }
                                .buttonStyle(.signalPrimary)
                                .accessibilityIdentifier("happening-join")
                        }
                    }
                } header: {
                    Text("Happening now")
                } footer: {
                    Text("A lead started these. Join opens a session set up for it — nothing records until you press Start.")
                }
            }

            // Bringing a session in, rather than making one. Ben, 2026-09-16: "someone else can
            // share their .ben file with another person on the iphone and the other person can
            // view it like they had recorded it themselves" — and pulling your own back down.
            if case .ready = store.state {
                Section {
                    Button {
                        choosingBundle = true
                    } label: {
                        Label("Open a .ben file", systemImage: "doc.badge.arrow.up")
                    }
                    .accessibilityIdentifier("open-field-bundle")
                } footer: {
                    Text("A session somebody sent you — by AirDrop, in a message, from Files — opens here and plays exactly as it did for them.")
                }

                // Ben, 2026-09-27: public .ben files "from nearby", and by "looking up locations".
                Section {
                    Button {
                        router.push(.publicFieldSessions)
                    } label: {
                        Label("Find public sessions", systemImage: "square.stack.3d.down.right")
                    }
                    .accessibilityIdentifier("find-public-sessions")
                } footer: {
                    Text("Sessions other people published at public places — near you, or by searching a place or a town.")
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

            // After this phone's own sessions, because a person who has sent up every night for a
            // year has a long list here, and the sessions on the phone are the ones they came for.
            if case .ready = store.state, !onTheServer.isEmpty {
                let pullable = onTheServer.filter(\.canBePulledBack)
                let older = onTheServer.count - pullable.count
                let shown = showingAllOnServer ? pullable : Array(pullable.prefix(Self.serverRowsShown))
                Section {
                    ForEach(shown) { session in
                        HStack {
                            VStack(alignment: .leading, spacing: 3) {
                                Text(session.title).foregroundStyle(Theme.bone)
                                Text(serverDetail(session)).font(.caption).foregroundStyle(Theme.fog)
                            }
                            Spacer()
                            if downloading == session.id {
                                ProgressView()
                            } else {
                                Button {
                                    Task { await download(session) }
                                } label: {
                                    Image(systemName: "arrow.down.circle")
                                }
                                .disabled(downloading != nil)
                                .accessibilityLabel("Download \(session.title) to this phone")
                                .accessibilityIdentifier("download-field-session")
                            }
                        }
                    }
                    if pullable.count > Self.serverRowsShown {
                        Button(showingAllOnServer ? "Show fewer" : "Show all \(pullable.count)") {
                            showingAllOnServer.toggle()
                        }
                        .accessibilityIdentifier("show-all-server-sessions")
                    }
                } header: {
                    Text("On the server, not on this phone")
                } footer: {
                    // Only sessions the server can hand back get a button. The older kind is
                    // counted rather than listed with a Download that would only be refused.
                    // With nothing left to bring back, the heading and the sentence about
                    // downloading stood over an empty section (walk, 2026-10-02): say only what is
                    // true then — the older ones are on the website.
                    Text((pullable.isEmpty
                          ? "Everything that can come back to this phone is here."
                          : "Sessions you sent from another device, or cleared from this one. Downloading brings the whole night back — readings, marks and recordings.")
                         + (older > 0
                            ? " \(older == 1 ? "One older session was" : "\(older) older sessions were") sent before session files existed and can't be pulled back; they're still on the website."
                            : ""))
                }
            }
        }
        .navigationTitle("Field Kit")
        .sheet(isPresented: $starting, onDismiss: { startingLike = nil }) {
            StartSessionSheet(like: startingLike, onBrowsePublic: {
                starting = false
                router.push(.publicFieldSessions)
            }) { label, investigation, channels in
                await start(label: label, investigation: investigation, channels: channels)
            }
            .environment(dependencies)
            // On an iPad the default form sheet is shorter than the sheet, so Open the session —
            // its only action — sat below the fold where nobody would look for it. A page-sized
            // sheet shows it; on an iPhone this changes nothing.
            .presentationSizing(.page)
        }
        // Ben, 2026-09-27: "If they have recorded a session, just save it and ask if they want to
        // create a new one when they hit the start button instead of making them deal with it
        // immediately by having to delete it to start a new one."
        .confirmationDialog(openSessionQuestion, isPresented: $confirmingNewSession,
                            titleVisibility: .visible) {
            Button(openSessionIsRecording ? "Save it and start a new one" : "Start a new one instead") {
                Task { await closeOpenSessionThenStart() }
            }
            .accessibilityIdentifier("save-and-start-new")
            if let open = openSession {
                Button("Go back to it") { router.push(.fieldSession(open.id)) }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(openSessionIsRecording
                 ? "It stops recording and is kept with your other sessions, ready to play back or send."
                 : "Nothing has been recorded in it yet, so there is nothing to keep.")
        }
        .onChange(of: router.startAnotherSessionLike) { _, _ in answerStartAnother() }
        .onAppear { answerStartAnother() }
        .alert("Couldn't start the session",
               isPresented: Binding(get: { errorMessage != nil },
                                    set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) { errorMessage = nil }
        } message: {
            Text(errorMessage ?? "")
        }
        // Whatever door a bundle came through, this is where it is said when it would not open:
        // the screen the session would have appeared on.
        .alert("Couldn't open that session",
               isPresented: Binding(get: { store.importProblem != nil },
                                    set: { if !$0 { store.importProblem = nil } })) {
            Button("OK", role: .cancel) { store.importProblem = nil }
        } message: {
            Text(store.importProblem ?? "")
        }
        // `.data` beside the app's own type: a .ben mailed through something that did not keep
        // its type arrives as plain data, and refusing it there would be refusing the common case.
        .fileImporter(isPresented: $choosingBundle,
                      allowedContentTypes: [.fieldSessionBundle, .data]) { result in
            guard case .success(let url) = result else { return }
            Task { await FieldBundleOpener.open(url, dependencies: dependencies, router: router) }
        }
        .onAppear { store.load() }
        .task { await loadServerSessions() }
        // Again whenever the person signed in changes: what they may launch, and what they were
        // sent, are theirs (found by the lead's UI test, signed in after another account).
        .task(id: dependencies.session.me?.userId) { await loadLaunches() }
        .refreshable { await loadLaunches(); await loadServerSessions() }
    }

    /// What is happening now and whether this person may launch anything. Best-effort, like the
    /// server list: signed out or without a signal the sections simply do not appear.
    private func loadLaunches() async {
        guard dependencies.session.me != nil else { happening = []; mayLaunch = false; return }
        if case .ok(let mine) = await dependencies.fieldLaunches.mine() {
            happening = mine
            if !mine.isEmpty { PushRegistrar.shared.askIfUseful() }
        }
        if case .ok(let items) = await dependencies.fieldLaunches.launchable() { mayLaunch = !items.isEmpty }
    }

    /// The server's list, less what is already here. Best-effort: signed out, or no signal, and
    /// the section simply does not appear.
    private func loadServerSessions() async {
        guard dependencies.session.me != nil else { serverSessions = []; return }
        guard case .ok(let sessions) = await dependencies.fieldUpload.mySessions() else { return }
        serverSessions = sessions
    }

    /// The server's list, less what is already here.
    private var onTheServer: [FieldUploadClient.ServerSession] {
        let here = Set(store.sessions.map(\.id))
        return serverSessions.filter { !here.contains($0.deviceSessionId) }
    }

    private func serverDetail(_ session: FieldUploadClient.ServerSession) -> String {
        var parts: [String] = []
        if let started = session.startedAt {
            parts.append(started.readerFormatted(date: .abbreviated, time: .shortened))
        }
        parts.append("\(session.readingCount) readings")
        if session.markerCount > 0 { parts.append("\(session.markerCount) marked") }
        if !session.files.isEmpty { parts.append("\(session.files.count) recordings") }
        return parts.joined(separator: " · ")
    }

    /// Pulls one session down as the `.ben` it was sent as, and opens it.
    private func download(_ session: FieldUploadClient.ServerSession) async {
        downloading = session.id
        defer { downloading = nil }
        let destination = FileManager.default.temporaryDirectory
            .appendingPathComponent("download-\(session.id.uuidString.lowercased()).ben")
        switch await dependencies.fieldUpload.downloadBundle(sessionId: session.id, to: destination) {
        case .success(let url):
            await FieldBundleOpener.open(url, dependencies: dependencies, router: router,
                                         serverSessionId: session.id)
            try? FileManager.default.removeItem(at: url)
            await loadServerSessions()
        case .failure(let error):
            store.importProblem = error.message
        }
    }

    // MARK: - Starting

    private var openSession: FieldSessionSummary? {
        store.activeSessionId.flatMap { store.summary(for: $0) }.flatMap { $0.isOpen ? $0 : nil }
    }

    private var openSessionIsRecording: Bool { openSession?.isRecording == true }

    private var openSessionQuestion: String {
        guard let open = openSession else { return "Start a new session?" }
        return open.isRecording
            ? "“\(open.title)” is still recording"
            : "“\(open.title)” is set up but not started"
    }

    /// Straight to the sheet, unless a session is still open — then it is asked about first.
    private func askToStart() {
        if openSession != nil {
            confirmingNewSession = true
        } else {
            starting = true
        }
    }

    private func closeOpenSessionThenStart() async {
        do {
            let closed = try await store.closeOpenSession()
            if case .saved(let id) = closed { startingLike = store.summary(for: id) }
            starting = true
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// "Start another session" on a review lands here: the sheet opens filled in like that one.
    private func answerStartAnother() {
        guard let previous = router.startAnotherSessionLike else { return }
        router.startAnotherSessionLike = nil
        startingLike = store.summary(for: previous)
        // Another session in a group session a lead launched (item 252): "They can take multiple
        // sessions without having to do anything with them" — so no sheet, the same setup again.
        if let like = startingLike, like.fieldLaunchId != nil, openSession == nil {
            do {
                let id = try store.startSession(
                    locationLabel: like.locationLabel,
                    investigationId: like.investigationId,
                    investigationTitle: like.investigationTitle,
                    channels: like.channels,
                    orgCalendarEventId: like.orgCalendarEventId,
                    hostedEventId: like.hostedEventId,
                    eventTitle: like.eventTitle,
                    fieldLaunchId: like.fieldLaunchId)
                startingLike = nil
                router.push(.fieldSession(id))
            } catch {
                errorMessage = error.localizedDescription
            }
            return
        }
        askToStart()
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
    @Environment(AppDependencies.self) private var dependencies
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
                } else if let event = summary.eventTitle, !event.isEmpty, event != summary.title {
                    // A group's night it was joined from (item 252).
                    Text(event).font(.caption2).foregroundStyle(Theme.haunt)
                }
            }
            Spacer()
            // Sessions pile up, to be sent whenever (item 252, Ben: "They can choose to submit them
            // later"), so the list says which are still only on this phone.
            if !summary.isOpen && !summary.isImported {
                Text(summary.isUploaded ? "Sent" : "Not sent")
                    .font(.caption2.weight(.semibold))
                    .foregroundStyle(summary.isUploaded ? Theme.success : Theme.fog)
                    .accessibilityIdentifier(summary.isUploaded ? "session-sent" : "session-not-sent")
            }
            Image(systemName: "chevron.right").font(.caption).foregroundStyle(Theme.fog)
        }
        .padding(.vertical, 2)
    }

    private var detail: String {
        var parts: [String] = [summary.startedAt.readerFormatted(date: .abbreviated, time: .shortened)]
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
        // A session that arrived as a .ben says so, and says which kind: another person's night
        // handed over, or this person's own pulled back from the server.
        if summary.isPublicArchiveCopy {
            parts.append("public archive")
        } else if summary.wasRecordedElsewhere(thisDeviceId: DeviceModel.vendorIdentifier(), me: dependencies.session.me?.userId) {
            parts.append("shared with you")
        } else if summary.isImported {
            // Your own night, back on this phone — from the server, or from a file you had kept.
            parts.append(summary.serverSessionId != nil ? "from the server" : "opened from a file")
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

/// Where a session gets its name, its investigation and what it records.
///
/// Ben, 2026-09-27: "it asks where you are when you start the session. If you use the map, you
/// should be able to determine where they are and if they did not allow you to read their position,
/// then you should ask where they are. Also, if there is an investigation at the location they
/// should be able to just pick it from the dropdown list." So the phone answers "where" when it may
/// — a known place close by, else Apple's address — and the box stays editable; the investigation
/// happening here tonight is chosen for them; and sessions others published nearby are offered.
private struct StartSessionSheet: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    /// The session this one follows, when started from a review or after saving another — its
    /// place, investigation and channels are where somebody is most likely to carry on.
    var like: FieldSessionSummary? = nil
    /// "See them" on the nearby public sessions: closes this sheet and opens that screen.
    var onBrowsePublic: () -> Void = {}
    var onStart: (String?, MyInvestigation?, CaptureChannels) async -> Void

    @State private var label = ""
    /// The phone's last suggestion for the label. The label is still that suggestion — replaced when
    /// a better one arrives, never once somebody has typed their own — while the two are equal. A
    /// flag set in a deferred task and cleared by `onChange` raced on the iPad, and the footer then
    /// called a name the phone filled in "your own words".
    @State private var suggestedLabel: String?
    /// How the last attempt to say where the phone is went, so a miss is said rather than blank.
    private enum WhereOutcome { case named, noName, noFix }
    @State private var whereOutcome: WhereOutcome?
    private var labelIsSuggested: Bool { suggestedLabel != nil && label == suggestedLabel }
    @State private var channels: CaptureChannels = .default
    @State private var investigations: [MyInvestigation] = []
    /// Selected by id, not by value — MyInvestigation is a server-shaped record and making it
    /// Hashable to please a Picker would be the tail wagging the dog.
    @State private var chosenId: UUID?
    @State private var busy = false
    @State private var locator = FieldLocator()
    @State private var nearbyPlaces: [ArchivePlaceCandidate] = []
    @State private var nearbyPublic: [PublicArchiveSession] = []
    @State private var refusedPermission: AppPermission?

    var body: some View {
        NavigationStack {
            SignalForm {
                whereSection

                if !investigations.isEmpty {
                    Section {
                        Picker("Investigation", selection: $chosenId) {
                            Text("Not linked").tag(UUID?.none)
                            ForEach(orderedInvestigations) { investigation in
                                Text(investigationLabel(investigation))
                                    .tag(UUID?.some(investigation.investigationId))
                            }
                        }
                        .accessibilityIdentifier("start-investigation")
                    } footer: {
                        Text(chosenIsHere
                             ? "Chosen because it's here, today. You can change it."
                             : "Optional — you can link this session to an investigation later, when you review it.")
                    }
                }

                if !nearbyPublic.isEmpty {
                    Section {
                        Button {
                            onBrowsePublic()
                        } label: {
                            Label(nearbyPublic.count == 1
                                  ? "1 public session was recorded near here"
                                  : "\(nearbyPublic.count) public sessions were recorded near here",
                                  systemImage: "square.stack.3d.down.right")
                        }
                        .accessibilityIdentifier("start-nearby-public")
                    } footer: {
                        Text("Play what others recorded here before you start. They download to this phone.")
                    }
                }

                Section {
                    ForEach(CaptureChannels.orderedForDisplay, id: \.rawValue) { channel in
                        Toggle(isOn: Binding(
                            get: { channels.contains(channel) },
                            set: { isOn in
                                guard isOn, let needed = AppPermission.needed(for: channel) else {
                                    if isOn { channels.insert(channel) } else { channels.remove(channel) }
                                    return
                                }
                                // Asked now if iOS never has; refused (now or before) stays off and
                                // offers Settings — see AppPermission.ensure.
                                Task {
                                    if await needed.ensure() { channels.insert(channel) }
                                    else { refusedPermission = needed }
                                }
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
                    .buttonStyle(.signalPrimary)
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
            .permissionRefusedAlert($refusedPermission)
            .task {
                if let like {
                    label = like.locationLabel ?? ""
                    channels = like.channels
                    chosenId = like.investigationId
                }
                // Refused channels are not offered as if they will work: they start off.
                for channel in CaptureChannels.orderedForDisplay {
                    if let needed = AppPermission.needed(for: channel), needed.isRefused {
                        channels.remove(channel)
                    }
                }
                async let roster: Void = loadInvestigations()
                async let place: Void = findWhere()
                _ = await (roster, place)
            }
        }
        .interactiveDismissDisabled(busy)
    }

    // MARK: - Where

    @ViewBuilder
    private var whereSection: some View {
        Section {
            TextField("Where are you? (Old Mill, back bedroom)", text: $label)
                .accessibilityIdentifier("session-label")

            switch locator.access {
            case .allowed:
                if locator.isLocating {
                    HStack(spacing: 8) {
                        ProgressView()
                        Text("Finding where you are…").font(.caption).foregroundStyle(Theme.fog)
                    }
                } else if whereOutcome == .noFix {
                    // Said, not left blank: an empty field with no word about why looked broken.
                    HStack {
                        Text("Couldn't find where you are — type it, or try again.")
                            .font(.caption).foregroundStyle(Theme.fog)
                        Spacer()
                        Button("Try again") { Task { await findWhere() } }
                            .font(.caption)
                            .accessibilityIdentifier("start-locate-again")
                    }
                } else if whereOutcome == .noName, label.trimmingCharacters(in: .whitespaces).isEmpty {
                    Text("Nothing nearby to name this spot by — type where you are.")
                        .font(.caption).foregroundStyle(Theme.fog)
                } else if nearbyPlaces.count > 1 {
                    // Other known places close by, one tap each — the phone's first guess is not
                    // always the building somebody is standing in.
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack {
                            ForEach(nearbyPlaces.prefix(5)) { place in
                                Button(place.name ?? "Unnamed place") { useName(place.name) }
                                    .buttonStyle(.signalSecondary)
                                    .font(.caption)
                            }
                        }
                    }
                }
            case .notAsked:
                Button {
                    Task {
                        await locator.requestAccess()
                        await findWhere()
                    }
                } label: {
                    Label("Use my location", systemImage: "location")
                }
                .accessibilityIdentifier("start-use-location")
            case .refused:
                HStack {
                    Text("Location is off for IsHaunted, so type where you are.")
                        .font(.caption).foregroundStyle(Theme.fog)
                    Spacer()
                    OpenSettingsButton()
                }
            }
        } footer: {
            Text(whereFooter)
        }
    }

    private var whereFooter: String {
        if locator.access == .allowed, locator.point != nil, labelIsSuggested {
            return "Filled in from where your phone is. Change it to anything you'll recognize later."
        }
        return "Your own words. This is what you'll recognize the session by later."
    }

    private func useName(_ name: String?) {
        guard let name, !name.isEmpty else { return }
        suggestedLabel = name
        label = name
    }

    /// Where the phone is, and what that suggests: a name, nearby investigations, public sessions.
    private func findWhere() async {
        guard locator.access == .allowed else { return }
        whereOutcome = nil
        guard let point = await locator.locate() else {
            whereOutcome = .noFix
            return
        }

        async let places = dependencies.archiveActions.candidates(latitude: point.latitude,
                                                                   longitude: point.longitude)
        async let published = dependencies.publicArchive.nearby(latitude: point.latitude,
                                                                longitude: point.longitude)
        nearbyPlaces = (await places).sorted { $0.miles < $1.miles }
        if case .ok(let rows) = await published { nearbyPublic = rows }

        let name = StartSuggestions.placeName(candidates: nearbyPlaces, address: locator.address)
        whereOutcome = name == nil ? .noName : .named
        if label.trimmingCharacters(in: .whitespaces).isEmpty || labelIsSuggested {
            useName(name)
        }
        preselectIfHere()
    }

    // MARK: - Investigations

    private var orderedInvestigations: [MyInvestigation] {
        StartSuggestions.order(investigations, latitude: locator.point?.latitude,
                               longitude: locator.point?.longitude)
    }

    private var chosenInvestigation: MyInvestigation? {
        investigations.first { $0.investigationId == chosenId }
    }

    private var chosenIsHere: Bool {
        guard let chosen = chosenInvestigation else { return false }
        return chosen.isHappeningToday()
            && StartSuggestions.isHere(chosen, latitude: locator.point?.latitude,
                                       longitude: locator.point?.longitude)
    }

    private func investigationLabel(_ item: MyInvestigation) -> String {
        var suffix: [String] = []
        if StartSuggestions.isHere(item, latitude: locator.point?.latitude, longitude: locator.point?.longitude) {
            suffix.append("here")
        }
        if item.isHappeningToday() { suffix.append("today") }
        return suffix.isEmpty ? item.title : "\(item.title) · \(suffix.joined(separator: ", "))"
    }

    private func preselectIfHere() {
        guard chosenId == nil,
              let here = StartSuggestions.preselect(investigations, latitude: locator.point?.latitude,
                                                    longitude: locator.point?.longitude)
        else { return }
        chosenId = here.investigationId
    }

    /// Best-effort: no account, no signal, no investigations — none of which should stop
    /// somebody recording. The picker simply does not appear.
    private func loadInvestigations() async {
        guard dependencies.session.me != nil else { return }
        let store = InvestigationsStore(api: dependencies.api)
        await store.load()
        investigations = store.upcoming
        preselectIfHere()
    }
}
