import SwiftUI
import BenKit

/// The Profile section root. Auth arrives in Slice 2; for now it hosts the
/// environment picker so every slice can be pointed at Dev or UAT.
struct SettingsHomeView: View {
    @Environment(AppDependencies.self) private var dependencies

    var body: some View {
        List {
            Section("Account") {
                Label("Sign in arrives in Slice 2", systemImage: "person.crop.circle.badge.clock")
                    .foregroundStyle(Theme.fog)
            }
            Section("Developer") {
                NavigationLink(value: AppRoute.developerSettings) {
                    LabeledContent("API environment", value: dependencies.environment.name)
                }
            }
        }
        .navigationTitle("Profile")
    }
}

/// Environment picker: Dev (localhost), UAT (ishaunted.com), or a custom base
/// URL — path-preserving, so `https://host/webapi` works. Switching clears the
/// session and caches.
struct DeveloperSettingsView: View {
    @Environment(AppDependencies.self) private var dependencies
    @State private var customURL: String = ""
    @State private var customError: String?

    var body: some View {
        List {
            Section("Environment") {
                ForEach(APIEnvironment.presets, id: \.self) { preset in
                    Button {
                        Task { await dependencies.switchEnvironment(to: preset) }
                    } label: {
                        HStack {
                            VStack(alignment: .leading) {
                                Text(preset.name).foregroundStyle(Theme.bone)
                                Text(preset.baseURL.absoluteString)
                                    .font(.caption).foregroundStyle(Theme.fog)
                            }
                            Spacer()
                            if dependencies.environment == preset {
                                Image(systemName: "checkmark").foregroundStyle(Theme.ecto)
                            }
                        }
                    }
                }
            }
            Section {
                TextField("https://host/base-path", text: $customURL)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .keyboardType(.URL)
                Button("Use custom base URL") {
                    guard let url = URL(string: customURL),
                          let scheme = url.scheme, ["http", "https"].contains(scheme)
                    else {
                        customError = "That doesn't look like an http(s) URL."
                        return
                    }
                    customError = nil
                    Task {
                        await dependencies.switchEnvironment(
                            to: APIEnvironment(name: "Custom", baseURL: url))
                    }
                }
                if let customError {
                    Text(customError).font(.caption).foregroundStyle(Theme.danger)
                }
            } header: {
                Text("Custom")
            } footer: {
                Text("Switching environments signs you out and clears cached responses.")
            }
        }
        .navigationTitle("API Environment")
    }
}
