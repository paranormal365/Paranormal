import SwiftUI
import BenKit

/// The management half of blocking: who you've blocked, and the way back.
///
/// The block itself happens on a post's menu, in the moment. This screen exists because a block
/// without a visible list is a decision you cannot review or reverse — and because App Review
/// looks for exactly that: a block control on the content AND somewhere the person can manage it.
struct BlockedAccountsView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var blocked: [BlockedUserRecord]?
    /// False once a fetch failed — "couldn't load" and "you block nobody" are different sentences.
    @State private var couldLoad = true
    @State private var toast: String?

    var body: some View {
        List {
            if let blocked {
                if blocked.isEmpty {
                    Section {
                        Label("You haven't blocked anyone.", systemImage: "checkmark.circle")
                            .foregroundStyle(Theme.fog)
                    }
                } else {
                    Section {
                        ForEach(blocked) { person in
                            HStack {
                                VStack(alignment: .leading) {
                                    Text(person.displayName)
                                    Text("Blocked \(person.dateCreated.formatted(date: .abbreviated, time: .omitted))")
                                        .font(.caption).foregroundStyle(Theme.fog)
                                }
                                Spacer()
                                Button("Unblock") {
                                    Task { await unblock(person) }
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                    } footer: {
                        Text("Unblocking shows their posts again. It doesn't re-follow anyone — that stays your choice.")
                    }
                }
            } else if !couldLoad {
                Section {
                    Label("Couldn't load your blocked accounts.", systemImage: "wifi.exclamationmark")
                        .foregroundStyle(Theme.warning)
                    Button("Try again") { Task { await load() } }
                }
            } else {
                Section { ProgressView() }
            }
        }
        .navigationTitle("Blocked accounts")
        .task { await load() }
        .overlay(alignment: .bottom) {
            if let toast {
                Text(toast)
                    .font(.callout)
                    .padding(.horizontal, 14).padding(.vertical, 8)
                    .background(.thinMaterial, in: Capsule())
                    .padding(.bottom, 12)
                    .task { try? await Task.sleep(for: .seconds(2)); self.toast = nil }
            }
        }
    }

    private func load() async {
        let result = await dependencies.feedActions.blockedUsers()
        blocked = result
        couldLoad = result != nil
    }

    private func unblock(_ person: BlockedUserRecord) async {
        guard await dependencies.feedActions.unblock(appUserId: person.appUserId) else {
            toast = "Couldn't unblock — try again."
            return
        }
        blocked?.removeAll { $0.appUserId == person.appUserId }
        toast = "Unblocked \(person.displayName)."
    }
}
