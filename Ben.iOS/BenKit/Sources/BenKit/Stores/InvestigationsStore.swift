import Foundation
import Observation

/// Investigations this member is on (iOS Slice 7), split the way a roster reads: what is
/// coming, then what has been.
@Observable
@MainActor
public final class InvestigationsStore {
    public enum State: Equatable {
        case loading
        case loaded
        /// Investigations belong to a group's members — there is nothing here without one.
        case signedOut
        case failed(reason: String?)
    }

    public private(set) var state: State = .loading
    public private(set) var investigations: [MyInvestigation] = []
    public private(set) var attended: [AttendedInvestigation] = []

    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    public var upcoming: [MyInvestigation] {
        investigations.filter { $0.isUpcoming() }
            .sorted { ($0.scheduledDateTime ?? .distantFuture) < ($1.scheduledDateTime ?? .distantFuture) }
    }

    public var past: [MyInvestigation] {
        investigations.filter { !$0.isUpcoming() }
            .sorted { ($0.scheduledDateTime ?? .distantPast) > ($1.scheduledDateTime ?? .distantPast) }
    }

    /// Only the visits that can actually be drawn. A place with no coordinates is a real
    /// visit with no pin — dropping it from the MAP is right; dropping it from the LIST
    /// would be losing it.
    public var mappable: [AttendedInvestigation] {
        attended.filter(\.hasCoordinates)
    }

    public func load() async {
        if case .loaded = state {} else { state = .loading }

        let roster = await api.load(Endpoint(.get, "api/my-investigations"), as: [MyInvestigation].self)
        switch roster {
        case .ok(let items):
            investigations = items
            state = .loaded
        case .sessionEnded:
            investigations = []; attended = []
            state = .signedOut
            return
        case .failed(_, let statusCode) where statusCode == 401:
            investigations = []; attended = []
            state = .signedOut
            return
        case .failed(let reason, _):
            state = .failed(reason: reason)
            return
        case .rateLimited:
            state = .failed(reason: "Too many requests — try again shortly.")
            return
        }

        // The attended list feeds the map. A failure here narrows the map, and must NOT
        // turn a working roster into an error page.
        if case .ok(let visits) = await api.load(
            Endpoint(.get, "api/my-investigations/attended"), as: [AttendedInvestigation].self) {
            attended = visits
        }
    }

    public func clear() {
        investigations = []
        attended = []
        state = .loading
    }
}

/// Public events, and RSVPing to them.
@Observable
@MainActor
public final class EventsStore {
    public private(set) var state: InvestigationsStore.State = .loading
    public private(set) var events: [PublicEventListItem] = []
    /// Event ids this person has said they are going to, for the ones we know about.
    public private(set) var attending: Set<UUID> = []

    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    public func load(signedIn: Bool) async {
        if case .loaded = state {} else { state = .loading }

        // Anonymous by design: the public events list is one of the front doors.
        switch await api.load(
            Endpoint(.get, "api/public/events", requiresAuth: false), as: [PublicEventListItem].self) {
        case .ok(let items):
            events = items
            state = .loaded
        case .failed(let reason, _):
            state = .failed(reason: reason)
            return
        case .sessionEnded:
            state = .failed(reason: nil)
            return
        case .rateLimited:
            state = .failed(reason: "Too many requests — try again shortly.")
            return
        }

        guard signedIn else {
            attending = []
            return
        }
        if case .ok(let mine) = await api.load(
            Endpoint(.get, "api/public/events/mine"), as: [PublicEventListItem].self) {
            attending = Set(mine.map(\.id))
        }
    }

    /// Reserves a place. The server refuses with a SENTENCE — closed, already started, full —
    /// and those are exactly what a person needs to read, so they are carried back verbatim.
    public func rsvp(_ eventId: UUID) async -> Result<Void, FeedActionError> {
        let endpoint = Endpoint(.post, "api/public/events/\(eventId.uuidString.lowercased())/rsvp")
        switch await api.load(endpoint, as: PublicEventRecord.self) {
        case .ok(let updated):
            applyAttendingCount(eventId, updated.attendingCount)
            attending.insert(eventId)
            return .success(())
        case .failed(let reason, _):
            return .failure(.failed(reason: reason))
        case .sessionEnded:
            return .failure(.sessionEnded)
        case .rateLimited(let retryAfter):
            return .failure(.rateLimited(retryAfter: retryAfter))
        }
    }

    public func cancelRsvp(_ eventId: UUID) async -> Bool {
        let endpoint = Endpoint(.delete, "api/public/events/\(eventId.uuidString.lowercased())/rsvp")
        guard await api.send(endpoint).isOk else { return false }
        attending.remove(eventId)
        // The count moved on the server; reflect it without a round trip.
        if let index = events.firstIndex(where: { $0.id == eventId }) {
            events[index].attendingCount = max(0, events[index].attendingCount - 1)
        }
        return true
    }

    /// The RSVP answer is the DETAIL record (with the server's own flags); the list carries
    /// the lighter item, so only the count is folded back rather than swapping shapes.
    private func applyAttendingCount(_ eventId: UUID, _ count: Int) {
        if let index = events.firstIndex(where: { $0.id == eventId }) {
            events[index].attendingCount = count
        }
    }
}
