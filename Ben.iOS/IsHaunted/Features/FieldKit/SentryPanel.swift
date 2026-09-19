import SwiftUI
import BenKit

/// Setting the phone down and letting it watch.
///
/// Everything here is about a device nobody is holding: what is worth waking up for, and what
/// the record has to contain for the person coming back at dawn to judge it.
struct SentryPanel: View {
    let session: ActiveFieldSession
    var camera: FieldCameraSession?

    @State private var config = SentryConfig.default
    @State private var showingSetup = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Label("Watching", systemImage: session.isArmed ? "eye" : "eye.slash")
                    .font(.caption)
                    .foregroundStyle(session.isArmed ? Theme.warning : Theme.fog)
                Spacer()
                if session.isArmed {
                    Text("armed")
                        .font(.caption2.bold())
                        .padding(.horizontal, 8).padding(.vertical, 3)
                        .background(Theme.warning.opacity(0.2), in: Capsule())
                        .foregroundStyle(Theme.warning)
                }
            }

            if let sentry = session.sentry {
                Text(watchingSummary(sentry))
                    .font(.caption).foregroundStyle(Theme.fog)
                Button(role: .destructive) {
                    Task { await session.disarm() }
                } label: {
                    Label("Stop watching", systemImage: "eye.slash")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("disarm-sentry")
            } else {
                Text("Set the device down and let it watch — anything past your levels is marked for you to find later.")
                    .font(.caption).foregroundStyle(Theme.fog)
                Button {
                    showingSetup = true
                } label: {
                    Label("Set up watching", systemImage: "eye")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("arm-sentry")
            }
        }
        .padding(12)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        .sheet(isPresented: $showingSetup) {
            SentrySetupSheet(session: session, config: $config)
        }
    }

    private func watchingSummary(_ sentry: SentryConfig) -> String {
        var parts: [String] = []
        if sentry.watchMagnetic { parts.append("magnetic field") }
        if sentry.watchSound { parts.append("sound") }
        if sentry.watchDeviceMovement { parts.append("the device being moved") }
        if sentry.watchSceneMotion { parts.append("movement in view") }
        return "Watching " + (parts.isEmpty ? "nothing" : parts.joined(separator: ", "))
    }
}

private struct SentrySetupSheet: View {
    @Environment(\.dismiss) private var dismiss
    let session: ActiveFieldSession
    @Binding var config: SentryConfig

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Toggle("Magnetic field", isOn: $config.watchMagnetic)
                        .accessibilityIdentifier("watch-magnetic")
                    Toggle("Sound", isOn: $config.watchSound)
                    Toggle("The device is moved", isOn: $config.watchDeviceMovement)
                        .accessibilityIdentifier("watch-device-movement")
                    Toggle("Movement in the camera's view", isOn: $config.watchSceneMotion)
                        .accessibilityIdentifier("watch-scene-motion")
                } header: {
                    Text("Wake up for")
                } footer: {
                    // The two motion triggers answer different questions and people conflate
                    // them, so the difference is spelled out rather than left to the labels.
                    Text("\"The device is moved\" is about the phone itself being knocked or picked up. \"Movement in view\" watches the picture through the camera — it can't tell a person from a curtain, so give it room.")
                }

                if config.watchDeviceMovement {
                    Section {
                        VStack(alignment: .leading) {
                            Text(String(format: "Sensitivity: %.2f g", config.deviceMovementThresholdG))
                                .font(.callout)
                            Slider(value: $config.deviceMovementThresholdG, in: 0.01...0.3)
                        }
                    } footer: {
                        Text("Lower catches somebody walking heavily past; higher needs a real knock.")
                    }
                }

                if config.watchSceneMotion {
                    Section {
                        VStack(alignment: .leading) {
                            Text(String(format: "Sensitivity: %.0f%% of the view",
                                        config.sceneMotionThreshold * 100))
                                .font(.callout)
                            Slider(value: $config.sceneMotionThreshold, in: 0.02...0.5)
                        }
                    } footer: {
                        Text("In a dark room the picture is noisy, so very low settings will trigger on nothing at all.")
                    }
                }

                if let problem = session.armingProblem(for: config) {
                    Section {
                        Label(problem, systemImage: "exclamationmark.triangle")
                            .font(.callout).foregroundStyle(Theme.warning)
                    }
                }

                Section {
                    Button {
                        Task { await session.arm(config); dismiss() }
                    } label: {
                        Text("Start watching").frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(session.armingProblem(for: config) != nil)
                    .accessibilityIdentifier("confirm-arm")
                } footer: {
                    Text("The screen stays on and dims while watching, so you can see it across a room. The phone won't lock itself and stop.")
                }
            }
            .navigationTitle("Watching")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
        }
    }
}
