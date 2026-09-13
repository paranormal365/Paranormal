import SwiftUI
import BenKit

/// An event's programme, night by night on the venue's clock, and signing up for its sessions (item 235 phase 14b).
///
/// **Times are the venue's**, with the zone named beside them — somebody checking from home before they travel
/// and somebody standing in the hotel read the same 11:00 PM.
///
/// **A full session still takes a name**, onto its waiting list, and says so on the button. Whoever gives a place
/// up moves the first person waiting up, and the server tells them.
struct ProgrammeView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var store: HostedEventsStore?
    @State private var programme: HostedEventProgramme?
    @State private var loaded = false
    @State private var failure: String?
    @State private var message: String?
    @State private var busySession: UUID?
    @State private var leaving: HostedEventSession?

    var body: some View {
        Group {
            if let programme {
                list(programme)
            } else if !loaded {
                ProgressView("Loading the programme…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let failure {
                ContentUnavailableView {
                    Label("Couldn't load the programme", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                } description: {
                    Text(failure)
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            } else {
                ContentUnavailableView("No programme yet", systemImage: "calendar",
                                       description: Text("The organizers haven't published what's on."))
            }
        }
        .navigationTitle("Programme")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if programme != nil,
               let url = dependencies.environment.url(for: HostedEventsStore.programmeCalendarEndpoint(hostedEventId)) {
                ToolbarItem(placement: .primaryAction) {
                    Link(destination: url) { Label("Add to calendar", systemImage: "calendar.badge.plus") }
                }
            }
        }
        .refreshable { await load() }
        .task(id: dependencies.session.me?.userId) {
            if store == nil { store = HostedEventsStore(api: dependencies.api) }
            await load()
        }
        .confirmationDialog(leaving.map { $0.mine?.waiting == true ? "Leave the waiting list?" : "Give up your place?" } ?? "",
                            isPresented: Binding(get: { leaving != nil }, set: { if !$0 { leaving = nil } }),
                            titleVisibility: .visible, presenting: leaving) { session in
            Button(session.mine?.waiting == true ? "Leave the waiting list" : "Give up my place", role: .destructive) {
                Task { await leave(session) }
            }
        } message: { session in
            Text(session.mine?.waiting == true
                 ? "You'll lose your place in the queue for \(session.title)."
                 : "The first person waiting for \(session.title) gets it.")
        }
    }

    @ViewBuilder
    private func list(_ programme: HostedEventProgramme) -> some View {
        List {
            if let message {
                Section { Text(message).font(.footnote).foregroundStyle(Theme.warning) }
            }
            if !programme.canSignUp, let why = programme.whyNotSignUp {
                Section { Text(why).font(.footnote).foregroundStyle(Theme.fog) }
            }
            ForEach(days(programme), id: \.day) { day in
                Section(dayTitle(day.day, programme.timeZone)) {
                    ForEach(day.sessions) { session in
                        sessionRow(session, programme)
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
    }

    @ViewBuilder
    private func sessionRow(_ session: HostedEventSession, _ programme: HostedEventProgramme) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(timeRange(session, programme.timeZone)).font(.caption.monospacedDigit()).foregroundStyle(Theme.fog)
                Spacer()
                if session.changed && !session.isCancelled { StatusBadge(text: "Changed", colour: Theme.warning) }
                if session.isCancelled { StatusBadge(text: "Called off", colour: Theme.fog) }
            }
            Text(session.title).font(.headline).strikethrough(session.isCancelled)
            let line = [session.where, session.ledBy.map { "with \($0)" }].compactMap { $0 }.joined(separator: " · ")
            if !line.isEmpty { Text(line).font(.subheadline).foregroundStyle(Theme.fog) }
            if session.isCancelled, let reason = session.cancelledReason, !reason.isEmpty {
                Text(reason).font(.footnote).foregroundStyle(Theme.warning)
            } else if let description = session.description, !description.isEmpty {
                Text(description).font(.footnote)
            }

            if session.requiresSignUp && !session.isCancelled {
                if let capacity = session.capacity {
                    Text(session.isFull ? "Full — \(session.placesTaken) of \(capacity) places taken"
                                        : "\(session.placesTaken) of \(capacity) places taken")
                        .font(.caption).foregroundStyle(Theme.fog)
                }
                signUpControls(session, programme)
            }
        }
        .padding(.vertical, 4)
        .accessibilityIdentifier("programme-session-\(session.id.uuidString.lowercased())")
    }

    @ViewBuilder
    private func signUpControls(_ session: HostedEventSession, _ programme: HostedEventProgramme) -> some View {
        if let mine = session.mine {
            HStack {
                if mine.waiting {
                    Label(waitingWords(session, mine), systemImage: "hourglass")
                        .font(.subheadline).foregroundStyle(Theme.warning)
                } else {
                    Label(mine.people == 1 ? "You're signed up" : "You're signed up for \(mine.people)", systemImage: "checkmark.circle.fill")
                        .font(.subheadline).foregroundStyle(Theme.success)
                }
                Spacer()
                Button(mine.waiting ? "Leave" : "Give up") { leaving = session }
                    .buttonStyle(.bordered).disabled(busySession != nil)
            }
        } else if programme.canSignUp {
            let title = session.isFull ? "Join the waiting list" : "Sign up"
            if busySession == session.id {
                ProgressView()
            } else if programme.maxPeople > 1 {
                Menu(title) {
                    ForEach(1...programme.maxPeople, id: \.self) { people in
                        Button(people == 1 ? "Just me" : "\(people) of us") { Task { await signUp(session, people: people) } }
                    }
                }
                .buttonStyle(.borderedProminent).disabled(busySession != nil)
            } else {
                Button(title) { Task { await signUp(session, people: 1) } }
                    .buttonStyle(.borderedProminent).disabled(busySession != nil)
            }
        }
    }

    /// Waiting while places are still free reads as a mistake unless it says why: the party is bigger than what's left.
    private func waitingWords(_ session: HostedEventSession, _ mine: MySessionPlace) -> String {
        let place = mine.position.map { "number \($0)" }
        if let capacity = session.capacity, capacity - session.placesTaken > 0, mine.people > capacity - session.placesTaken {
            let left = capacity - session.placesTaken
            return "There \(left == 1 ? "is 1 place" : "are \(left) places") left for \(mine.people) of you, so you're on the waiting list"
                + (place.map { " — \($0)" } ?? "")
        }
        return "On the waiting list" + (place.map { " — \($0)" } ?? "")
    }

    // ── the venue's clock ────────────────────────────────────────────────────

    private struct Day { let day: Date; let sessions: [HostedEventSession] }

    private func days(_ programme: HostedEventProgramme) -> [Day] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = programme.timeZone
        let grouped = Dictionary(grouping: programme.sessions) { calendar.startOfDay(for: $0.startsAtUtc) }
        return grouped.keys.sorted().map { Day(day: $0, sessions: grouped[$0]!.sorted { $0.startsAtUtc < $1.startsAtUtc }) }
    }

    private func dayTitle(_ day: Date, _ zone: TimeZone) -> String {
        day.formatted(Date.FormatStyle(timeZone: zone).weekday(.wide).month(.twoDigits).day(.twoDigits))
    }

    private func timeRange(_ session: HostedEventSession, _ zone: TimeZone) -> String {
        let style = Date.FormatStyle(date: .omitted, time: .shortened, timeZone: zone)
        let abbreviation = zone.abbreviation(for: session.startsAtUtc) ?? zone.identifier
        return "\(session.startsAtUtc.formatted(style)) – \(session.endsAtUtc.formatted(style)) \(abbreviation)"
    }

    // ── reads and writes ─────────────────────────────────────────────────────

    private func load() async {
        guard let store else { return }
        switch await store.loadProgramme(hostedEventId) {
        case .ok(let value):
            programme = value
            failure = nil
            if value?.changedSinceSeen == true { await store.markProgrammeSeen(hostedEventId) }
        case .failed(let reason, _):
            failure = reason ?? "Check your connection and try again."
        case .sessionEnded:
            failure = "Sign in again to see the programme."
        case .rateLimited:
            failure = "Too many requests — try again shortly."
        }
        loaded = true
    }

    private func signUp(_ session: HostedEventSession, people: Int) async {
        guard let store else { return }
        busySession = session.id
        defer { busySession = nil }
        apply(await store.signUp(hostedEventId, session: session.id, people: people))
    }

    private func leave(_ session: HostedEventSession) async {
        guard let store else { return }
        busySession = session.id
        defer { busySession = nil }
        apply(await store.leave(hostedEventId, session: session.id))
    }

    private func apply(_ result: LoadResult<HostedEventProgramme>) {
        switch result {
        case .ok(let value):
            programme = value
            message = nil
        case .failed(let reason, _):
            message = reason ?? "That couldn't be done just now."
        case .sessionEnded:
            message = "Sign in again first."
        case .rateLimited:
            message = "Too many requests — try again shortly."
        }
    }
}
