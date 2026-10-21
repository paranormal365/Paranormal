import Foundation

/// The event's results and a tour date's room (event-scenario-gaps, 10/21/2026).
extension HostedEventsStore {

    // ── the results ──────────────────────────────────────────────────────────

    /// Nil when the event isn't on the public site (404): nothing to show, not a failure.
    public func loadResults(_ eventId: UUID) async -> LoadResult<HostedEventResults?> {
        let result = await api.load(
            Endpoint(.get, "api/public/hosted-events/\(Self.lower(eventId))/results", requiresAuth: false),
            as: HostedEventResults.self)
        switch result {
        case .ok(let value): return .ok(value)
        case .failed(_, let status?) where status == 404: return .ok(nil)
        case .failed(let reason, let status): return .failed(reason: reason, statusCode: status)
        case .sessionEnded: return .sessionEnded
        case .rateLimited(let after): return .rateLimited(retryAfter: after)
        }
    }

    // ── a tour date's room ───────────────────────────────────────────────────

    /// Nil when this reader isn't in the room (the server answers 204) or the date isn't a tour date (404).
    public func loadTourRoom(_ dateId: UUID) async -> LoadResult<TourRoom?> {
        let result = await api.loadData(Endpoint(.get, Self.tourRoomPath(dateId)))
        switch result {
        case .ok(let data):
            if data.isEmpty { return .ok(nil) }
            do { return .ok(try BenJSON.decoder.decode(TourRoom.self, from: data)) }
            catch { return .failed(reason: nil) }
        case .failed(_, let status?) where status == 404: return .ok(nil)
        case .failed(let reason, let status): return .failed(reason: reason, statusCode: status)
        case .sessionEnded: return .sessionEnded
        case .rateLimited(let after): return .rateLimited(retryAfter: after)
        }
    }

    /// Posts words in a tour date's room.
    public func postTourRoom(_ dateId: UUID, body: String) async -> LoadResult<TourRoom> {
        guard let endpoint = try? Endpoint.json(.post, Self.tourRoomPath(dateId), payload: TourRoomPost(body: body))
        else { return .failed(reason: nil) }
        return await api.load(endpoint, as: TourRoom.self)
    }

    /// Takes a post down: its author, or the people running the walk.
    public func removeTourRoomMessage(_ dateId: UUID, message messageId: UUID) async -> LoadResult<TourRoom> {
        await api.load(Endpoint(.delete, "\(Self.tourRoomPath(dateId))/messages/\(Self.lower(messageId))"), as: TourRoom.self)
    }

    private struct TourRoomPost: Encodable { let body: String }

    static func tourRoomPath(_ dateId: UUID) -> String { "api/public/tour-dates/\(lower(dateId))/room" }
    private static func lower(_ id: UUID) -> String { id.uuidString.lowercased() }
}
