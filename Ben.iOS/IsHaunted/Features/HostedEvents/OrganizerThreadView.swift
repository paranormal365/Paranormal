import SwiftUI
import BenKit

/// Writing to the people running an event (event-scenario-gaps; Ben's scenario: "contact Jim either on our
/// app"). The same conversation as on the event's page on the website, oldest first, with the organizers'
/// answers standing out.
///
/// Words only. Offered from the event's screen to somebody with a booking there, and before the event starts
/// to anybody signed in with a question; the server tells anybody else nothing.
struct OrganizerThreadView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var thread: EventGuestThread?
    @State private var loaded = false
    @State private var error: String?
    @State private var note: String?
    @State private var draft = ""
    @State private var busy = false

    var body: some View {
        SignalList {
            if !loaded {
                Section { ProgressView("Loading…") }
            } else if let thread {
                Section {
                    if thread.messages.isEmpty {
                        Text("Nothing written yet.").foregroundStyle(Theme.fog)
                    }
                    ForEach(thread.messages) { message in
                        row(message)
                    }
                } footer: {
                    Text("Only \(thread.organizerName) and the people running this event see what you write here.")
                }

                Section {
                    if thread.canWrite {
                        TextField("A question about the event", text: $draft, axis: .vertical)
                            .lineLimit(2...6)
                            .accessibilityIdentifier("organizer-thread-body")
                        Button {
                            Task { await send() }
                        } label: {
                            Text(busy ? "Sending…" : "Send")
                        }
                        .buttonStyle(.signalPrimary)
                        .disabled(busy || draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .accessibilityIdentifier("organizer-thread-send")
                    } else if let why = thread.whyNotWrite {
                        Text(why).foregroundStyle(Theme.fog)
                    }
                    if let note { Text(note).foregroundStyle(Theme.ecto) }
                    if let error { Text(error).foregroundStyle(Theme.danger) }
                }
            } else {
                Section {
                    if let error {
                        Text(error).foregroundStyle(Theme.danger)
                    } else {
                        Text("Questions to the organizers close when the event starts, except for people with a booking.").foregroundStyle(Theme.fog)
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Write to the organizers")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
    }

    @ViewBuilder
    private func row(_ message: EventGuestThreadMessage) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(message.isMine ? "You" : message.authorName).font(.subheadline.weight(.semibold))
                Spacer()
                Text(message.postedUtc.formatted(.relative(presentation: .named))).font(.caption).foregroundStyle(Theme.fog)
            }
            Text(message.body)
        }
        .padding(.vertical, 2)
        // The organizers' answers stand out with a colored edge, the way they do in the event's room.
        .overlay(alignment: .leading) {
            if message.fromOrganizer {
                Rectangle().fill(Theme.ecto).frame(width: 3).offset(x: -12)
            }
        }
        .accessibilityIdentifier(message.fromOrganizer ? "organizer-thread-answer" : "organizer-thread-message")
    }

    private var store: HostedEventsStore { HostedEventsStore(api: dependencies.api) }

    private func load() async {
        switch await store.loadOrganizerThread(hostedEventId) {
        case .ok(let value): thread = value; error = nil
        case .failed(let reason, _): error = reason ?? "This couldn't be fetched just now."
        default: error = "This couldn't be fetched just now."
        }
        loaded = true
    }

    private func send() async {
        let words = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !words.isEmpty else { return }
        busy = true
        defer { busy = false }
        note = nil
        switch await store.writeToOrganizers(hostedEventId, body: words) {
        case .ok(let value): thread = value; draft = ""; error = nil; note = value.note
        case .failed(let reason, _): error = reason ?? "That couldn't be sent just now."
        default: error = "That couldn't be sent just now."
        }
    }
}
