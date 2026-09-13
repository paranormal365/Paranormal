import Foundation

/// A guest's hosted-event bookings and passes (item 235 phase 14).
///
/// **Picking and asking stay on the website.** The seat picker, the soft hold and the contact questions
/// are one web page that has been lived with; the phone shows where a booking stands, carries the pass,
/// and sends somebody to that page to book. Rebuilding the picker natively before the web one has settled
/// would be a second copy of the hardest screen in the feature.
///
/// **The pass works with no signal.** The last pass read is kept on the phone (`PassCache`), so a guest
/// standing in a basement with one bar still has a code to show — and a withdrawn pass is kept as withdrawn,
/// so an old saved copy never looks valid.
@MainActor
public final class HostedEventsStore {
    let api: APIClient
    private let cache: PassCache

    public init(api: APIClient, cache: PassCache = .applicationSupport()) {
        self.api = api
        self.cache = cache
    }

    /// Everything this person has booked, one row per event.
    public func loadMine() async -> LoadResult<[MyHostedEventBooking]> {
        await api.load(Endpoint(.get, "api/public/hosted-events/mine"), as: [MyHostedEventBooking].self)
    }

    /// The hosted event itself, as a visitor reads it. Anonymous.
    public func loadEvent(_ eventId: UUID) async -> LoadResult<PublicHostedEvent> {
        await api.load(Endpoint(.get, "api/public/hosted-events/\(Self.id(eventId))", requiresAuth: false),
                       as: PublicHostedEvent.self)
    }

    /// This person's booking at one event. A 404 is the ordinary answer "none", not a failure.
    public func loadMyBooking(_ eventId: UUID) async -> LoadResult<MyHostedEventBooking?> {
        switch await api.load(Endpoint(.get, "api/public/hosted-events/\(Self.id(eventId))/my-booking"),
                              as: MyHostedEventBooking.self) {
        case .ok(let booking): return .ok(booking)
        case .failed(_, 404): return .ok(nil)
        case .failed(let reason, let status): return .failed(reason: reason, statusCode: status)
        case .sessionEnded: return .sessionEnded
        case .rateLimited(let after): return .rateLimited(retryAfter: after)
        }
    }

    /// What a pass read gave, and whether it came from the phone rather than the server.
    public enum PassAnswer: Sendable, Equatable {
        /// Fresh from the server, and now saved.
        case live(MyHostedEventPass)
        /// The server could not be reached; this is the copy saved at `savedAt`.
        case saved(MyHostedEventPass, savedAt: Date)
        /// The server answered that there is no pass, in its own words ("The venue hasn't issued your pass yet").
        case none(reason: String?)
        case failed(reason: String?)
    }

    /// The pass: from the server when it answers, from the phone when it cannot be reached.
    ///
    /// A server that ANSWERS "no pass" clears the saved copy — the booking was released, and a code kept
    /// from before would be a lie at the door. Only an unreachable server falls back to the saved one.
    public func loadPass(_ eventId: UUID) async -> PassAnswer {
        switch await api.load(Endpoint(.get, "api/public/hosted-events/\(Self.id(eventId))/my-booking/pass"),
                              as: MyHostedEventPass.self) {
        case .ok(let pass):
            cache.save(pass, for: eventId)
            return .live(pass)
        case .failed(let reason, let status?) where status == 403 || status == 404:
            cache.remove(eventId)
            return .none(reason: reason)
        case .failed(let reason, _):
            if let saved = cache.load(eventId) { return .saved(saved.pass, savedAt: saved.savedAt) }
            return .failed(reason: reason)
        case .sessionEnded:
            if let saved = cache.load(eventId) { return .saved(saved.pass, savedAt: saved.savedAt) }
            return .failed(reason: "Sign in again to fetch your pass.")
        case .rateLimited:
            if let saved = cache.load(eventId) { return .saved(saved.pass, savedAt: saved.savedAt) }
            return .failed(reason: "Too many requests — try again shortly.")
        }
    }

    /// The saved pass alone, for a screen that opens before any network answers.
    public func savedPass(_ eventId: UUID) -> (pass: MyHostedEventPass, savedAt: Date)? {
        cache.load(eventId).map { ($0.pass, $0.savedAt) }
    }

    /// Says back that the venue's answer has been seen. Clears the bell.
    public func acknowledge(_ eventId: UUID) async -> LoadResult<MyHostedEventBooking> {
        await api.load(Endpoint(.post, "api/public/hosted-events/\(Self.id(eventId))/my-booking/acknowledge"),
                       as: MyHostedEventBooking.self)
    }

    /// Lets a request or a hold go; asks the venue to release a confirmed booking.
    public func withdraw(_ eventId: UUID, reason: String?) async -> LoadResult<EmptyBody> {
        var query: [URLQueryItem] = []
        if let reason, !reason.isEmpty { query.append(URLQueryItem(name: "reason", value: reason)) }
        return await api.send(Endpoint(.delete, "api/public/hosted-events/\(Self.id(eventId))/my-booking", query: query))
    }

    /// Forgets every saved pass — on sign-out, so the next person on this phone never sees one.
    public func forgetSavedPasses() { cache.removeAll() }

    private static func id(_ id: UUID) -> String { id.uuidString.lowercased() }
}

/// Passes kept on the phone for when there is no signal (item 235 phase 14).
///
/// In Application Support with complete-until-first-unlock protection: readable once the phone has been
/// unlocked since it started, which is every moment somebody is standing at a door holding it, and not
/// from a phone that has just been switched on by someone else.
public struct PassCache: Sendable {
    public struct Saved: Codable, Sendable {
        public var pass: MyHostedEventPass
        public var savedAt: Date
    }

    private let directory: URL

    public init(directory: URL) { self.directory = directory }

    public static func applicationSupport() -> PassCache {
        let base = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask,
                                                 appropriateFor: nil, create: true))
            ?? FileManager.default.temporaryDirectory
        return PassCache(directory: base.appendingPathComponent("EventPasses", isDirectory: true))
    }

    public func save(_ pass: MyHostedEventPass, for eventId: UUID, at now: Date = Date()) {
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let data = try Self.encoder.encode(Saved(pass: pass, savedAt: now))
            #if os(iOS)
            try data.write(to: file(eventId), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
            #else
            try data.write(to: file(eventId), options: .atomic)
            #endif
        } catch {
            // A pass that could not be saved is still shown; it just will not be there offline.
        }
    }

    public func load(_ eventId: UUID) -> Saved? {
        guard let data = try? Data(contentsOf: file(eventId)) else { return nil }
        return try? BenJSON.decoder.decode(Saved.self, from: data)
    }

    public func remove(_ eventId: UUID) {
        try? FileManager.default.removeItem(at: file(eventId))
    }

    public func removeAll() {
        try? FileManager.default.removeItem(at: directory)
    }

    /// Keeps fractional seconds, so a pass read back from the phone is exactly the pass that was saved.
    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(date.formatted(Date.ISO8601FormatStyle(includingFractionalSeconds: true)))
        }
        return encoder
    }()

    private func file(_ eventId: UUID) -> URL {
        directory.appendingPathComponent("\(eventId.uuidString.lowercased()).json")
    }
}
