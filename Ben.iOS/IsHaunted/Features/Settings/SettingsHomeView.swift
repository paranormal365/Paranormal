import SwiftUI
import BenKit

/// The Profile section root: identity when signed in, the sign-in door when
/// not, and the developer environment picker.
struct SettingsHomeView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router
    @State private var showSignIn = false
    @State private var showRegister = false

    private var session: SessionStore { dependencies.session }

    var body: some View {
        List {
            if let me = session.me {
                Section("Account") {
                    LabeledContent("Email", value: me.email)
                    if me.isSuperAdmin {
                        Label("SuperAdmin", systemImage: "crown")
                            .foregroundStyle(Theme.haunt)
                    } else if me.isAdmin {
                        Label("Admin", systemImage: "checkmark.shield")
                            .foregroundStyle(Theme.haunt)
                    }
                    if me.isEntraOnly {
                        // Guid.Empty from api/me: an Entra identity with no
                        // linked local account — account setup comes in Slice 8.
                        Label("Microsoft account — finish setup on the website",
                              systemImage: "person.crop.circle.badge.questionmark")
                            .foregroundStyle(Theme.warning)
                    }
                    Button("Sign out", role: .destructive) {
                        Task { await session.signOut() }
                    }
                }
                Section("Security") {
                    NavigationLink(value: AppRoute.security) {
                        Label("Password & two-step sign-in", systemImage: "lock")
                    }
                }
            } else {
                Section {
                    Button {
                        showSignIn = true
                    } label: {
                        Label("Sign in", systemImage: "person.crop.circle.badge.checkmark")
                    }
                    Button {
                        showRegister = true
                    } label: {
                        Label("Create an account", systemImage: "person.badge.plus")
                    }
                } header: {
                    Text("Account")
                } footer: {
                    Text("You can browse the feed and public events without an account.")
                }
            }

            // Events has no tab on iPhone — Field Kit took the fifth slot — so this is where
            // public events live on a phone. On iPad the sidebar carries them and this row
            // would be a second door to the same room.
            if !router.isSection(.events) {
                Section {
                    NavigationLink(value: AppRoute.eventsList) {
                        Label("Public events", systemImage: "calendar")
                    }
                } footer: {
                    Text("Events groups have posted publicly, and the ones you're going to.")
                }
            }

            Section("Developer") {
                NavigationLink(value: AppRoute.developerSettings) {
                    LabeledContent("API environment", value: dependencies.environment.name)
                }
            }
        }
        .navigationTitle("Profile")
        .sheet(isPresented: $showSignIn) {
            SignInView().environment(dependencies)
        }
        .sheet(isPresented: $showRegister) {
            RegisterView().environment(dependencies)
        }
    }
}

/// Environment picker: Dev (localhost), UAT (ishaunted.com), or a custom base
/// URL — path-preserving, so `https://host/webapi` works. Switching signs out
/// and clears caches.
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
                Text("For a physical iPhone on your Wi-Fi, point this at your Mac's LAN address, e.g. http://192.168.1.50:5252 — see TESTING.md. Switching environments signs you out and clears cached responses.")
            }
        }
        .navigationTitle("API Environment")
    }
}
