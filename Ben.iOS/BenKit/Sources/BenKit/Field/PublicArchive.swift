import Foundation

/// A published field session, as the public archive lists it.
///
/// Ben, 2026-09-27: "a server lookup to see if there are any available public .ben files from
/// nearby", and "look up .ben files based on looking up locations". The rows are the archive's own:
/// published, at a public location. The position is the place's PUBLIC point, deliberately
/// approximate — the public copy moves every reading there too.
public struct PublicArchiveSession: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var placeId: UUID
    public var placeName: String?
    public var placeCity: String?
    public var placeState: String?
    public var publicLatitude: Double?
    public var publicLongitude: Double?
    public var recordedBy: String
    public var locationLabel: String?
    public var startedAt: Date
    public var endedAt: Date?
    public var readingCount: Int
    public var markerCount: Int
    /// Recordings a visitor may have — zero while a session's media is held.
    public var mediaCount: Int
    /// Whether there is a public `.ben` to download. Older sessions were sent before session files.
    public var canDownload: Bool
    /// From where the lookup was asked. Nil for a search by name.
    public var distanceMiles: Double?

    public init(id: UUID, placeId: UUID, placeName: String?, placeCity: String?, placeState: String?,
                publicLatitude: Double?, publicLongitude: Double?, recordedBy: String,
                locationLabel: String?, startedAt: Date, endedAt: Date?, readingCount: Int,
                markerCount: Int, mediaCount: Int, canDownload: Bool, distanceMiles: Double?) {
        self.id = id
        self.placeId = placeId
        self.placeName = placeName
        self.placeCity = placeCity
        self.placeState = placeState
        self.publicLatitude = publicLatitude
        self.publicLongitude = publicLongitude
        self.recordedBy = recordedBy
        self.locationLabel = locationLabel
        self.startedAt = startedAt
        self.endedAt = endedAt
        self.readingCount = readingCount
        self.markerCount = markerCount
        self.mediaCount = mediaCount
        self.canDownload = canDownload
        self.distanceMiles = distanceMiles
    }

    /// "The Old Mill", or the town when the place has no name.
    public var placeTitle: String {
        if let placeName, !placeName.isEmpty { return placeName }
        let town = [placeCity, placeState].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: ", ")
        return town.isEmpty ? "A public place" : town
    }

    /// "Nashville, TN · 1.2 mi" — where, and how far, as far as either is known.
    public var whereLine: String {
        var parts: [String] = []
        let town = [placeCity, placeState].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: ", ")
        if !town.isEmpty, placeName?.isEmpty == false { parts.append(town) }
        if let distanceMiles {
            parts.append(distanceMiles < 0.1 ? "here" : String(format: "%.1f mi", distanceMiles))
        }
        return parts.joined(separator: " · ")
    }
}

/// Finding published sessions and fetching their public copies. Anonymous: nothing here needs an
/// account, because the archive is public.
public struct PublicArchiveClient: Sendable {
    private let api: APIClient

    public init(api: APIClient) { self.api = api }

    public func nearby(latitude: Double, longitude: Double,
                       radiusMiles: Double = 10) async -> LoadResult<[PublicArchiveSession]> {
        await api.load(Endpoint(.get, "api/public/field-sessions/nearby", query: [
            URLQueryItem(name: "latitude", value: String(latitude)),
            URLQueryItem(name: "longitude", value: String(longitude)),
            URLQueryItem(name: "radiusMiles", value: String(radiusMiles)),
        ]), as: [PublicArchiveSession].self)
    }

    public func search(_ query: String) async -> LoadResult<[PublicArchiveSession]> {
        await api.load(Endpoint(.get, "api/public/field-sessions/search", query: [
            URLQueryItem(name: "query", value: query),
        ]), as: [PublicArchiveSession].self)
    }

    /// The public copy of one session, written to `destination`.
    public func downloadBundle(sessionId: UUID, to destination: URL) async -> LoadResult<URL> {
        await api.download(Endpoint(
            .get, "api/public/field-sessions/\(sessionId.uuidString.lowercased())/bundle"),
            to: destination)
    }
}

/// What the new-session sheet offers first, worked out from where the phone is.
///
/// Ben, 2026-09-27: "If you use the map, you should be able to determine where they are and if they
/// did not allow you to read their position, then you should ask where they are. Also, if there is
/// an investigation at the location they should be able to just pick it from the dropdown list."
public enum StartSuggestions {
    /// Close enough to call "here": the width of a large building and its grounds.
    public static let hereMiles = 0.5
    /// Close enough for a known place's name to stand for where somebody is.
    public static let placeNameMiles = 0.25

    /// Investigations in the order the sheet lists them: here and today, then here, then today,
    /// then the rest soonest first.
    public static func order(_ investigations: [MyInvestigation], latitude: Double?, longitude: Double?,
                             now: Date = Date()) -> [MyInvestigation] {
        func rank(_ item: MyInvestigation) -> Int {
            let here = isHere(item, latitude: latitude, longitude: longitude)
            let today = item.isHappeningToday(now: now)
            switch (here, today) {
            case (true, true): return 0
            case (true, false): return 1
            case (false, true): return 2
            case (false, false): return 3
            }
        }
        return investigations.sorted {
            let (a, b) = (rank($0), rank($1))
            if a != b { return a < b }
            return ($0.scheduledDateTime ?? .distantFuture) < ($1.scheduledDateTime ?? .distantFuture)
        }
    }

    /// The one to choose for them: happening today AND here. Never a guess from time alone — two
    /// investigations tonight in different towns is a normal week, and a wrong link is worse than
    /// none.
    public static func preselect(_ investigations: [MyInvestigation], latitude: Double?, longitude: Double?,
                                 now: Date = Date()) -> MyInvestigation? {
        let matches = investigations.filter {
            $0.isHappeningToday(now: now) && isHere($0, latitude: latitude, longitude: longitude)
        }
        return matches.count == 1 ? matches[0] : nil
    }

    public static func isHere(_ investigation: MyInvestigation, latitude: Double?, longitude: Double?) -> Bool {
        guard let latitude, let longitude,
              let theirs = investigation.latitude, let theirLongitude = investigation.longitude else { return false }
        return miles(latitude, longitude, theirs, theirLongitude) <= hereMiles
    }

    /// The name to put in "Where are you?": a known place close by, else the address the phone
    /// worked out, else nothing (and the person types it).
    public static func placeName(candidates: [ArchivePlaceCandidate], address: String?) -> String? {
        if let place = candidates.filter({ $0.miles <= placeNameMiles }).min(by: { $0.miles < $1.miles }),
           let name = place.name, !name.isEmpty {
            return name
        }
        guard let address, !address.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
        return address
    }

    public static func miles(_ lat1: Double, _ lon1: Double, _ lat2: Double, _ lon2: Double) -> Double {
        let radius = 3958.8
        let dLat = (lat2 - lat1) * .pi / 180
        let dLon = (lon2 - lon1) * .pi / 180
        let a = sin(dLat / 2) * sin(dLat / 2)
            + cos(lat1 * .pi / 180) * cos(lat2 * .pi / 180) * sin(dLon / 2) * sin(dLon / 2)
        return radius * 2 * atan2(a.squareRoot(), (1 - a).squareRoot())
    }
}
