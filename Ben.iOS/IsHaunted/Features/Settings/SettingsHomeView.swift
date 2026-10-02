import SwiftUI
import BenKit

/// The Profile section root: identity when signed in, the sign-in door when
/// not, and the developer environment picker.
struct SettingsHomeView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router
    @State private var showSignIn = false
    @State private var showRegister = false
    /// Events this person may run the door at. The row appears only when there is one — for most guests there never is.
    @State private var doorDuties: [MyHostedEventDuty] = []

    private var session: SessionStore { dependencies.session }
    /// The name people see, from the feed profile. Profile showed only an email, so the person
    /// signed in could not tell from here what anybody else sees of them (walk, 2026-10-02).
    @State private var myProfile: FeedProfileRecord?

    static func initials(_ name: String) -> String {
        let letters = name.split(separator: " ").prefix(2).compactMap(\.first)
        return letters.isEmpty ? "?" : String(letters).uppercased()
    }

    var body: some View {
        SignalList {
            if let me = session.me {
                Section("Account") {
                    if let myProfile {
                        NavigationLink(value: AppRoute.feedProfile(me.userId)) {
                            HStack(spacing: 12) {
                                Text(Self.initials(myProfile.displayName))
                                    .font(.headline).foregroundStyle(.white)
                                    .frame(width: 44, height: 44)
                                    .background(Theme.gradient, in: Circle())
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(myProfile.displayName).font(.headline).foregroundStyle(Theme.bone)
                                    Text("\(myProfile.postCount) post\(myProfile.postCount == 1 ? "" : "s") · \(myProfile.followerCount) follower\(myProfile.followerCount == 1 ? "" : "s")")
                                        .font(.caption).foregroundStyle(Theme.fog)
                                }
                            }
                        }
                        .accessibilityIdentifier("my-profile")
                    }
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
                    NavigationLink(value: AppRoute.blockedAccounts) {
                        Label("Blocked accounts", systemImage: "hand.raised")
                    }
                    .accessibilityIdentifier("settings-blocked-accounts")
                }
                // App Review 5.1.1(v): an app that creates accounts must let you delete one
                // here, and not buried — a reviewer looks for it in the account settings. The
                // screen itself explains what survives and what does not.
                Section {
                    NavigationLink(value: AppRoute.deleteAccount) {
                        Label("Delete account", systemImage: "person.crop.circle.badge.xmark")
                            .foregroundStyle(Theme.danger)
                    }
                    .accessibilityIdentifier("settings-delete-account")
                } footer: {
                    Text("Removes your name, sign-in and contact details. What you posted for a group stays with that group.")
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

            // Investigations gives up its tab on iPhone to Field Kit and My Cases for a member, and
            // then nothing led to it: the group's work was unreachable on a phone for anybody
            // with no case of their own (walk, 2026-10-02). It lives here then, like Events.
            if !router.isSection(.investigations), dependencies.surfaces.surfaces.hasInvestigations {
                Section {
                    NavigationLink(value: AppRoute.investigationsList) {
                        Label("Investigations", systemImage: "binoculars")
                    }
                    .accessibilityIdentifier("settings-investigations")
                } footer: {
                    Text("The visits your group is working, and the cases behind them.")
                }
            }

            // The guest's own copy of what they offered at somebody's public event. Gated on
            // being signed in, and that gate is the point: submissions belong to an account, so
            // offering the row to a signed-out visitor would be a link that can only ever end in
            // a refusal — the dead-end click the site made policy against.
            if session.me != nil {
                Section {
                    NavigationLink(value: AppRoute.myEvents) {
                        Label("What I'm going to", systemImage: "ticket")
                    }
                    .accessibilityIdentifier("settings-my-events")
                } footer: {
                    Text("Events you've booked, and your passes — which work without a signal.")
                }

                if !doorDuties.isEmpty {
                    Section {
                        NavigationLink(value: AppRoute.doorDuties) {
                            Label("Doors I'm running", systemImage: "door.left.hand.open")
                        }
                        .accessibilityIdentifier("settings-door-duties")
                    } footer: {
                        Text("Let people in at events you're helping at — by name or by scanning their pass, with or without a signal.")
                    }
                }

                Section {
                    NavigationLink(value: AppRoute.myEvidence) {
                        Label("My evidence", systemImage: "photo.on.rectangle.angled")
                    }
                    .accessibilityIdentifier("settings-my-evidence")
                } footer: {
                    Text("Photos and recordings you've offered at public events — yours to keep, "
                       + "and to add to the archive of the place they were taken at.")
                }
            }

            // Deliberately outside the signed-in branch. App Review works through a build
            // without an account for as long as it can, and "where does this app say what it
            // does with my data" must be answerable from that state.
            Section {
                NavigationLink(value: AppRoute.about) {
                    Label("About & Privacy", systemImage: "hand.raised")
                }
                .accessibilityIdentifier("settings-about")
            } footer: {
                Text("What IsHaunted does with what you give it — and what it doesn't.")
            }

            #if DEBUG
            // Debug builds only. The base URL is not a user setting: a shipped app that can be
            // pointed at localhost or an arbitrary host is one support call away from a person
            // who cannot tell a broken app from a mistyped address. `AppRoute.developerSettings`
            // is unreachable in release — nothing else navigates to it and DeepLinkParser does
            // not produce it — so removing the row removes the screen.
            Section("Developer") {
                NavigationLink(value: AppRoute.developerSettings) {
                    LabeledContent("API environment", value: dependencies.environment.name)
                }
            }
            #endif
        }
        .navigationTitle("Profile")
        // Asked once per account; kept on the phone by the store, so a door opened with no signal is still offered.
        .task(id: session.me?.userId) {
            if let id = session.me?.userId {
                let result = await dependencies.api.load(
                    Endpoint(.get, "api/feed/profile/\(id.uuidString.lowercased())"), as: FeedProfileRecord.self)
                if case .ok(let record) = result { myProfile = record } else { myProfile = nil }
            } else {
                myProfile = nil
            }
            guard session.me != nil else { doorDuties = []; return }
            switch await DoorStore(api: dependencies.api).loadDuties() {
            case .live(let duties), .saved(let duties, _): doorDuties = duties
            case .failed: break
            }
        }
        .sheet(isPresented: $showSignIn) {
            SignInView().environment(dependencies)
        }
        .sheet(isPresented: $showRegister) {
            RegisterView().environment(dependencies)
        }
    }
}

/// Environment picker: Dev (localhost), the live site, or a custom base URL — path-preserving,
/// so `https://host/webapi` works. Switching signs out and clears caches.
///
/// DEBUG builds only. Nothing in a release build navigates here.
struct DeveloperSettingsView: View {
    @Environment(AppDependencies.self) private var dependencies
    @State private var customURL: String = ""
    @State private var customError: String?

    var body: some View {
        SignalList {
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
