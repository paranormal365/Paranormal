import SwiftUI
import BenKit

/// Joining the group's session a lead launched (item 252), from a feed card, a push, a QR code or
/// "Happening now". Ben, 2026-09-28: "Clicking the link would open the Field Kit into a session
/// where they don't have to complete picking the location" — and "instead of forcing them to join".
///
/// So: fetch the launch, make way for it (a session still open is saved, as Start always does),
/// and give way to the live session already set to the thing and its place. It opens pending;
/// nothing records until the person presses Start.
struct JoinLaunchView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    let launchId: UUID

    private enum Problem: Equatable {
        case gone
        case signIn
        case failed(String?)
    }

    @State private var problem: Problem?

    var body: some View {
        Group {
            switch problem {
            case nil:
                ProgressView("Joining the group's session…")
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            case .gone:
                ContentUnavailableView {
                    Label("This session isn't open", systemImage: "clock.badge.xmark")
                } description: {
                    Text("It has ended, or it was sent to other people. A launch stays open until six hours after the thing it was for.")
                }
            case .signIn:
                ContentUnavailableView {
                    Label("Sign in to join", systemImage: "person.crop.circle.badge.questionmark")
                } description: {
                    Text("This session is for the people the group sent it to. Sign in with the account you registered with.")
                } actions: {
                    Button("Sign in") { router.open(.profile) }
                        .buttonStyle(.borderedProminent)
                }
            case .failed(let reason):
                ContentUnavailableView {
                    Label("Couldn't join", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason ?? "The server couldn't be reached.")
                } actions: {
                    Button("Try again") { Task { await join() } }
                }
            }
        }
        .navigationTitle("Join")
        .navigationBarTitleDisplayMode(.inline)
        .accessibilityIdentifier("join-launch")
        .task { await join() }
    }

    private func join() async {
        problem = nil
        switch await dependencies.fieldLaunches.launch(launchId) {
        case .ok(let launch):
            let store = dependencies.fieldKit
            // Joined already and still open — tapping the card again goes back to it.
            if let open = store.activeSessionId.flatMap({ store.summary(for: $0) }),
               open.isOpen, open.fieldLaunchId == launch.id {
                router.replaceTop(with: .fieldSession(open.id))
                return
            }
            do {
                try await store.closeOpenSession()
                let id = try store.startSession(joining: launch)
                router.replaceTop(with: .fieldSession(id))
            } catch {
                problem = .failed(error.localizedDescription)
            }
        case .failed(_, let status) where status == 404:
            // Private and not theirs, ended, or never was: the server does not say which. Signed
            // out, the likeliest is that they are one of its people and simply not signed in.
            problem = dependencies.session.me == nil ? .signIn : .gone
        case .sessionEnded:
            problem = .signIn
        case .failed(let reason, _):
            problem = .failed(reason)
        case .rateLimited:
            problem = .failed("Too many tries just now. Wait a moment and try again.")
        }
    }
}
