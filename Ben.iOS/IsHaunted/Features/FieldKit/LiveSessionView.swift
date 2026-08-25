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
    @Environment(\.horizontalSizeClass) private var sizeClass

    let sessionId: UUID

    @State private var showingSettings = false
    @State private var camera = FieldCameraSession()
    @State private var blackout = false
    @State private var brightnessBeforeBlackout: CGFloat?
    @State private var showingLocationExplainer = false
    @State private var noteDraft = ""
    @State private var askingForNote = false
    @State private var errorMessage: String?

    private var store: FieldSessionStore { dependencies.fieldKit }
    private var active: ActiveFieldSession? { store.active }
    private var summary: FieldSessionSummary? { store.summary(for: sessionId) }

    var body: some View {
        panel
            .background(Theme.ink)
            .safeAreaInset(edge: .bottom) { stopBarIfActive }
            .navigationTitle(summary?.title ?? "Session")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { toolbarItems }
            .sheet(isPresented: $showingSettings) { levelsSheet }
            .sheet(isPresented: $showingLocationExplainer) {
                LocationExplainerSheet { await active?.requestLocation() }
            }
            .alert("Add a note", isPresented: $askingForNote) { noteAlertButtons }
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
            .onChange(of: blackout) { _, isDark in applyBlackout(isDark) }
            .onChange(of: store.active?.channels) { _, channels in
                if channels?.contains(.video) == true { camera.start() } else { camera.stop() }
            }
            .onChange(of: store.active?.isArmed) { _, armed in
                UIApplication.shared.isIdleTimerDisabled = armed == true || blackout
            }
            .onDisappear {
                camera.stop()
                applyBlackout(false)
                UIApplication.shared.isIdleTimerDisabled = false
            }
            .task { await bringUp() }
    }

    @ViewBuilder
    private var panel: some View {
        if let active, active.sessionId == sessionId {
            if sizeClass == .regular {
                HStack(alignment: .top, spacing: 20) {
                    ScrollView { instruments(active).frame(maxWidth: 420) }
                    ScrollView { controls(active) }
                }
                .padding(.horizontal, 16)
            } else {
                ScrollView {
                    VStack(spacing: 18) {
                        instruments(active)
                        controls(active)
                    }
                    .padding(.horizontal, 16)
                    .padding(.bottom, 24)
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
    private var levelsSheet: some View {
        if let active { LevelsSheet(session: active) }
    }

    @ViewBuilder
    private var noteAlertButtons: some View {
        TextField("What happened?", text: $noteDraft)
        Button("Save") {
            let note = noteDraft
            noteDraft = ""
            Task { await active?.mark(kind: .manual, note: note.isEmpty ? nil : note) }
        }
        Button("Cancel", role: .cancel) { noteDraft = "" }
    }

    private func bringUp() async {
        store.load()
        await store.activate(sessionId)
        if store.active?.channels.contains(.video) == true { camera.start() }
        if store.active?.channels.contains(.location) == true,
           store.active?.locationAuthorization == .notDetermined {
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

                if active.sentry?.watchSceneMotion == true {
                    Label("watching", systemImage: "eye")
                        .font(.caption2.bold())
                        .padding(.horizontal, 8).padding(.vertical, 4)
                        .background(.black.opacity(0.55), in: Capsule())
                        .foregroundStyle(Theme.warning)
                        .padding(8)
                }
            }
            if let problem = camera.problem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.warning)
            }
        }
        .accessibilityIdentifier("camera-preview")
    }

    /// Always on screen, never scrolled away. Somebody ending a session at 3am should not have
    /// to hunt for the control, and a session left running by accident is a flat battery.
    private func stopBar(_ active: ActiveFieldSession) -> some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 1) {
                Text("\(active.readingCount) readings")
                    .font(.caption.monospacedDigit()).foregroundStyle(Theme.fog)
                if active.isReportingNow {
                    Text("over report level")
                        .font(.caption2.bold()).foregroundStyle(Theme.warning)
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

            Button(role: .destructive) {
                stop()
            } label: {
                Label("Stop", systemImage: "stop.circle")
                    .font(.headline)
                    .padding(.horizontal, 8)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.danger)
            .accessibilityIdentifier("stop-field-session")
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.bar)
    }

    // MARK: - Instruments

    @ViewBuilder
    private func instruments(_ active: ActiveFieldSession) -> some View {
        VStack(spacing: 16) {
            SessionClock(startedAt: active.startedAt, isRecording: true)
                .padding(.top, 8)

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
                                && active.locationAuthorization.canLocate)
                .padding(12)
                .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
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

                Button {
                    Task { await active.mark(kind: .manual) }
                } label: {
                    Label("Mark", systemImage: "flag")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("mark-now")
            }

            Button {
                askingForNote = true
            } label: {
                Label("Mark with a note", systemImage: "square.and.pencil")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.bordered)

            FieldCaptureBar(session: active)

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
        Task {
            do {
                try await store.endSession(sessionId)
                router.push(.fieldSessionReview(sessionId))
            } catch {
                errorMessage = error.localizedDescription
            }
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
                    Task { await onAllow(); dismiss() }
                } label: {
                    Text("Continue").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)

                Button("Record without it") { dismiss() }
                    .frame(maxWidth: .infinity)
            }
            .padding(20)
            .navigationBarTitleDisplayMode(.inline)
        }
        .presentationDetents([.medium])
    }
}
