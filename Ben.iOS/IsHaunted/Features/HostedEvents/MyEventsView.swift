import SwiftUI
import SafariServices
import BenKit

/// What I'm going to: every hosted event this person has booked, one row each (item 235 phase 14).
///
/// The same list as the website's /my-events. A confirmed booking has its pass one tap away — the thing
/// somebody opens this for, standing in a queue.
struct MyEventsView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var store: HostedEventsStore?
    @State private var bookings: [MyHostedEventBooking] = []
    @State private var state: LoadState = .loading

    enum LoadState: Equatable { case loading, loaded, failed(String?) }

    var body: some View {
        Group {
            switch state {
            case .loading where bookings.isEmpty:
                ProgressView("Loading your bookings…").frame(maxWidth: .infinity, maxHeight: .infinity)
            case .failed(let reason) where bookings.isEmpty:
                // A refusal is not an empty list.
                ContentUnavailableView {
                    Label("Couldn't load your bookings", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason ?? "Pull to try again.")
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            default:
                if bookings.isEmpty {
                    ContentUnavailableView(
                        "Nothing booked yet",
                        systemImage: "ticket",
                        description: Text("Events you ask to come to, or are given a place at, show here."))
                } else {
                    List {
                        let coming = bookings.filter { $0.status.isLive }
                        let over = bookings.filter { !$0.status.isLive }
                        if !coming.isEmpty {
                            Section { ForEach(coming) { row($0) } }
                        }
                        if !over.isEmpty {
                            Section("Not coming to these") { ForEach(over) { row($0) } }
                        }
                    }
                    .listStyle(.insetGrouped)
                }
            }
        }
        .navigationTitle("What I'm going to")
        .navigationBarTitleDisplayMode(.large)
        .refreshable { await load() }
        // Keyed on the account, so a session that resolves after the screen opened (a cold start straight
        // into a link) reads the list again instead of leaving "sign in again" up.
        .task(id: dependencies.session.me?.userId) {
            if store == nil { store = HostedEventsStore(api: dependencies.api) }
            await load()
        }
    }

    @ViewBuilder
    private func row(_ booking: MyHostedEventBooking) -> some View {
        let words = HostedEventWords.status(booking)
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(booking.eventName).font(.headline)
                Spacer()
                StatusBadge(text: words.text, colour: words.colour)
            }
            Text([booking.organizationName, booking.venueName, HostedEventWords.dates(booking.startsOn, booking.endsOn)]
                    .compactMap { $0 }.joined(separator: " · "))
                .font(.subheadline).foregroundStyle(Theme.fog)
            Text(HostedEventWords.what(booking)).font(.caption).foregroundStyle(Theme.fog)

            if let sessions = booking.sessions, !sessions.isEmpty {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(sessions, id: \.sessionId) { session in
                        HStack(spacing: 4) {
                            Text(session.startsAtUtc.formatted(.dateTime.weekday(.abbreviated).hour().minute()))
                            Text("· \(session.title)")
                            if session.waiting { Text("· waiting list").foregroundStyle(Theme.warning) }
                            if session.calledOff { Text("· called off").foregroundStyle(Theme.fog) }
                        }
                        .font(.caption)
                        .strikethrough(session.calledOff)
                    }
                }
            }

            HStack(spacing: 10) {
                if booking.status == .confirmed {
                    NavigationLink(value: AppRoute.eventPass(booking.hostedEventId)) {
                        Label("Pass", systemImage: "qrcode")
                    }
                    .buttonStyle(.borderedProminent)
                    .accessibilityIdentifier("my-events-pass-\(booking.hostedEventId.uuidString.lowercased())")
                }
                // Programme, menus, downloads and the room live on the event's own screen, which also links to its page.
                NavigationLink(value: AppRoute.eventHub(booking.hostedEventId)) {
                    Label("The event", systemImage: "sparkles")
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("my-events-hub-\(booking.hostedEventId.uuidString.lowercased())")
            }
            .padding(.top, 2)
        }
        .padding(.vertical, 4)
    }

    private func load() async {
        guard let store else { return }
        if bookings.isEmpty { state = .loading }
        switch await store.loadMine() {
        case .ok(let rows):
            bookings = rows
            state = .loaded
        case .failed(let reason, _):
            state = .failed(reason)
        case .sessionEnded:
            state = .failed("Sign in again to see your bookings.")
        case .rateLimited:
            state = .failed("Too many requests — try again shortly.")
        }
    }
}

/// A website page inside the app, for the parts that stay on the web — booking places, above all.
struct SafariSheet: UIViewControllerRepresentable {
    let url: URL

    func makeUIViewController(context: Context) -> SFSafariViewController {
        SFSafariViewController(url: url)
    }

    func updateUIViewController(_ controller: SFSafariViewController, context: Context) {}
}

extension URL: @retroactive Identifiable {
    public var id: String { absoluteString }
}
