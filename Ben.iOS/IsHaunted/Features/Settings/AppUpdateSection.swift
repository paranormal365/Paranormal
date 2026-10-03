import SwiftUI
import UIKit
import BenKit

/// Profile's "App" section: which version this is, and whether a newer one is out.
///
/// Ben, 10/03/2026: "On your profile page, have a check for updated version which checks the webapi
/// to see if a newer version has been released." The site answers with the version Apple says is
/// live (`api/public/app-version/ios`), so this is right the moment a release goes out.
///
/// It checks once when Profile opens and again on request. Outside the signed-in branch on purpose:
/// an app that is out of date is most likely on a phone nobody has signed in on for a while.
struct AppUpdateSection: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.openURL) private var openURL
    @State private var status: AppUpdateStatus?
    @State private var checking = false

    private var current: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"
    }
    private var build: String {
        Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "—"
    }
    private var device: String { UIDevice.current.userInterfaceIdiom == .pad ? "iPad" : "iPhone" }

    var body: some View {
        Section {
            LabeledContent("Version", value: "\(current) (\(build))")
                .accessibilityIdentifier("settings-app-version")

            switch status {
            case .upToDate:
                Label("You're up to date", systemImage: "checkmark.circle")
                    .foregroundStyle(Theme.haunt)
                    .accessibilityIdentifier("settings-update-current")
            case .available(let live):
                VStack(alignment: .leading, spacing: 8) {
                    Label("Version \(live.latestVersion) is available", systemImage: "sparkles")
                        .font(.headline)
                        .foregroundStyle(Theme.bone)
                    if let notes = live.releaseNotes, !notes.isEmpty {
                        Text(notes)
                            .font(.callout)
                            .foregroundStyle(Theme.fog)
                    }
                    Button {
                        if let url = URL(string: live.storeUrl) { openURL(url) }
                    } label: {
                        Label("Update in the App Store", systemImage: "arrow.down.app")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(SignalPrimaryButtonStyle())
                    .accessibilityIdentifier("settings-update-open-store")
                }
                .padding(.vertical, 4)
                .accessibilityIdentifier("settings-update-available")
            case .needsNewerIOS(let live, let phoneOS):
                Label("Version \(live.latestVersion) needs iOS \(live.minimumOsVersion ?? "") or later. "
                      + "This \(device) has iOS \(phoneOS): update iOS in Settings first.",
                      systemImage: "exclamationmark.triangle")
                    .foregroundStyle(Theme.warning)
                    .accessibilityIdentifier("settings-update-needs-ios")
            case .failed(let reason):
                Label(reason, systemImage: "wifi.exclamationmark")
                    .foregroundStyle(Theme.warning)
                    .accessibilityIdentifier("settings-update-failed")
            case nil:
                EmptyView()
            }

            Button {
                Task { await check() }
            } label: {
                if checking {
                    HStack(spacing: 8) {
                        ProgressView()
                        Text("Checking…")
                    }
                } else {
                    Label("Check for updates", systemImage: "arrow.triangle.2.circlepath")
                }
            }
            .disabled(checking)
            .accessibilityIdentifier("settings-check-updates")
        } header: {
            Text("App")
        } footer: {
            Text("Asks ishaunted.com which version is in the App Store.")
        }
        .task {
            if status == nil { await check() }
        }
    }

    private func check() async {
        checking = true
        defer { checking = false }
        status = await AppUpdateChecker(api: dependencies.api)
            .check(current: current, phoneOS: UIDevice.current.systemVersion)
    }
}
