import SwiftUI
import BenKit

/// The lead's page (item 252): what they may launch right now, and the button.
///
/// Ben, 2026-09-28: "the planner wants the hunt to start at a specific time. That is what the button
/// does." Launch puts a card in the feed of everybody registered — public when the thing is public —
/// and tells those with the app. Nobody is started automatically; they tap Join.
struct LaunchForGroupView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    @State private var launchable: [LaunchableRecord]?
    @State private var problem: String?
    @State private var confirming: LaunchableRecord?
    @State private var launching = false

    var body: some View {
        SignalList {
            if let problem {
                Section { Text(problem).foregroundStyle(Theme.warning) }
            }
            if let launchable {
                if launchable.isEmpty {
                    Section {
                        Text("Nothing to launch right now. A tour date, event or investigation you lead can be launched from three hours before it starts until it ends.")
                            .font(.callout).foregroundStyle(Theme.fog)
                    }
                } else {
                    Section {
                        ForEach(launchable) { item in
                            row(item)
                        }
                    } footer: {
                        Text("Launch puts a card in the feed of everybody registered, and notifies those with the app. They tap Join to start their own session — nobody is started automatically.")
                    }
                }
            } else {
                ProgressView().frame(maxWidth: .infinity)
            }
        }
        .navigationTitle("Launch a session")
        .disabled(launching)
        .refreshable { await load() }
        .task { await load() }
        .confirmationDialog(confirming.map { "Launch \($0.title)?" } ?? "", isPresented: Binding(
            get: { confirming != nil }, set: { if !$0 { confirming = nil } }), titleVisibility: .visible,
            presenting: confirming) { item in
            Button("Launch for \(peopleText(item.people))") { Task { await launch(item) } }
                .accessibilityIdentifier("confirm-launch")
            Button("Cancel", role: .cancel) {}
        } message: { item in
            Text(item.isPublic
                 ? "The card goes in the feed for anyone to see, and the people registered who have the app are notified."
                 : "The card goes in the feed of the people registered, and those with the app are notified.")
        }
    }

    private func row(_ item: LaunchableRecord) -> some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text(item.title).font(.subheadline.weight(.semibold)).foregroundStyle(Theme.bone)
                PlaceTime(utc: item.startsUtc, zoneId: item.timeZoneId)
                    .font(.caption).foregroundStyle(Theme.fog)
                Text(detail(item)).font(.caption).foregroundStyle(Theme.fog)
            }
            Spacer()
            Button("Launch") { confirming = item }
                .buttonStyle(.signalPrimary)
                .accessibilityIdentifier("launch-\(item.id.uuidString.lowercased())")
        }
        .padding(.vertical, 4)
    }

    private func detail(_ item: LaunchableRecord) -> String {
        var parts = ["\(peopleText(item.people)) registered"]
        if let last = item.lastLaunchedUtc {
            parts.append("launched at \(last.readerFormatted(date: .omitted, time: .shortened))")
        }
        return parts.joined(separator: " · ")
    }

    private func peopleText(_ n: Int) -> String { n == 1 ? "1 person" : "\(n) people" }

    private func load() async {
        switch await dependencies.fieldLaunches.launchable() {
        case .ok(let items): launchable = items; problem = nil
        case .failed(let reason, _): problem = reason ?? "The server couldn't be reached."; launchable = launchable ?? []
        case .sessionEnded: problem = "Sign in to launch a session."; launchable = []
        case .rateLimited: problem = "Too many tries just now. Wait a moment and try again."
        }
    }

    private func launch(_ item: LaunchableRecord) async {
        launching = true
        defer { launching = false }
        switch await dependencies.fieldLaunches.launch(target: item.target, id: item.id) {
        case .ok(let outcome):
            problem = nil
            router.push(.launchDetail(outcome.launch.id, sent: LaunchSent(outcome)))
            await load()
        case .failed(let reason, _):
            problem = reason ?? "It couldn't be launched. Try again in a moment."
        case .sessionEnded:
            problem = "Your session ended — sign in again to launch."
        case .rateLimited:
            problem = "Too many tries just now. Wait a moment and try again."
        }
    }
}
