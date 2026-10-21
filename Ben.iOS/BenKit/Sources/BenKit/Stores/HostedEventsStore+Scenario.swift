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

    // ── who came to the classes ──────────────────────────────────────────────

    /// The classes, with their rosters, for somebody on the door or in the group. Refused for anybody else.
    public func loadClasses(organizationId: UUID, hostedEventId: UUID) async -> LoadResult<HostedEventClasses> {
        await api.load(Endpoint(.get, "\(Self.sessionsPath(organizationId, hostedEventId))/attendance"), as: HostedEventClasses.self)
    }

    /// Marks somebody there at a class, or takes the mark off. Answers the class's roster.
    public func setAttended(organizationId: UUID, hostedEventId: UUID, sessionId: UUID, signUpId: UUID,
                            here: Bool) async -> LoadResult<ClassRoster> {
        guard let endpoint = try? Endpoint.json(
            .put, "\(Self.sessionsPath(organizationId, hostedEventId))/\(Self.lower(sessionId))/sign-ups/\(Self.lower(signUpId))/attended",
            payload: AttendedPost(attended: here))
        else { return .failed(reason: nil) }
        return await api.load(endpoint, as: ClassRoster.self)
    }

    private struct AttendedPost: Encodable { let attended: Bool }

    static func sessionsPath(_ organizationId: UUID, _ hostedEventId: UUID) -> String {
        "api/organizations/\(lower(organizationId))/events/\(lower(hostedEventId))/sessions"
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

    // ── writing to the organizers (event-scenario-gaps, 10/21/2026) ──────────

    /// The reader's conversation with an event's organizers. `.ok(nil)` for somebody with no booking there
    /// (the server answers 204), which is not a failure: the hub simply doesn't offer it.
    public func loadOrganizerThread(_ hostedEventId: UUID) async -> LoadResult<EventGuestThread?> {
        switch await api.loadData(Endpoint(.get, Self.organizerThreadPath(hostedEventId))) {
        case .ok(let data):
            if data.isEmpty { return .ok(nil) }
            do { return .ok(try BenJSON.decoder.decode(EventGuestThread.self, from: data)) }
            catch { return .failed(reason: nil) }
        case .failed(_, let status?) where status == 404: return .ok(nil)
        case .failed(let reason, let status): return .failed(reason: reason, statusCode: status)
        case .sessionEnded: return .sessionEnded
        case .rateLimited(let after): return .rateLimited(retryAfter: after)
        }
    }

    /// Writes to the organizers; keeps the server's sentence when it refuses.
    public func writeToOrganizers(_ hostedEventId: UUID, body: String) async -> LoadResult<EventGuestThread> {
        guard let endpoint = try? Endpoint.json(.post, Self.organizerThreadPath(hostedEventId), payload: OrganizerThreadPost(body: body))
        else { return .failed(reason: nil) }
        return await api.load(endpoint, as: EventGuestThread.self)
    }

    private struct OrganizerThreadPost: Encodable { let body: String }

    static func organizerThreadPath(_ hostedEventId: UUID) -> String {
        "api/public/hosted-events/\(lower(hostedEventId))/organizer-thread"
    }

    static func tourRoomPath(_ dateId: UUID) -> String { "api/public/tour-dates/\(lower(dateId))/room" }
    private static func lower(_ id: UUID) -> String { id.uuidString.lowercased() }
}
