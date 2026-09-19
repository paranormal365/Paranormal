import Foundation

/// Walking tours near somewhere (item 234, Ben 2026-09-10).
///
/// Ben: *"I would also like to add the tours in a Haunted Tours tab in the iPhone and iPad app
/// based on current location or looking up a location on the tab."* Both come through one
/// endpoint — `GET api/public/tours` — which does the great-circle filtering and the sorting on
/// the server, so the phone never has to hold every tour in the country to find the near ones.
///
/// **Anonymous throughout.** A tour is a thing somebody looks at before they have an account, and
/// gating the list would put a sign-in wall in front of the app's most public surface.
@MainActor
public final class ToursStore {
    public enum State: Equatable {
        case idle
        case loading
        case loaded
        case failed(reason: String?)
    }

    public private(set) var state: State = .idle
    public private(set) var tours: [PublicTourListItem] = []

    /// Where the last search measured from, so a screen can say what it searched.
    public private(set) var searchedNear: String?

    private let api: APIClient

    public init(api: APIClient) { self.api = api }

    /// The widest the server will accept. Asked for by name rather than as a bare 100.
    public static let maxRadiusMiles = 100

    /// Tours near a point, or matching a name, or both.
    ///
    /// - Parameters:
    ///   - latitude/longitude: where to measure from. Nil searches everywhere by name.
    ///   - radiusMiles: clamped by the server to 0.1…100.
    ///   - query: matches the tour's name, the business's, or the city.
    ///   - near: what to tell the person this searched — "your location", "Nashville, TN".
    public func load(
        latitude: Double? = nil, longitude: Double? = nil,
        radiusMiles: Int = 25, query: String? = nil, near: String? = nil
    ) async {
        state = .loading
        searchedNear = near

        // Through Endpoint's own query list, not glued onto the path: a path carrying "?" is
        // escaped whole and reaches the server as one long segment, which answers 404 — which is
        // exactly what the first build of this screen showed.
        var items = [URLQueryItem(name: "radiusMiles", value: String(radiusMiles))]
        if let latitude, let longitude {
            items.append(URLQueryItem(name: "lat", value: String(latitude)))
            items.append(URLQueryItem(name: "lon", value: String(longitude)))
        }
        let typed = query?.trimmingCharacters(in: .whitespaces) ?? ""
        if !typed.isEmpty { items.append(URLQueryItem(name: "query", value: typed)) }

        let endpoint = Endpoint(.get, "api/public/tours", query: items, requiresAuth: false)

        switch await api.load(endpoint, as: [PublicTourListItem].self) {
        case .ok(let items):
            tours = items
            state = .loaded
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .sessionEnded:
            // Anonymous already; there is no session to have ended, but the case must be handled.
            state = .failed(reason: nil)
        case .rateLimited:
            state = .failed(reason: "Too many searches — try again shortly.")
        }
    }

    /// One tour, with its dates, its gallery and what people made of it.
    public func loadOne(organizationUrlName: String, tourSlug: String) async -> PublicTourRecord? {
        let endpoint = Endpoint(
            .get, "api/public/organizations/\(organizationUrlName)/tours/\(tourSlug)",
            requiresAuth: false)
        if case .ok(let tour) = await api.load(endpoint, as: PublicTourRecord.self) { return tour }
        return nil
    }
}
