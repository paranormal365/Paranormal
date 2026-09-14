import Foundation
import Network
import BenKit

/// Sends the room outbox whenever there's a chance it will get through, and keeps the Share Extension's list of
/// events current (item 235 phase 14d).
///
/// **When:** the app coming to the front, and the network coming back while it is open — a guest walking up out of
/// the cellar with the app still on screen should not have to do anything. **Never** while signed out: the posts
/// belong to an account, and a post sent without one would only be refused.
@MainActor
final class RoomOutboxDrain {
    private let dependencies: AppDependencies
    private var monitor: NWPathMonitor?
    private var wasSatisfied = true

    init(dependencies: AppDependencies) {
        self.dependencies = dependencies
    }

    /// Starts watching the network. Calling it again is harmless.
    func start() {
        guard monitor == nil else { return }
        let monitor = NWPathMonitor()
        monitor.pathUpdateHandler = { [weak self] path in
            let satisfied = path.status == .satisfied
            Task { @MainActor in
                guard let self else { return }
                defer { self.wasSatisfied = satisfied }
                if satisfied && !self.wasSatisfied { await self.send() }
            }
        }
        monitor.start(queue: DispatchQueue(label: "com.ishaunted.room-outbox.network"))
        self.monitor = monitor
    }

    func send() async {
        guard dependencies.session.me != nil, !RoomOutbox.shared().waiting().isEmpty else { return }
        _ = await RoomOutboxSender(store: HostedEventsStore(api: dependencies.api)).sendWaiting()
    }

    /// The events this person could share photos to: ones they're confirmed at, and doors they run (the team posts
    /// too). What each room allows is learned when it's opened, and kept across this refresh.
    func refreshShareableEvents() async {
        guard dependencies.session.me != nil else { return }
        let store = HostedEventsStore(api: dependencies.api)
        var events: [ShareableEvents.Event] = []

        if case .ok(let mine) = await store.loadMine() {
            events += mine.filter { $0.status == .confirmed }.map {
                ShareableEvents.Event(hostedEventId: $0.hostedEventId, eventName: $0.eventName, organizationName: $0.organizationName,
                                      startsOn: $0.startsOn, endsOn: $0.endsOn, canAddPhotos: nil, needsPhotoConsent: nil,
                                      photoNotice: nil, hostNames: [])
            }
        } else {
            return // Couldn't ask: keep the list that's there rather than emptying it.
        }
        switch await DoorStore(api: dependencies.api).loadDuties() {
        case .live(let duties), .saved(let duties, _):
            for duty in duties where !events.contains(where: { $0.hostedEventId == duty.hostedEventId }) {
                events.append(ShareableEvents.Event(hostedEventId: duty.hostedEventId, eventName: duty.eventName,
                                                    organizationName: duty.organizationName, startsOn: duty.startsOn,
                                                    endsOn: duty.endsOn, canAddPhotos: nil, needsPhotoConsent: nil,
                                                    photoNotice: nil, hostNames: []))
            }
        case .failed:
            break
        }
        ShareableEvents.shared().save(events)
    }
}
