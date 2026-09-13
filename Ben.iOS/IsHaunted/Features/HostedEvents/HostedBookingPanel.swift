import SwiftUI
import BenKit

/// The booking half of an event screen when the date is a hosted event's (item 235 phase 14).
///
/// The shipped app's RSVP button is the wrong tool here: a hosted event's places are asked for or picked on
/// its own page, with its own questions. So this panel says where the reader's booking stands, carries the
/// pass, lets a request or a hold go — and sends anybody who wants a place to the website page that books it.
struct HostedBookingPanel: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var store: HostedEventsStore?
    @State private var hosted: PublicHostedEvent?
    @State private var booking: MyHostedEventBooking?
    @State private var loaded = false
    @State private var busy = false
    @State private var message: String?
    @State private var openOnWebsite: URL?
    @State private var confirmLetGo = false
    @State private var bookingUnreadable = false

    private var signedIn: Bool { dependencies.session.me != nil }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if !loaded {
                ProgressView()
            } else if let booking {
                bookingState(booking)
            } else if bookingUnreadable {
                Text("Your booking couldn't be read just now. Pull down on My events, or open this again shortly.")
                    .font(.footnote).foregroundStyle(Theme.fog)
            } else {
                noBooking()
            }

            if let message {
                Text(message).font(.footnote).foregroundStyle(Theme.warning)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        .accessibilityIdentifier("hosted-booking-panel")
        // Keyed on sign-in so a session that arrives after the screen opens reads the booking again.
        .task(id: signedIn) {
            if store == nil { store = HostedEventsStore(api: dependencies.api) }
            await load()
        }
        .sheet(item: $openOnWebsite, onDismiss: { Task { await load() } }) { url in
            SafariSheet(url: url).ignoresSafeArea()
        }
        .confirmationDialog("Let these places go?", isPresented: $confirmLetGo, titleVisibility: .visible) {
            Button("Let them go", role: .destructive) { Task { await letGo() } }
        } message: {
            Text("They go back to the venue for somebody else.")
        }
    }

    @ViewBuilder
    private func bookingState(_ booking: MyHostedEventBooking) -> some View {
        let words = HostedEventWords.status(booking)
        StatusBadge(text: words.text, colour: words.colour)
        Text(HostedEventWords.what(booking)).font(.subheadline)

        switch booking.status {
        case .requested:
            Text("\(booking.organizationName ?? "The venue") will answer you. Nothing is held until they do.")
                .font(.footnote).foregroundStyle(Theme.fog)
        case .held:
            if let until = booking.holdExpiresUtc {
                Text("Nobody else can take them until \(until.formatted(date: .abbreviated, time: .shortened)), while the venue answers.")
                    .font(.footnote).foregroundStyle(Theme.fog)
            }
        case .confirmed:
            Text("Nothing is paid through this app — you settle up with \(booking.organizationName ?? "the venue").")
                .font(.footnote).foregroundStyle(Theme.fog)
        case .turnedDown, .cancelled, .expired:
            if let note = booking.decisionNote, !note.isEmpty {
                Text("“\(note)”").font(.footnote).foregroundStyle(Theme.fog)
            }
        }

        HStack(spacing: 10) {
            if booking.status == .confirmed {
                NavigationLink(value: AppRoute.eventPass(hostedEventId)) {
                    Label("Your pass", systemImage: "qrcode")
                }
                .buttonStyle(.borderedProminent)
                .accessibilityIdentifier("hosted-booking-pass")
            }
            if booking.status == .requested || booking.status == .held {
                Button("Let them go", role: .destructive) { confirmLetGo = true }
                    .buttonStyle(.bordered).disabled(busy)
            }
            if !booking.status.isLive, hosted?.isTakingBookings == true {
                websiteButton("Ask again")
            }
        }
    }

    @ViewBuilder
    private func noBooking() -> some View {
        if let hosted {
            if !hosted.isTakingBookings {
                Text(hosted.notTakingBookingsSentence ?? "This event isn't taking bookings.")
                    .font(.footnote).foregroundStyle(Theme.fog)
            } else {
                Text(hosted.bookingMode == .pick
                     ? "Places are chosen on the seating plan. It opens on the website, and your booking shows here afterwards."
                     : "Ask \(hosted.organizationName) for a place on the website. Your booking shows here afterwards.")
                    .font(.footnote).foregroundStyle(Theme.fog)
                if let price = hosted.dayPassPrice {
                    Text("A day pass is \(price.formatted(.currency(code: "USD"))), settled with \(hosted.organizationName) — never paid here.")
                        .font(.caption).foregroundStyle(Theme.fog)
                }
                websiteButton(hosted.bookingMode == .pick ? "Choose your places" : "Ask for a place")
            }
        } else {
            Text("This event's booking details couldn't be read just now.")
                .font(.footnote).foregroundStyle(Theme.fog)
        }
    }

    private func websiteButton(_ title: String) -> some View {
        Button {
            if let hosted {
                openOnWebsite = dependencies.environment.websiteURL(path: hosted.pagePath)
            }
        } label: {
            Label(title, systemImage: "safari")
        }
        .buttonStyle(.borderedProminent)
        .accessibilityIdentifier("hosted-booking-website")
    }

    private func load() async {
        guard let store else { return }
        if case .ok(let event) = await store.loadEvent(hostedEventId) { hosted = event }
        if signedIn {
            switch await store.loadMyBooking(hostedEventId) {
            case .ok(let mine):
                booking = mine
                bookingUnreadable = false
            case .failed, .sessionEnded, .rateLimited:
                // Never fall through to "Ask for a place" for somebody who may already have one.
                bookingUnreadable = booking == nil
            }
        } else {
            booking = nil
            bookingUnreadable = false
        }
        loaded = true
    }

    private func letGo() async {
        guard let store else { return }
        busy = true
        defer { busy = false }
        switch await store.withdraw(hostedEventId, reason: nil) {
        case .ok:
            message = "Let go. The places are back with the venue."
            await load()
        case .failed(let reason, _):
            message = reason ?? "That couldn't be done just now."
        case .sessionEnded:
            message = "Sign in again first."
        case .rateLimited:
            message = "Too many requests — try again shortly."
        }
    }
}
