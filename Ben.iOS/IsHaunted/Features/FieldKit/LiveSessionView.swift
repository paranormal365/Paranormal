import SwiftUI
import BenKit

/// The instrument panel: what the room is doing, right now.
///
/// Laid out for a phone held in one hand in the dark — the dial first, the clock and the run
/// time above it, everything else below in the order somebody reaches for it. On a wide screen
/// the dial and the log sit side by side rather than the dial growing absurd.
struct LiveSessionView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router
    @Environment(\.dismiss) private var dismiss
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.scenePhase) private var scenePhase

    let sessionId: UUID

    @State private var showingSettings = false
    @State private var camera = FieldCameraSession()
    /// Whole-session video and the camera's eye for Watch for Motion.
    @State private var video = SessionVideoRecorder()
    /// Remembered between sessions: somebody who turns it off does not want it back every night.
    @AppStorage("fieldkit.watch-for-motion") private var watchForMotionPreference = true
    /// Set while Stop is closing the video and the session, so it cannot be pressed twice.
    @State private var stopping = false
    /// A permission somebody reached for and had refused — said, with the way to Settings.
    @State private var refusedPermission: AppPermission?
    @State private var blackout = false

    /// Why Start refused, shown on the bar. Nil while nothing has refused.
    @State private var startProblem: String?
    @State private var showingEVP = false
    @State private var brightnessBeforeBlackout: CGFloat?
    @State private var showingLocationExplainer = false
    /// Whether this person has already been told what location is for. Somebody who said
    /// "Record without it" was asked again at the start of every session afterwards, because
    /// declining our own sheet never changes what the SYSTEM thinks — it stays undetermined
    /// forever. Asked once; the Position card is the way back.
    @AppStorage("fieldkit.location-explained") private var locationExplained = false
    @State private var choosingRoom = false
    @State private var askingForNote = false
    @State private var errorMessage: String?

    /// Room under the scrolling content for the pinned bar, so nothing ends up behind it.
    private static let barClearance: CGFloat = 96

    private var store: FieldSessionStore { dependencies.fieldKit }
    private var active: ActiveFieldSession? { store.active }
    private var summary: FieldSessionSummary? { store.summary(for: sessionId) }

    var body: some View {
        // The bar takes its OWN space in a stack rather than floating over the content on a
        // safe-area inset. The inset version did not shrink the scroll area, so controls sat
        // underneath the bar and a tap aimed at Mark landed on Stop — a mis-tap in the dark
        // that ends a recording. A stack cannot get this wrong.
        VStack(spacing: 0) {
            panel
            stopBarIfActive
        }
            .background(Theme.ink)
            .navigationTitle(summary?.title ?? "Session")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { toolbarItems }
            .sheet(isPresented: $showingSettings) { levelsSheet }
            .sheet(isPresented: $showingLocationExplainer) {
                LocationExplainerSheet(onAnswered: { locationExplained = true },
                                       onAllow: { await active?.requestLocation() })
            }
            .sheet(isPresented: $askingForNote) { noteComposer }
            .permissionRefusedAlert($refusedPermission)
            .sheet(isPresented: $choosingRoom) { roomSheet }
            .alert("Couldn't stop the session",
                   isPresented: Binding(get: { errorMessage != nil },
                                        set: { if !$0 { errorMessage = nil } })) {
                Button("OK", role: .cancel) { errorMessage = nil }
            } message: { Text(errorMessage ?? "") }
            // Blacked out: the screen goes dark so its light does not reach the recording, the
            // room, or anybody else in it. Everything underneath keeps running.
            //
            // A full-screen presentation rather than an overlay, for two reasons that only show
            // up in the dark: an overlay does not cover the tab bar, and it did not actually
            // block touches — so a hand brushing the screen could have hit Stop.
            .fullScreenCover(isPresented: $blackout) {
                BlackoutOverlay(session: active) { blackout = false }
            }
            .fullScreenCover(isPresented: $showingEVP) { evpSheet }
            .onChange(of: blackout) { _, isDark in applyBlackout(isDark) }
            .onChange(of: store.active?.channels) { _, channels in
                if channels?.contains(.video) == true {
                    camera.start()
                    syncVideo()
                } else {
                    // The clip is closed and filed BEFORE the camera goes, or it ends unfinished.
                    Task {
                        await finishVideo()
                        camera.stop()
                    }
                }
            }
            .onChange(of: camera.isRunning) { _, _ in syncVideo() }
            .onChange(of: store.active?.isRecording) { _, _ in syncVideo() }
            .onChange(of: store.active?.lastMotionDetectedAt) { _, moment in
                guard let moment, let active = store.active else { return }
                Task {
                    await video.motionDetected(at: moment, session: active, camera: camera,
                                               files: store.files)
                }
            }
            .onChange(of: store.active?.isArmed) { _, armed in
                UIApplication.shared.isIdleTimerDisabled = armed == true || blackout
            }
            // The session carries on while the app is put away — the background-audio mode is
            // declared for exactly this — and the stretch is marked so the review says so.
            // `.inactive` is not away: it is the control centre, a notification, the way out and
            // the way back in. Only `.background` counts, and only `.active` ends it.
            .onChange(of: scenePhase) { _, phase in
                Task {
                    switch phase {
                    case .background:
                        // iOS takes the camera as the app goes; the clip is closed and filed now,
                        // while it still can be, and a new one starts on the way back.
                        await finishVideo()
                        await store.active?.appWentToBackground()
                    case .active:
                        await store.active?.appReturned()
                        syncVideo()
                    default: break
                    }
                }
            }
            .onDisappear {
                let video = video, camera = camera
                Task {
                    await finishVideo(video: video, camera: camera)
                    video.stopFeeding()
                    camera.stop()
                }
                applyBlackout(false)
                UIApplication.shared.isIdleTimerDisabled = false
            }
            .task { await bringUp() }
    }

    @ViewBuilder
    private var panel: some View {
        if let active, active.sessionId == sessionId {
            // The bottom padding clears the pinned Stop bar. Without it the scroll content
            // sits UNDER the bar, and a tap aimed at the last control lands on Stop instead —
            // which is how a test aiming for "Mark" ended the session. In the field that is
            // somebody losing a recording to a mis-tap in the dark.
            if sizeClass == .regular {
                HStack(alignment: .top, spacing: 20) {
                    ScrollView {
                        instruments(active).frame(maxWidth: 420).padding(.bottom, Self.barClearance)
                    }
                    ScrollView { controls(active).padding(.bottom, Self.barClearance) }
                }
                .padding(.horizontal, 16)
            } else {
                ScrollView {
                    VStack(spacing: 18) {
                        instruments(active)
                        controls(active)
                    }
                    .padding(.horizontal, 16)
                    .padding(.bottom, Self.barClearance)
                }
            }
        } else if summary != nil {
            ProgressView("Bringing the instruments up")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            ContentUnavailableView {
                Label("That session isn't here", systemImage: "waveform.slash")
            } description: {
                Text("It may have been deleted.")
            }
        }
    }

    @ViewBuilder
    private var stopBarIfActive: some View {
        if let active, active.sessionId == sessionId { stopBar(active) }
    }

    @ToolbarContentBuilder
    private var toolbarItems: some ToolbarContent {
        ToolbarItem(placement: .primaryAction) {
            Button { showingSettings = true } label: {
                Image(systemName: "slider.horizontal.3")
            }
            .accessibilityLabel("Levels")
        }
    }

    @ViewBuilder
    private var evpSheet: some View {
        if let active { EVPModeView(session: active) }
    }

    @ViewBuilder
    private var levelsSheet: some View {
        if let active { LevelsSheet(session: active) }
    }

    @ViewBuilder
    private var noteComposer: some View {
        if let active {
            NoteComposerView(session: active).environment(dependencies)
        }
    }

    private func bringUp() async {
        store.load()
        await store.activate(sessionId)
        await store.active?.setWatchForMotion(watchForMotionPreference)
        if store.active?.channels.contains(.video) == true { camera.start() }
        syncVideo()
        if store.active?.channels.contains(.location) == true,
           store.active?.locationAuthorization == .notDetermined,
           !locationExplained {
            showingLocationExplainer = true
        }
    }

    /// Takes the screen brightness down to nothing and holds the phone awake, then puts the
    /// brightness back exactly where it was. Restoring the ORIGINAL value matters: leaving
    /// somebody's phone at zero after a session would look like a dead device.
    private func applyBlackout(_ isDark: Bool) {
        if isDark {
            if brightnessBeforeBlackout == nil {
                brightnessBeforeBlackout = UIScreen.main.brightness
            }
            UIScreen.main.brightness = 0
            UIApplication.shared.isIdleTimerDisabled = true
        } else {
            if let previous = brightnessBeforeBlackout {
                UIScreen.main.brightness = previous
                brightnessBeforeBlackout = nil
            }
            UIApplication.shared.isIdleTimerDisabled = active?.isArmed == true
        }
    }

    /// What the camera can see, so a device being left in a corner can be aimed before it is
    /// put down. Low resolution on purpose — this feed is for aiming and for spotting movement,
    /// not for the recording.
    @ViewBuilder
    private func viewfinder(_ active: ActiveFieldSession) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            ZStack(alignment: .topLeading) {
                CameraPreview(session: camera.session)
                    .frame(height: 200)
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                    .overlay {
                        // The frame lights up while the camera is seeing motion, as it does on
                        // the replay.
                        if let seen = active.lastMotionDetectedAt {
                            TimelineView(.periodic(from: .now, by: 0.5)) { context in
                                RoundedRectangle(cornerRadius: 12)
                                    .stroke(Theme.warning,
                                            lineWidth: context.date.timeIntervalSince(seen)
                                                < ReplayMotion.signSeconds ? 3 : 0)
                            }
                        }
                    }

                VStack(alignment: .leading, spacing: 6) {
                    if video.isRecording, let started = camera.clipStartedAt {
                        TimelineView(.periodic(from: .now, by: 1)) { context in
                            Label(SessionClock.elapsed(from: started, to: context.date),
                                  systemImage: "record.circle")
                                .font(.caption2.bold().monospacedDigit())
                                .padding(.horizontal, 8).padding(.vertical, 4)
                                .background(.black.opacity(0.55), in: Capsule())
                                .foregroundStyle(Theme.danger)
                        }
                        .accessibilityIdentifier("video-recording")
                    }
                    if active.watchForMotion || active.sentry?.watchSceneMotion == true {
                        Label("watching", systemImage: "eye")
                            .font(.caption2.bold())
                            .padding(.horizontal, 8).padding(.vertical, 4)
                            .background(.black.opacity(0.55), in: Capsule())
                            .foregroundStyle(Theme.warning)
                    }
                }
                .padding(8)

                // "Motion detected" for a few seconds after the camera saw something with the
                // phone still — the same sign the replay shows at that moment.
                if let seen = active.lastMotionDetectedAt {
                    TimelineView(.periodic(from: .now, by: 0.5)) { context in
                        if context.date.timeIntervalSince(seen) < ReplayMotion.signSeconds {
                            MotionDetectedSign()
                                .frame(maxWidth: .infinity, maxHeight: .infinity,
                                       alignment: .bottom)
                                .padding(.bottom, 10)
                        }
                    }
                }
            }
            .frame(height: 200)

            Toggle(isOn: Binding(
                get: { active.watchForMotion },
                set: { on in
                    if on, AppPermission.camera.isRefused {
                        refusedPermission = .camera
                        return
                    }
                    watchForMotionPreference = on
                    Task { await active.setWatchForMotion(on) }
                })
            ) {
                VStack(alignment: .leading, spacing: 1) {
                    Label("Watch for motion", systemImage: "figure.walk.motion")
                    Text("While the phone is still, movement in view is marked and photographed. The video keeps recording.")
                        .font(.caption2).foregroundStyle(Theme.fog)
                }
            }
            .tint(Theme.ecto)
            .accessibilityIdentifier("watch-for-motion")

            if let problem = camera.problem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.warning)
                if AppPermission.camera.isRefused { OpenSettingsButton() }
            }
            if let problem = video.problem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.warning)
                    .accessibilityIdentifier("video-problem")
            }
        }
        .accessibilityIdentifier("camera-preview")
    }

    /// Always on screen, never scrolled away. Somebody ending a session at 3am should not have
    /// to hunt for the control, and a session left running by accident is a flat battery.
    private func stopBar(_ active: ActiveFieldSession) -> some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 1) {
                if active.isRecording {
                    Text("\(active.readingCount) readings")
                        .font(.caption.monospacedDigit()).foregroundStyle(Theme.fog)
                } else {
                    Text("not started")
                        .font(.caption).foregroundStyle(Theme.fog)
                    // Nothing has happened yet, so nothing is lost by throwing it away. Ben:
                    // "They can delete the session if nothing happens and they want to free up
                    // space immediately." Only offered while pending — a recording is ended,
                    // never discarded, from this bar.
                    Button("Discard") {
                        Task { await discard() }
                    }
                    .font(.caption).foregroundStyle(Theme.danger)
                    .accessibilityIdentifier("discard-session")
                }
                if active.isReportingNow {
                    Text("over report level")
                        .font(.caption2.bold()).foregroundStyle(Theme.warning)
                }
                if let startProblem {
                    Text(startProblem)
                        .font(.caption2).foregroundStyle(Theme.danger)
                        .fixedSize(horizontal: false, vertical: true)
                        .accessibilityIdentifier("start-problem")
                }
            }
            Spacer()
            Button {
                blackout = true
            } label: {
                Image(systemName: "moon.fill")
                    .font(.headline)
                    .padding(.horizontal, 6)
            }
            .buttonStyle(.bordered)
            .accessibilityLabel("Blackout the screen")
            .accessibilityIdentifier("blackout")

            if active.isRecording {
                Button(role: .destructive) {
                    stop()
                } label: {
                    Label("Stop", systemImage: "stop.circle")
                        .font(.headline)
                        .padding(.horizontal, 8)
                }
                .buttonStyle(.borderedProminent)
                .tint(Theme.danger)
                .disabled(stopping)
                .accessibilityIdentifier("stop-field-session")
            } else {
                // Pending (item 215): set the room, the base level and the channels first, then
                // Start. Ben: "They may want to set everything up first and then start."
                Button {
                    Task {
                        startProblem = nil          // a fresh attempt, not last time's answer
                        do {
                            try await store.beginRecording(sessionId)
                        } catch {
                            // Ben, 2026-09-16: "I can set the base, but when I hit start, nothing happens." It was
                            // `try?`: every reason Start could refuse — a session a crash had left behind, a database
                            // that would not open — was swallowed, and the bar stayed on "not started".
                            startProblem = error.localizedDescription
                        }
                    }
                } label: {
                    Label("Start", systemImage: "record.circle")
                        .font(.headline)
                        .padding(.horizontal, 8)
                }
                .buttonStyle(.borderedProminent)
                .tint(Theme.success)
                .accessibilityIdentifier("start-recording")
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.bar)
    }

    @ViewBuilder
    private var roomSheet: some View {
        if let active {
            RoomSheet(current: active.room, visited: active.roomsVisited) { room in
                Task { await active.setRoom(room) }
            }
        }
    }

    // MARK: - Instruments

    @ViewBuilder
    private func instruments(_ active: ActiveFieldSession) -> some View {
        VStack(spacing: 16) {
            SessionClock(startedAt: active.startedAt, isRecording: active.isRecording,
                         isPending: !active.isRecording)
                .padding(.top, 8)

            RoomBar(room: active.room) { choosingRoom = true }

            if active.channels.contains(.video) {
                viewfinder(active)
            }

            if active.channels.contains(.magnetic) {
                AnalogMeterView(
                    value: active.magneticDeviationMilligauss,
                    range: active.meterRange,
                    reportAt: active.policy.reportAtMilligauss,
                    unit: "mG",
                    absoluteText: active.sample.magneticMilligauss
                        .map { String(format: "%.0f mG total", $0) },
                    caption: caption(for: active),
                    hasBaseline: active.baselines.magneticMicrotesla != nil)
            } else {
                Label("Magnetic field is switched off for this session.",
                      systemImage: "gauge.with.needle")
                    .font(.callout).foregroundStyle(Theme.fog)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            if active.channels.contains(.audio) {
                AudioLevelMeter(dbfs: active.sample.soundDbfs,
                                peakDbfs: active.sample.soundPeakDbfs,
                                baselineDbfs: active.baselines.soundDbfs,
                                reportAtDb: active.policy.reportAtDecibels)
                    .padding(12)
                    .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
            }

            PositionReadout(sample: active.sample.position,
                            headingDegrees: active.sample.headingDegrees,
                            relativeAltitudeMeters: active.sample.relativeAltitudeMeters,
                            isEnabled: active.channels.contains(.location)
                                && active.locationAuthorization.canLocate,
                            onUseLocation: locationAction(active))
                .padding(12)
                .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        }
    }

    /// What the position card's button does: ask the first time, and after a no, say so and offer
    /// Settings — iOS never shows its question twice.
    private func locationAction(_ active: ActiveFieldSession) -> (() -> Void)? {
        guard active.channels.contains(.location) else { return nil }
        switch active.locationAuthorization {
        case .notDetermined: return { showingLocationExplainer = true }
        case .denied, .restricted: return { refusedPermission = .location }
        case .authorized: return nil
        }
    }

    /// The honesty line under the dial. It says what the instrument IS, and when it should not
    /// be believed.
    private func caption(for active: ActiveFieldSession) -> String {
        if let calibration = active.sample.magneticCalibration, !calibration.isTrustworthy {
            return "Magnetometer needs calibrating — move the phone in a figure of eight. "
                 + "Readings won't be reported until it settles."
        }
        return "Magnetic field only — this is not an AC electromagnetic meter."
    }

    // MARK: - Controls

    @ViewBuilder
    private func controls(_ active: ActiveFieldSession) -> some View {
        VStack(spacing: 14) {
            HStack(spacing: 12) {
                Button {
                    Task { await active.setBaselines() }
                } label: {
                    Label(active.baselines.isSet ? "Reset base" : "Set base",
                          systemImage: "target")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .accessibilityIdentifier("set-base-level")

                if active.isRecording {
                    Button {
                        Task { await active.mark(kind: .manual) }
                    } label: {
                        Label("Mark", systemImage: "flag")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("mark-now")
                } else {
                    // Set-up time. The mark, EVP and capture controls arrive with Start — a
                    // mark before the clock began would belong to no moment (item 215).
                    Text("Set the room, base level and channels, then press Start.")
                        .font(.caption).foregroundStyle(Theme.fog)
                        .frame(maxWidth: .infinity)
                        .accessibilityIdentifier("pending-hint")
                }
            }
            if active.isRecording {

                HStack(spacing: 12) {
                    Button {
                        askingForNote = true
                    } label: {
                        Label("Note", systemImage: "square.and.pencil")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("open-note")

                    Button {
                        showingEVP = true
                    } label: {
                        Label("EVP", systemImage: "questionmark.bubble")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("open-evp")
                }

                FieldCaptureBar(session: active, camera: camera)
            }

            SentryPanel(session: active, camera: camera)

            channelToggles(active)

            markerLog(active)
        }
    }

    /// What this session is recording. Switching one off tears its stream down rather than
    /// leaving it running quietly — the reason to switch video off at 2am is the battery.
    private func channelToggles(_ active: ActiveFieldSession) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Recording").font(.caption).foregroundStyle(Theme.fog)

            ForEach(CaptureChannels.orderedForDisplay, id: \.rawValue) { channel in
                Toggle(isOn: Binding(
                        get: { active.channels.contains(channel) },
                        set: { isOn in
                            // Refused before, so iOS will not ask again: say so and offer
                            // Settings, rather than a switch that turns on and records nothing.
                            if isOn, let needed = AppPermission.needed(for: channel), needed.isRefused {
                                refusedPermission = needed
                                return
                            }
                            var channels = active.channels
                            if isOn { channels.insert(channel) } else { channels.remove(channel) }
                            Task { await active.setChannels(channels) }
                        })
                    ) {
                        VStack(alignment: .leading, spacing: 1) {
                            Label(channel.title, systemImage: channel.icon)
                            Text(channel.costNote)
                                .font(.caption2).foregroundStyle(Theme.fog)
                        }
                    }
                .tint(Theme.ecto)
                .accessibilityIdentifier("channel-\(channel.title.lowercased())")
            }
        }
        .padding(12)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
    }

    @ViewBuilder
    private func markerLog(_ active: ActiveFieldSession) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Marked").font(.caption).foregroundStyle(Theme.fog)
                Spacer()
                Text("\(active.readingCount) readings")
                    .font(.caption.monospacedDigit()).foregroundStyle(Theme.fog)
            }

            if active.markers.isEmpty {
                Text("Nothing marked yet. Anything past your report level lands here on its own.")
                    .font(.caption).foregroundStyle(Theme.fog)
            } else {
                ForEach(active.markers.prefix(8)) { marker in
                    HStack(spacing: 10) {
                        Image(systemName: marker.kind.isAutomatic ? "bolt.fill" : "flag.fill")
                            .font(.caption)
                            .foregroundStyle(marker.kind.isAutomatic ? Theme.warning : Theme.ecto)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(marker.kind.title).font(.caption).foregroundStyle(Theme.bone)
                            if let note = marker.note {
                                Text(note).font(.caption2).foregroundStyle(Theme.fog)
                            }
                        }
                        Spacer()
                        Text(marker.at, format: .dateTime.hour().minute().second())
                            .font(.caption2.monospacedDigit()).foregroundStyle(Theme.fog)
                    }
                    .accessibilityIdentifier("marker-row")
                }
            }
        }
        .padding(12)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
    }

    private func stop() {
        guard !stopping else { return }
        stopping = true
        Task {
            defer { stopping = false }
            // The video first: it is filed into the session, and the session only keeps what it
            // holds when it ends.
            await finishVideo()
            do {
                try await store.endSession(sessionId)
                // The review takes this screen's place — Back from it is Field Kit, not the
                // instruments of a session that is over.
                router.replaceTop(with: .fieldSessionReview(sessionId))
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    private func syncVideo() {
        guard let active = store.active, active.sessionId == sessionId else { return }
        Task { await video.sync(session: active, camera: camera, files: store.files) }
    }

    private func finishVideo() async {
        await finishVideo(video: video, camera: camera)
    }

    private func finishVideo(video: SessionVideoRecorder, camera: FieldCameraSession) async {
        guard let active = store.active, active.sessionId == sessionId else { return }
        await video.finish(session: active, camera: camera, files: store.files)
    }

    /// Throws away a session that never started. Nothing was logged, so nothing is lost; the
    /// directory goes with it and the space comes back at once.
    private func discard() async {
        await store.deactivate()
        do {
            try store.delete(sessionId)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

/// Where the base level and the report level are set.
private struct LevelsSheet: View {
    @Environment(\.dismiss) private var dismiss
    let session: ActiveFieldSession

    @State private var reportAtMilligauss: Double = 20
    @State private var reportAtDecibels: Double = 12
    @State private var debounceSeconds: Double = 3

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    LabeledContent("Magnetic base") {
                        Text(session.baselines.magneticMilligauss
                                .map { String(format: "%.0f mG", $0) } ?? "not set")
                    }
                    LabeledContent("Sound base") {
                        Text(session.baselines.soundDbfs
                                .map { String(format: "%.0f dB", $0) } ?? "not set")
                    }
                    Button("Take the room as it is now") {
                        Task { await session.setBaselines() }
                    }
                } header: {
                    Text("Base level")
                } footer: {
                    Text("What this room reads when nothing is happening. Everything is measured against it — an absolute field reading means nothing on its own, because the Earth alone is around 500 mG.")
                }

                Section {
                    VStack(alignment: .leading) {
                        Text("Magnetic field: \(Int(reportAtMilligauss)) mG from base")
                            .font(.callout)
                        Slider(value: $reportAtMilligauss, in: 5...200, step: 5)
                            .accessibilityIdentifier("report-level-magnetic")
                    }
                    VStack(alignment: .leading) {
                        Text("Sound: \(Int(reportAtDecibels)) dB above base").font(.callout)
                        Slider(value: $reportAtDecibels, in: 3...40, step: 1)
                    }
                    VStack(alignment: .leading) {
                        Text("Quiet period: \(Int(debounceSeconds))s").font(.callout)
                        Slider(value: $debounceSeconds, in: 1...60, step: 1)
                    }
                } header: {
                    Text("Report at")
                } footer: {
                    Text("Anything past these is marked for you to review. The quiet period stops one door slamming from filling the log with forty records.")
                }
            }
            .navigationTitle("Levels")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") {
                        Task {
                            await session.setPolicy(SamplingPolicy(
                                gaugeHz: session.policy.gaugeHz,
                                heartbeatSeconds: session.policy.heartbeatSeconds,
                                reportAtMilligauss: reportAtMilligauss,
                                reportAtDecibels: reportAtDecibels,
                                debounceSeconds: debounceSeconds))
                            dismiss()
                        }
                    }
                }
            }
            .onAppear {
                reportAtMilligauss = session.policy.reportAtMilligauss
                reportAtDecibels = session.policy.reportAtDecibels
                debounceSeconds = session.policy.debounceSeconds
            }
        }
    }
}

/// Asked before the system asks, so the system's one-line prompt is not the first explanation
/// anybody gets — and so a refusal is an informed one.
struct LocationExplainerSheet: View {
    @Environment(\.dismiss) private var dismiss
    /// Called whichever way this is answered, so it is never asked twice unbidden.
    var onAnswered: () -> Void
    var onAllow: () async -> Void

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 16) {
                Label("Where you were", systemImage: "location")
                    .font(.title3.bold())

                Text("Every reading and every photo can carry where you were standing when you took it, so a spike in the cellar isn't confused with one in the hall.")
                Text("Indoors a phone is usually accurate to somewhere between 20 and 50 metres — often the width of the whole building. Every reading carries its own accuracy so nobody mistakes it for room-level precision.")
                    .font(.callout).foregroundStyle(Theme.fog)
                Text("It stays on this device with the rest of the session.")
                    .font(.callout).foregroundStyle(Theme.fog)

                Spacer()

                Button {
                    onAnswered()
                    Task { await onAllow(); dismiss() }
                } label: {
                    Text("Continue").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .accessibilityIdentifier("location-continue")

                Button("Record without it") { onAnswered(); dismiss() }
                    .frame(maxWidth: .infinity)
                    .accessibilityIdentifier("location-decline")
            }
            .padding(20)
            .navigationBarTitleDisplayMode(.inline)
        }
        .presentationDetents([.medium])
    }
}
