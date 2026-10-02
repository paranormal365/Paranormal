import SwiftUI
import BenKit

/// The lead's QR code, scanned (item 252). Ben: "would the qr code scan also register someone to
/// join... maybe it sends a request to the leader and the leader has to confirm."
///
/// Somebody registered is straight in, like the feed card. Anybody else signs in and asks; the lead
/// says yes or no on their phone. Nobody waits to record: while the lead decides, a session can be
/// started here and kept on the phone — the yes is what lets it go to the group.
struct JoinByCodeView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    let token: String

    @State private var standing: JoinStandingRecord?
    @State private var problem: String?
    @State private var busy = false

    var body: some View {
        SignalList {
            if let problem {
                Section { Text(problem).foregroundStyle(Theme.warning) }
            }
            if let standing {
                Section {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(standing.title).font(.title3.weight(.semibold)).foregroundStyle(Theme.bone)
                        Text("\(standing.organizationName) · started by \(standing.launchedByName)")
                            .font(.callout).foregroundStyle(Theme.fog)
                    }
                    .padding(.vertical, 4)
                }
                content(standing)
            } else if problem == nil {
                ProgressView("Checking the code…").frame(maxWidth: .infinity)
            }
        }
        .navigationTitle("Join")
        .navigationBarTitleDisplayMode(.inline)
        .disabled(busy)
        .refreshable { await load() }
        .task(id: dependencies.session.me?.userId) { await load() }
        // Waiting for the lead: look again every few seconds, so a yes moves them straight into the
        // session — the notification says so too, but not everybody allows notifications.
        .task(id: standing?.standing) {
            guard standing?.standing == "pending" else { return }
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(6))
                if Task.isCancelled { return }
                await load()
            }
        }
    }

    @ViewBuilder
    private func content(_ standing: JoinStandingRecord) -> some View {
        switch standing.standing {
        case "sign-in":
            Section {
                Button("Sign in to join") { router.open(.profile) }
                    .accessibilityIdentifier("join-code-sign-in")
            } footer: {
                Text("This session is for the people the group invited. Sign in, and the lead can let you in.")
            }
        case "ask":
            Section {
                Button {
                    Task { await ask() }
                } label: {
                    Label("Ask to join", systemImage: "hand.raised")
                        .font(.headline)
                }
                .accessibilityIdentifier("join-code-ask")
            } footer: {
                Text("\(standing.launchedByName) is asked to let you in. You can record while you wait.")
            }
        case "pending":
            Section {
                Label("Waiting for \(standing.launchedByName)", systemImage: "hourglass")
                    .foregroundStyle(Theme.fog)
                    .accessibilityIdentifier("join-code-pending")
                recordButton(standing, joined: true)
            } footer: {
                Text("You'll get a notification when you're in. Sessions you record now stay on this phone, and go to the group once you're in.")
            }
        case "declined":
            Section {
                Label("\(standing.launchedByName) didn't add you", systemImage: "xmark.circle")
                    .foregroundStyle(Theme.fog)
                    .accessibilityIdentifier("join-code-declined")
                recordButton(standing, joined: false)
            } footer: {
                Text("You can still record. The sessions are yours, and stay on this phone.")
            }
        default:
            EmptyView()   // "in" goes straight to the session
        }
    }

    private func recordButton(_ standing: JoinStandingRecord, joined: Bool) -> some View {
        Button {
            start(standing, joined: joined)
        } label: {
            Label("Record now", systemImage: "record.circle")
        }
        .accessibilityIdentifier("join-code-record")
    }

    private func load() async {
        problem = nil
        switch await dependencies.fieldLaunches.standing(token: token) {
        case .ok(let found):
            standing = found
            if found.isIn, let launch = found.launch { await join(launch) }
        case .failed(_, let status) where status == 404:
            problem = "This code isn't open. It may have ended — ask the lead for tonight's."
        case .failed(let reason, _): problem = reason ?? "The server couldn't be reached."
        case .sessionEnded: problem = "Your session ended — sign in again."
        case .rateLimited: problem = "Too many tries just now. Wait a moment and try again."
        }
    }

    private func ask() async {
        busy = true
        defer { busy = false }
        switch await dependencies.fieldLaunches.ask(token: token) {
        case .ok(let found):
            standing = found
            // The lead's yes arrives as a notification; allowing them is what makes it reach this phone.
            PushRegistrar.shared.askIfUseful()
        case .failed(let reason, _): problem = reason ?? "The lead couldn't be asked just now."
        case .sessionEnded: problem = "Your session ended — sign in again."
        case .rateLimited: problem = "Too many tries just now. Wait a moment and try again."
        }
    }

    /// In: the same as the feed card — straight into a session set up for it.
    private func join(_ launch: FieldLaunchRecord) async {
        let store = dependencies.fieldKit
        if let open = store.activeSessionId.flatMap({ store.summary(for: $0) }), open.isOpen, open.fieldLaunchId == launch.id {
            router.replaceTop(with: .fieldSession(open.id))
            return
        }
        do {
            try await store.closeOpenSession()
            router.replaceTop(with: .fieldSession(try store.startSession(joining: launch)))
        } catch {
            problem = error.localizedDescription
        }
    }

    /// Recording while the lead decides (or after a no). A waiting session remembers the launch, so
    /// once they are in it can go to the group; after a no it is simply theirs.
    private func start(_ standing: JoinStandingRecord, joined: Bool) {
        Task {
            let store = dependencies.fieldKit
            do {
                try await store.closeOpenSession()
                let id = try store.startSession(locationLabel: standing.title,
                                                eventTitle: standing.title,
                                                fieldLaunchId: joined ? standing.launchId : nil)
                router.replaceTop(with: .fieldSession(id))
            } catch {
                problem = error.localizedDescription
            }
        }
    }
}
