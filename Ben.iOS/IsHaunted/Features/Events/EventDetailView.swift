import SwiftUI
import MapKit
import BenKit

/// One night, and this reader's seat on it (item 234, Ben 2026-09-10).
///
/// The app had **no event detail screen at all** — `AppRoute.eventDetail` was declared, deep-linked
/// to, and fell through to a placeholder. Ben's *"the person who is touring can confirm it on the
/// app - if they want"* needs somewhere to confirm it, so this is that somewhere.
///
/// Everything it shows is what the guest email already says, which is the point: the meeting point,
/// when to be there, who is leading it, a calendar entry and directions. One set of facts, two ways
/// of reading them.
struct EventDetailView: View {
    let eventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var store: EventsStore?
    @State private var event: PublicEventRecord?
    @State private var loading = true
    @State private var busy = false
    @State private var message: String?
    @State private var seats = 1

    private var signedIn: Bool { dependencies.session.me != nil }

    /// The ceiling on the picker, matching the server's own clamp.
    private static let maxSeats = 20

    var body: some View {
        Group {
            if loading {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let event {
                content(event)
            } else {
                ContentUnavailableView {
                    Label("Couldn't load this night", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text("It may have been taken off the calendar.")
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            }
        }
        .navigationTitle(event?.tourName ?? event?.title ?? "Event")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            if store == nil { store = EventsStore(api: dependencies.api) }
            await load()
        }
    }

    @ViewBuilder
    private func content(_ event: PublicEventRecord) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                header(event)
                seatPanel(event)
                meetingPoint(event)
                guides(event)

                if let description = event.description, !description.isEmpty {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("About the night").font(.headline).foregroundStyle(Theme.bone)
                        // The server sanitizes this, and the phone has no HTML view here — the
                        // tags are stripped rather than shown, which is better than printing
                        // "<p>" at somebody.
                        Text(description.strippingHTML)
                            .font(.body).foregroundStyle(Theme.fog)
                    }
                }

                if let note = message {
                    Text(note).font(.footnote).foregroundStyle(Theme.warning)
                }
            }
            .padding()
        }
    }

    private func header(_ event: PublicEventRecord) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            if let tour = event.tourName {
                Label(tour, systemImage: "figure.walk")
                    .font(.subheadline.weight(.medium)).foregroundStyle(Theme.ecto)
            }
            Text(event.title).font(.title2.weight(.semibold)).foregroundStyle(Theme.bone)
            Text(event.organizationName).font(.subheadline).foregroundStyle(Theme.fog)

            // The clock of the PLACE, not this phone's — see EventClock.
            Label(EventClock.dayAndTime(event.startDateTime, event.timeZoneId), systemImage: "clock")
                .font(.callout).foregroundStyle(Theme.fog)

            if let capacity = event.attendeeCapacity {
                Text("\(event.attendingCount) of \(capacity) places taken")
                    .font(.caption).foregroundStyle(Theme.fog)
            }
        }
    }

    // ── The seat ─────────────────────────────────────────────────────────────

    @ViewBuilder
    private func seatPanel(_ event: PublicEventRecord) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            switch event.mySeat?.status {
            case .requested:
                badge("Asked for", Theme.warning)
                Text("\(event.mySeat?.placesLabel ?? "A place") with \(event.organizationName). "
                     + "They'll confirm it — you'll hear from them.")
                    .font(.footnote).foregroundStyle(Theme.fog)

            case .reserved:
                badge("\(event.mySeat?.placesLabel ?? "A place") reserved", Theme.success)
                if event.mySeat?.acknowledgedUtc == nil {
                    // Ben: "the person who is touring can confirm it on the app - if they want."
                    // Optional in the strongest sense — nothing waits on it.
                    Text("Let them know you've seen it, if you like — it's optional.")
                        .font(.footnote).foregroundStyle(Theme.fog)
                    Button("Got it") { Task { await acknowledge() } }
                        .buttonStyle(.borderedProminent).disabled(busy)
                } else {
                    Text("You've confirmed you've seen it.")
                        .font(.footnote).foregroundStyle(Theme.fog)
                }

            case .turnedDown:
                badge("Not this time", Theme.fog)
                Text("\(event.organizationName) couldn't take this booking. Another date may have room.")
                    .font(.footnote).foregroundStyle(Theme.fog)

            case .none where event.flags.hasRsvpd:
                badge("You're coming", Theme.success)

            case .none:
                askPanel(event)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
        // Mist, the app's own panel colour. Haunt is an ACCENT — used as a background it came out
        // a bright purple with grey text on it, which is what the first build of this looked like.
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
    }

    @ViewBuilder
    private func askPanel(_ event: PublicEventRecord) -> some View {
        if !signedIn {
            Text(event.flags.rsvpBlockedReason ?? "Sign in to say you're coming.")
                .font(.footnote).foregroundStyle(Theme.fog)
        } else if event.flags.canRsvp {
            if event.tourName != nil {
                // A walk asks how many are coming; an ordinary evening does not, because one
                // sign-up there has always been one person.
                Picker("How many?", selection: $seats) {
                    ForEach(1...Self.maxSeats, id: \.self) { Text("\($0)").tag($0) }
                }
                .pickerStyle(.menu)
                Button("Ask for a place") { Task { await ask() } }
                    .buttonStyle(.borderedProminent).disabled(busy)
            } else {
                Button("I'm coming") { Task { await ask() } }
                    .buttonStyle(.borderedProminent).disabled(busy)
            }
        } else {
            Text(event.flags.rsvpBlockedReason ?? "Sign-ups for this one have closed.")
                .font(.footnote).foregroundStyle(Theme.fog)
        }
    }

    private func badge(_ text: String, _ colour: Color) -> some View {
        Text(text)
            .font(.caption.weight(.semibold))
            .padding(.horizontal, 8).padding(.vertical, 4)
            .background(colour.opacity(0.2), in: Capsule())
            .foregroundStyle(colour)
    }

    // ── Where, and who ───────────────────────────────────────────────────────

    @ViewBuilder
    private func meetingPoint(_ event: PublicEventRecord) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Where you meet").font(.headline).foregroundStyle(Theme.bone)

            if let address = event.location.exactAddress {
                Text(address).font(.body).foregroundStyle(Theme.fog)
            } else if event.location.isExactAddressHidden {
                Text("The exact address goes to people who are coming.")
                    .font(.footnote).foregroundStyle(Theme.fog)
            } else if let place = [event.location.city, event.location.state]
                        .compactMap({ $0 }).joined(separator: ", ").nilIfEmpty {
                Text(place).font(.body).foregroundStyle(Theme.fog)
            }

            HStack(spacing: 12) {
                if let url = directionsURL(event) {
                    Link(destination: url) {
                        Label("Directions", systemImage: "arrow.triangle.turn.up.right.circle")
                    }
                    .buttonStyle(.bordered)
                }
                AddToCalendarButton(event: listItem(event))
            }
        }
    }

    @ViewBuilder
    private func guides(_ event: PublicEventRecord) -> some View {
        if let guides = event.guides, !guides.isEmpty {
            VStack(alignment: .leading, spacing: 6) {
                Text(guides.count == 1 ? "Your guide" : "Your guides")
                    .font(.headline).foregroundStyle(Theme.bone)
                // Ben asked for the guide named "for safety" — the person walking into the dark
                // should know who they are meeting.
                ForEach(guides, id: \.displayName) { guide in
                    Label(guide.displayName, systemImage: "person.circle")
                        .font(.body).foregroundStyle(Theme.fog)
                }
            }
        }
    }

    /// Apple Maps for the meeting point. The coordinates are approximate by design, so the
    /// ADDRESS is preferred when the reader is entitled to it.
    private func directionsURL(_ event: PublicEventRecord) -> URL? {
        if let address = event.location.exactAddress,
           let encoded = address.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) {
            return URL(string: "https://maps.apple.com/?daddr=\(encoded)")
        }
        guard let lat = event.location.approximateLatitude,
              let lon = event.location.approximateLongitude else { return nil }
        return URL(string: "https://maps.apple.com/?daddr=\(lat),\(lon)")
    }

    /// The calendar sheet takes the list shape, which is all it reads.
    private func listItem(_ event: PublicEventRecord) -> PublicEventListItem {
        PublicEventListItem(
            id: event.id, urlName: nil, organizationId: event.organizationId,
            organizationName: event.organizationName,
            organizationUrlName: event.organizationUrlName,
            title: event.title, startDateTime: event.startDateTime,
            endDateTime: event.endDateTime, isAllDay: event.isAllDay,
            city: event.location.city, state: event.location.state,
            approximateLatitude: event.location.approximateLatitude,
            approximateLongitude: event.location.approximateLongitude,
            attendingCount: event.attendingCount, attendeeCapacity: event.attendeeCapacity,
            isOnline: event.meetingUrl != nil,
            tourName: event.tourName, tourUrlName: event.tourUrlName,
            timeZoneId: event.timeZoneId)
    }

    // ── Doing things ─────────────────────────────────────────────────────────

    private func load() async {
        loading = true
        defer { loading = false }
        event = await store?.loadOne(eventId, signedIn: signedIn)
        await refreshReminders()
    }

    private func ask() async {
        busy = true
        defer { busy = false }
        message = nil

        switch await store?.askForSeats(eventId, seats: seats) {
        case .success(let updated):
            event = updated
            await refreshReminders()
        case .failure(let error):
            // The server's own sentence — "Sign-ups for this event have closed" tells somebody to
            // look for another date, which "that didn't work" does not.
            message = error.message
        case .none:
            message = "That couldn't be sent just now."
        }
    }

    private func acknowledge() async {
        busy = true
        defer { busy = false }
        if let updated = await store?.acknowledgeSeat(eventId) { event = updated }
        else { message = "That couldn't be saved just now." }
    }

    /// Sets or clears the phone's own reminders to match the seat as it now stands.
    private func refreshReminders() async {
        guard let event else { return }
        await SeatReminders.schedule(for: event)
    }
}

private extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }

    /// Tags out, entities left alone — the server already sanitized this, and the phone only needs
    /// it readable.
    var strippingHTML: String {
        replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression)
            .replacingOccurrences(of: "&nbsp;", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
