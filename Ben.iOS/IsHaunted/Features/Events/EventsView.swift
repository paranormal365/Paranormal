import SwiftUI
import EventKit
import EventKitUI
import BenKit

/// Public events (iOS Slice 7), replacing the Slice-1 preview. Anyone may read this — it is
/// one of the site's front doors — and a signed-in person can reserve a place.
struct EventsView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var store: EventsStore?
    @State private var busyEventId: UUID?
    @State private var message: String?

    private var signedIn: Bool { dependencies.session.me != nil }

    var body: some View {
        Group {
            switch store?.state {
            case .none, .loading:
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)

            case .signedOut, .failed(nil):
                ContentUnavailableView {
                    Label("Couldn't load events", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text("The server couldn't be reached.")
                } actions: {
                    Button("Try again") { Task { await store?.load(signedIn: signedIn) } }
                        .buttonStyle(.borderedProminent)
                }

            case .failed(let reason):
                ContentUnavailableView {
                    Label("Couldn't load events", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason ?? "The server couldn't be reached.")
                } actions: {
                    Button("Try again") { Task { await store?.load(signedIn: signedIn) } }
                }

            case .loaded where store?.events.isEmpty == true:
                ContentUnavailableView {
                    Label("No upcoming events", systemImage: "calendar")
                } description: {
                    Text("Public events groups post will show up here.")
                }

            case .loaded:
                List(store?.events ?? []) { event in
                    EventRow(
                        event: event,
                        isAttending: store?.attending.contains(event.id) == true,
                        canRsvp: signedIn,
                        isBusy: busyEventId == event.id,
                        onRsvp: { Task { await rsvp(event) } },
                        onCancel: { Task { await cancel(event) } })
                }
                .listStyle(.insetGrouped)
            }
        }
        .navigationTitle("Events")
        .refreshable { await store?.load(signedIn: signedIn) }
        .task {
            let store = EventsStore(api: dependencies.api)
            self.store = store
            await store.load(signedIn: signedIn)
        }
        // Signing in mid-session turns "who's attending" from unknown to known.
        .onChange(of: dependencies.session.me?.userId) { _, _ in
            Task { await store?.load(signedIn: signedIn) }
        }
        .alert("Events", isPresented: Binding(
            get: { message != nil }, set: { if !$0 { message = nil } })
        ) {
            Button("OK") { message = nil }
        } message: {
            Text(message ?? "")
        }
    }

    private func rsvp(_ event: PublicEventListItem) async {
        busyEventId = event.id
        defer { busyEventId = nil }

        switch await store?.rsvp(event.id) {
        case .success:
            message = "You're on the list for \(event.title)."
        case .failure(let error):
            // The server's own sentence — "This event is full" sends somebody to another
            // date; "Couldn't RSVP" sends them to press the same button again.
            message = error.message
        case .none:
            break
        }
    }

    private func cancel(_ event: PublicEventListItem) async {
        busyEventId = event.id
        defer { busyEventId = nil }
        if await store?.cancelRsvp(event.id) != true {
            message = "Couldn't cancel that — try again in a moment."
        }
    }
}

struct EventRow: View {
    let event: PublicEventListItem
    let isAttending: Bool
    let canRsvp: Bool
    let isBusy: Bool
    let onRsvp: () -> Void
    let onCancel: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(event.title)
                .font(.headline).foregroundStyle(Theme.bone)
            Text(event.organizationName)
                .font(.subheadline).foregroundStyle(Theme.fog)

            HStack(spacing: 6) {
                Image(systemName: event.isOnline ? "video" : "mappin.and.ellipse")
                Text(event.startDateTime, format: .dateTime.month().day().hour().minute())
                if let place = event.placeLabel, !event.isOnline {
                    Text("· \(place)")
                }
            }
            .font(.caption).foregroundStyle(Theme.fog)

            HStack(spacing: 8) {
                if let left = event.spacesLeft {
                    Text(event.isFull ? "Full" : "\(left) space\(left == 1 ? "" : "s") left")
                        .font(.caption)
                        .foregroundStyle(event.isFull ? Theme.danger : Theme.fog)
                } else if event.attendingCount > 0 {
                    Text("\(event.attendingCount) going")
                        .font(.caption).foregroundStyle(Theme.fog)
                }

                Spacer()

                // Add to the phone's own calendar — the native counterpart of the website's
                // download-an-invite. Offered for anything upcoming, RSVP or not.
                if event.startDateTime > .now {
                    AddToCalendarButton(event: event)
                }

                if isBusy {
                    ProgressView()
                } else if isAttending {
                    Button("Cancel", role: .destructive, action: onCancel)
                        .buttonStyle(.bordered).controlSize(.small)
                } else if canRsvp && !event.isFull && event.startDateTime > .now {
                    Button("Reserve", action: onRsvp)
                        .buttonStyle(.borderedProminent).controlSize(.small)
                }
            }
        }
        .padding(.vertical, 4)
    }
}

/// Puts an event in the phone's calendar. Uses the system's own "add event" sheet rather
/// than writing directly, so the person picks the calendar and sees what is being saved —
/// and the app never needs full calendar access to do it.
struct AddToCalendarButton: View {
    let event: PublicEventListItem
    @State private var showSheet = false

    var body: some View {
        Button {
            showSheet = true
        } label: {
            Image(systemName: "calendar.badge.plus")
        }
        .buttonStyle(.bordered)
        .controlSize(.small)
        .accessibilityLabel("Add to calendar")
        .sheet(isPresented: $showSheet) {
            EventEditSheet(event: event)
        }
    }
}

struct EventEditSheet: UIViewControllerRepresentable {
    @Environment(\.dismiss) private var dismiss
    let event: PublicEventListItem

    func makeUIViewController(context: Context) -> UINavigationController {
        let store = EKEventStore()
        let ekEvent = EKEvent(eventStore: store)
        ekEvent.title = event.title
        ekEvent.startDate = event.startDateTime
        ekEvent.endDate = event.endDateTime
        ekEvent.isAllDay = event.isAllDay
        // The location is APPROXIMATE by design (the server snaps public coordinates), so the
        // calendar entry names the town, never a precise address it does not actually know.
        ekEvent.location = event.isOnline ? "Online" : event.placeLabel
        ekEvent.notes = "\(event.organizationName) · via IsHaunted"

        let controller = EKEventEditViewController()
        controller.event = ekEvent
        controller.eventStore = store
        controller.editViewDelegate = context.coordinator
        return UINavigationController(rootViewController: controller)
    }

    func updateUIViewController(_ controller: UINavigationController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(dismiss: { dismiss() }) }

    final class Coordinator: NSObject, EKEventEditViewDelegate {
        private let dismiss: () -> Void
        init(dismiss: @escaping () -> Void) { self.dismiss = dismiss }

        func eventEditViewController(_ controller: EKEventEditViewController,
                                     didCompleteWith action: EKEventEditViewAction) {
            dismiss()
        }
    }
}
