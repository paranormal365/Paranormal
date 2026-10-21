import SwiftUI
import BenKit

/// Who came to each class, for whoever is on the door (Ben, 10/21/2026: door helpers get an app screen to
/// mark class attendance).
///
/// Each class that takes sign-ups, its time on the venue's clock, and a switch per person with a place.
/// Somebody still waiting was never let in, so they're listed but can't be marked.
struct ClassAttendanceView: View {
    let organizationId: UUID
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var classes: HostedEventClasses?
    @State private var loaded = false
    @State private var error: String?
    @State private var busy = false

    var body: some View {
        SignalList {
            if !loaded {
                Section { ProgressView("Loading the classes…") }
            } else if let classes {
                if classes.classes.isEmpty {
                    Section { Text("No classes at this event take sign-ups, so there's nobody to mark.").foregroundStyle(Theme.fog) }
                }
                if let error { Section { Text(error).foregroundStyle(Theme.danger) } }
                ForEach(classes.classes) { session in
                    Section {
                        if session.roster.in.isEmpty {
                            Text("Nobody has a place yet.").foregroundStyle(Theme.fog)
                        }
                        ForEach(session.roster.in) { line in
                            Toggle(isOn: Binding(
                                get: { line.isHere },
                                set: { here in Task { await mark(session, line, here) } })) {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(line.name)
                                    if line.people > 1 || line.isHelping {
                                        Text([line.people > 1 ? "\(line.people) people" : nil, line.isHelping ? "helping" : nil]
                                            .compactMap { $0 }.joined(separator: " · "))
                                            .font(.caption).foregroundStyle(Theme.fog)
                                    }
                                }
                            }
                            .disabled(busy)
                            .accessibilityIdentifier("class-here")
                        }
                    } header: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(session.title)
                            Text(when(session, in: classes.timeZone)).font(.caption).textCase(nil)
                        }
                    } footer: {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("\(session.roster.peopleThere) of \(session.roster.peopleIn) here")
                            if !session.roster.waiting.isEmpty {
                                Text("Waiting for a place: \(session.roster.waiting.map(\.name).joined(separator: ", ")). They can't be marked until they have one.")
                            }
                        }
                    }
                }
            } else {
                Section {
                    Text(error ?? "The classes couldn't be fetched just now. Pull down to try again.")
                        .foregroundStyle(Theme.warning)
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Who's at the classes")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
    }

    private var store: HostedEventsStore { HostedEventsStore(api: dependencies.api) }

    private func when(_ session: HostedEventClass, in zone: TimeZone) -> String {
        var style = Date.FormatStyle(date: .abbreviated, time: .shortened)
        style.timeZone = zone
        var end = Date.FormatStyle(date: .omitted, time: .shortened)
        end.timeZone = zone
        return ["\(session.startsAtUtc.formatted(style))–\(session.endsAtUtc.formatted(end))", session.where, session.ledBy.map { "with \($0)" }]
            .compactMap { $0 }.joined(separator: " · ")
    }

    private func load() async {
        switch await store.loadClasses(organizationId: organizationId, hostedEventId: hostedEventId) {
        case .ok(let value): classes = value; error = nil
        case .failed(let reason, _): error = reason
        default: error = nil
        }
        loaded = true
    }

    private func mark(_ session: HostedEventClass, _ line: ClassRosterLine, _ here: Bool) async {
        busy = true
        defer { busy = false }
        switch await store.setAttended(organizationId: organizationId, hostedEventId: hostedEventId,
                                       sessionId: session.sessionId, signUpId: line.signUpId, here: here) {
        case .ok(let roster):
            error = nil
            if var all = classes, let at = all.classes.firstIndex(where: { $0.sessionId == session.sessionId }) {
                all.classes[at].roster = roster
                classes = all
            }
        case .failed(let reason, _): error = reason ?? "That couldn't be marked just now."
        default: error = "That couldn't be marked just now."
        }
    }
}
