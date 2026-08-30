import Foundation

/// A public location a session could be published to, offered by the server.
///
/// `publishedSessions` is why one of these is worth picking over starting a new place: somewhere
/// eleven people have already recorded is an archive, and adding to it is the entire point.
public struct ArchivePlaceCandidate: Codable, Sendable, Equatable, Identifiable {
    public let id: UUID
    public let name: String?
    public let city: String?
    public let state: String?
    /// Distance from where the session was recorded. "0.02 miles" is the same cave; "0.8" is not.
    public let miles: Double
    public let publishedSessions: Int

    public init(id: UUID, name: String?, city: String?, state: String?,
                miles: Double, publishedSessions: Int) {
        self.id = id
        self.name = name
        self.city = city
        self.state = state
        self.miles = miles
        self.publishedSessions = publishedSessions
    }

    /// "Adams, TN" — or whichever half exists.
    public var where_: String {
        [city, state].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: ", ")
    }

    /// "300 ft away" reads better than "0.06 miles away" at the distances that matter here.
    public var distanceText: String {
        miles < 0.2 ? "\(Int((miles * 5280).rounded())) ft away"
                    : String(format: "%.1f mi away", miles)
    }
}

/// A public location being named for the first time. Its kind is decided by the server, never here.
public struct NewArchivePlace: Codable, Sendable, Equatable {
    public var name: String
    public var streetAddress1: String?
    public var city: String?
    public var state: String?
    public var zipCode: String?
    public var latitude: Double?
    public var longitude: Double?

    public init(name: String, streetAddress1: String? = nil, city: String? = nil,
                state: String? = nil, zipCode: String? = nil,
                latitude: Double? = nil, longitude: Double? = nil) {
        self.name = name
        self.streetAddress1 = streetAddress1
        self.city = city
        self.state = state
        self.zipCode = zipCode
        self.latitude = latitude
        self.longitude = longitude
    }
}

/// Putting a session into a place's public archive, and taking it back out.
///
/// **Publishing is an act, not a setting.** A session is private to whoever recorded it until
/// somebody chooses otherwise, and choosing is reversible — a person who published the wrong night
/// must be able to undo it without asking anybody.
public struct ArchiveActions: Sendable {
    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// Public locations near where the session was recorded.
    ///
    /// Asked BEFORE offering to name a new place, because a picker is what actually prevents one
    /// cave becoming two pages — string matching catches the easy cases and missed a real one the
    /// first time three people published to one location.
    public func candidates(latitude: Double?, longitude: Double?) async -> [ArchivePlaceCandidate] {
        guard let latitude, let longitude else { return [] }
        let endpoint = Endpoint(.get, "api/places/archive-candidates", query: [
            URLQueryItem(name: "latitude", value: String(latitude)),
            URLQueryItem(name: "longitude", value: String(longitude)),
        ])
        return await api.load(endpoint, as: [ArchivePlaceCandidate].self).value ?? []
    }

    /// Publishes to a place that already exists — the picker's path, and the preferred one.
    public func publish(sessionId: UUID, toExisting placeId: UUID) async -> Result<Void, FeedActionError> {
        struct Body: Encodable { let placeId: UUID }
        return await send(sessionId: sessionId, body: Body(placeId: placeId))
    }

    /// Publishes to a place nobody has recorded yet. The server matches before it creates, so two
    /// people describing one landmark differently still land on one page.
    public func publish(sessionId: UUID, naming place: NewArchivePlace) async -> Result<Void, FeedActionError> {
        struct Body: Encodable { let newPlace: NewArchivePlace }
        return await send(sessionId: sessionId, body: Body(newPlace: place))
    }

    /// Takes it back out. Where it happened is kept — that is a fact about the recording, not
    /// about having shared it — so republishing later asks nothing again.
    public func retract(sessionId: UUID) async -> Result<Void, FeedActionError> {
        let path = "api/field-sessions/\(sessionId.uuidString.lowercased())/publish"
        return outcome(await api.send(Endpoint(.delete, path)))
    }

    private func send(sessionId: UUID, body: some Encodable) async -> Result<Void, FeedActionError> {
        let path = "api/field-sessions/\(sessionId.uuidString.lowercased())/publish"
        guard let endpoint = try? Endpoint.json(.post, path, payload: body)
        else { return .failure(.failed(reason: nil)) }
        return outcome(await api.send(endpoint))
    }

    private func outcome(_ result: LoadResult<EmptyBody>) -> Result<Void, FeedActionError> {
        switch result {
        case .ok: .success(())
        // The server's own sentence survives — "Only public locations have an open archive" is
        // something a person can act on, and "couldn't publish" is not.
        case .failed(let reason, _): .failure(.failed(reason: reason))
        case .sessionEnded: .failure(.sessionEnded)
        case .rateLimited(let after): .failure(.rateLimited(retryAfter: after))
        }
    }
}
