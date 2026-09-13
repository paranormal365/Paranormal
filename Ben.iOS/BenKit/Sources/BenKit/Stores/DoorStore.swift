import Foundation

/// The door on the phone (item 235 phase 14c): the events a person may let people into, one night's list, and
/// checking a party in — by name, by the pass code they read out, or by scanning their pass, which shows who they
/// are first and checks them in when the door says so.
///
/// **It works in a cellar.** The last list read for each night is kept on the phone, and so is the list of doors.
/// With no signal a scanned pass is matched against the kept list by its last six characters — the same pass
/// code the door would type — and the arrival is kept, with the time it happened, and sent when there is signal.
/// The server checks every kept arrival again when it arrives, so a pass withdrawn in the meantime is still
/// refused; that refusal is reported to the door rather than lost.
///
/// **What needs a signal says so.** A walk-up has to be counted against the places left, and taking somebody
/// back out who has already been recorded has to reach the server; neither is kept for later.
@MainActor
public final class DoorStore {
    private let api: APIClient
    private let cache: DoorCache
    private let now: @Sendable () -> Date

    /// One send at a time per place arrivals are kept, across every door screen, so a kept arrival is never sent twice.
    private static var sending: Set<String> = []

    public init(api: APIClient, cache: DoorCache = .applicationSupport(), now: @escaping @Sendable () -> Date = { Date() }) {
        self.api = api
        self.cache = cache
        self.now = now
    }

    // ── answers ──────────────────────────────────────────────────────────────

    public enum Answer<Value: Equatable & Sendable>: Equatable, Sendable {
        case live(Value)
        /// The copy kept on the phone, because the server could not be reached.
        case saved(Value, savedAt: Date)
        case failed(String?)
    }

    public enum MoveOutcome: Equatable, Sendable {
        case done(HostedEventDoor)
        /// No signal: recorded on the phone and waiting to be sent.
        case kept(HostedEventDoor)
        case refused(String)
    }

    /// What a scanned pass says about who is in front of the door — nobody is let in by looking.
    public enum LookUpOutcome: Equatable, Sendable {
        /// The server recognised the pass for this event. `party` is their row on tonight's list, when they are on it.
        case found(HostedEventScanResult, HostedEventDoorParty?)
        /// No signal, and the code is on the list kept on this phone.
        case foundOnThePhone(HostedEventDoorParty)
        /// The server's reason not to let them in, to say aloud.
        case refused(String)
        /// No signal, and no party on the kept list has this code.
        case notOnTheSavedList
        case failed(String)
    }

    public enum CheckInOutcome: Equatable, Sendable {
        /// Recorded, and tonight's list read again.
        case checkedIn(HostedEventDoor)
        /// No signal: checked in on the phone and waiting to be sent.
        case kept(HostedEventDoor)
        case refused(String)
    }

    public struct SendReport: Equatable, Sendable {
        public var sent: Int
        /// "Daniel Park: This pass was withdrawn by the venue." — each kept arrival the server would not take.
        public var refused: [String]
        public var stillWaiting: Int
    }

    // ── the doors ────────────────────────────────────────────────────────────

    public func loadDuties() async -> Answer<[MyHostedEventDuty]> {
        switch await api.load(Endpoint(.get, "api/me/hosted-event-duties"), as: [MyHostedEventDuty].self) {
        case .ok(let duties):
            cache.saveDuties(duties, at: now())
            return .live(duties)
        case .failed(_, let status) where status == nil || status! >= 500:
            if let saved = cache.loadDuties() { return .saved(saved.value, savedAt: saved.savedAt) }
            return .failed(nil)
        case .failed(let reason, _):
            return .failed(reason)
        case .sessionEnded:
            return .failed("Sign in again to see your doors.")
        case .rateLimited:
            return .failed("Too many requests — try again shortly.")
        }
    }

    // ── one night ────────────────────────────────────────────────────────────

    public func loadDoor(organization orgId: UUID, event eventId: UUID, night nightId: UUID? = nil) async -> Answer<HostedEventDoor> {
        var query: [URLQueryItem] = []
        if let nightId { query.append(URLQueryItem(name: "night", value: Self.id(nightId))) }
        switch await api.load(Endpoint(.get, Self.base(orgId, eventId), query: query), as: HostedEventDoor.self) {
        case .ok(let door):
            let shown = overlayQueued(on: door)
            cache.saveDoor(shown, at: now())
            return .live(shown)
        case .failed(_, 403), .failed(_, 404):
            // Not theirs any more: nothing of it stays on the phone.
            cache.removeDoors(for: eventId)
            return .failed("This door isn't yours to run any more. Ask the organizers if that's a surprise.")
        case .failed(_, let status) where status == nil || status! >= 500:
            if let saved = cache.loadDoor(eventId, night: nightId) {
                return .saved(overlayQueued(on: saved.value), savedAt: saved.savedAt)
            }
            return .failed("There's no signal, and this door hasn't been opened on this phone before.")
        case .failed(let reason, _):
            return .failed(reason)
        case .sessionEnded:
            return .failed("Sign in again to run the door.")
        case .rateLimited:
            return .failed("Too many requests — try again shortly.")
        }
    }

    /// Lets a party in by name. `people` is how many actually came, when it isn't all of them.
    public func arrive(_ door: HostedEventDoor, organization orgId: UUID, party: HostedEventDoorParty, people: Int? = nil) async -> MoveOutcome {
        let at = now()
        let request = MoveRequest(hostedEventBookingId: party.hostedEventBookingId, hostedEventNightId: door.nightId, people: people, arrivedUtc: nil)
        switch await send(.post, "\(Self.base(orgId, door.hostedEventId))/arrive", request) {
        case .ok(let updated):
            cache.saveDoor(overlayQueued(on: updated), at: at)
            return .done(overlayQueued(on: updated))
        case .failed(_, let status) where status == nil || status! >= 500:
            enqueue(QueuedDoorMove(id: UUID(), organizationId: orgId, hostedEventId: door.hostedEventId, nightId: door.nightId,
                                   kind: .arrive, hostedEventBookingId: party.hostedEventBookingId, token: nil, people: people,
                                   leadName: party.leadName, arrivedUtc: at))
            let kept = Self.marking(door, party.hostedEventBookingId, in: true, people: people, at: at)
            cache.saveDoor(kept, at: at)
            return .kept(kept)
        case .failed(let reason, _):
            return .refused(reason ?? "\(party.leadName) couldn't be let in just now.")
        case .sessionEnded:
            return .refused("Sign in again to run the door.")
        case .rateLimited:
            return .refused("Too many at once — wait a moment.")
        }
    }

    /// Takes an arrival back. One still waiting on the phone is simply dropped; one already recorded needs a signal.
    public func undo(_ door: HostedEventDoor, organization orgId: UUID, party: HostedEventDoorParty) async -> MoveOutcome {
        var queue = cache.loadOutbox()
        if let index = queue.firstIndex(where: { $0.hostedEventBookingId == party.hostedEventBookingId && $0.nightId == door.nightId }) {
            queue.remove(at: index)
            cache.saveOutbox(queue)
            let back = Self.marking(door, party.hostedEventBookingId, in: false, people: nil, at: now())
            cache.saveDoor(back, at: now())
            return .done(back)
        }

        let request = MoveRequest(hostedEventBookingId: party.hostedEventBookingId, hostedEventNightId: door.nightId, people: nil, arrivedUtc: nil)
        switch await send(.post, "\(Self.base(orgId, door.hostedEventId))/undo", request) {
        case .ok(let updated):
            cache.saveDoor(overlayQueued(on: updated), at: now())
            return .done(overlayQueued(on: updated))
        case .failed(_, let status) where status == nil || status! >= 500:
            return .refused("Taking \(party.leadName) back out needs a signal, because their arrival has already been recorded. Try again in a moment.")
        case .failed(let reason, _):
            return .refused(reason ?? "That couldn't be taken back just now.")
        case .sessionEnded:
            return .refused("Sign in again to run the door.")
        case .rateLimited:
            return .refused("Too many at once — wait a moment.")
        }
    }

    /// Looks a scanned pass up, so the door can see who this is before letting them in (Ben, 2026-09-13: the scanner
    /// "would look up and verify their information and allow the door person to check them in"). Nothing is recorded.
    public func lookUp(_ door: HostedEventDoor, organization orgId: UUID, token: String) async -> LookUpOutcome {
        guard let endpoint = scanEndpoint(orgId, door, ScanRequest(token: token, checkIn: false, hostedEventNightId: door.nightId, arrivedUtc: nil))
        else { return .failed("That code couldn't be read.") }

        switch await api.load(endpoint, as: HostedEventScanResult.self) {
        case .ok(let result) where result.admitted:
            return .found(result, door.expected.first { $0.hostedEventBookingId == result.hostedEventBookingId })
        case .ok(let result):
            return .refused(result.refusal ?? "That pass isn't for tonight's door.")
        case .failed(_, let status) where status == nil || status! >= 500:
            guard let party = door.party(forScanned: token) else { return .notOnTheSavedList }
            return .foundOnThePhone(party)
        case .failed(let reason, _):
            return .failed(reason ?? "That code couldn't be checked just now.")
        case .sessionEnded:
            return .failed("Sign in again to run the door.")
        case .rateLimited:
            return .failed("Too many at once — wait a moment.")
        }
    }

    /// Checks in the party a scanned pass belongs to, once the door has seen who they are. `people` is how many
    /// actually came, when it isn't all of them. With no signal it is kept on the phone, pass and all, and the
    /// server checks the pass again when it is sent.
    public func checkIn(_ door: HostedEventDoor, organization orgId: UUID, token: String,
                        party: HostedEventDoorParty?, people: Int? = nil) async -> CheckInOutcome {
        let at = now()
        guard let endpoint = scanEndpoint(orgId, door, ScanRequest(token: token, checkIn: true, hostedEventNightId: door.nightId, arrivedUtc: nil))
        else { return .refused("That code couldn't be read.") }

        switch await api.load(endpoint, as: HostedEventScanResult.self) {
        case .ok(let result) where result.admitted:
            if let people, let bookingId = result.hostedEventBookingId, people < (result.partySize ?? people + 1) {
                _ = await send(.post, "\(Self.base(orgId, door.hostedEventId))/arrive",
                               MoveRequest(hostedEventBookingId: bookingId, hostedEventNightId: door.nightId, people: people, arrivedUtc: nil))
            }
            if case .live(let reread) = await loadDoor(organization: orgId, event: door.hostedEventId, night: door.nightId) {
                return .checkedIn(reread)
            }
            let marked = result.hostedEventBookingId.map { Self.marking(door, $0, in: true, people: people, at: at) } ?? door
            return .checkedIn(marked)
        case .ok(let result):
            return .refused(result.refusal ?? "That pass isn't for tonight's door.")
        case .failed(_, let status) where status == nil || status! >= 500:
            guard let party = party ?? door.party(forScanned: token) else {
                return .refused("That pass isn't on the list kept on this phone, so it can't be checked in without a signal.")
            }
            enqueue(QueuedDoorMove(id: UUID(), organizationId: orgId, hostedEventId: door.hostedEventId, nightId: door.nightId,
                                   kind: .scan, hostedEventBookingId: party.hostedEventBookingId, token: token, people: people,
                                   leadName: party.leadName, arrivedUtc: at))
            let kept = Self.marking(door, party.hostedEventBookingId, in: true, people: people, at: at)
            cache.saveDoor(kept, at: at)
            return .kept(kept)
        case .failed(let reason, _):
            return .refused(reason ?? "They couldn't be checked in just now.")
        case .sessionEnded:
            return .refused("Sign in again to run the door.")
        case .rateLimited:
            return .refused("Too many at once — wait a moment.")
        }
    }

    /// Somebody who turned up without a booking. Needs a signal: they are counted against the places left.
    public func walkUp(_ door: HostedEventDoor, organization orgId: UUID, people: Int, name: String?) async -> MoveOutcome {
        let request = WalkUpRequest(hostedEventNightId: door.nightId, people: people,
                                    name: name?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty, note: nil)
        switch await send(.post, "\(Self.base(orgId, door.hostedEventId))/walk-up", request) {
        case .ok(let updated):
            cache.saveDoor(overlayQueued(on: updated), at: now())
            return .done(overlayQueued(on: updated))
        case .failed(_, let status) where status == nil || status! >= 500:
            return .refused("Writing down somebody without a booking needs a signal, because the places left have to be checked first.")
        case .failed(let reason, _):
            return .refused(reason ?? "That couldn't be written down just now.")
        case .sessionEnded:
            return .refused("Sign in again to run the door.")
        case .rateLimited:
            return .refused("Too many at once — wait a moment.")
        }
    }

    public func undoWalkUp(_ door: HostedEventDoor, organization orgId: UUID, walkUp: HostedEventWalkUp) async -> MoveOutcome {
        switch await api.load(Endpoint(.delete, "\(Self.base(orgId, door.hostedEventId))/walk-up/\(Self.id(walkUp.id))"), as: HostedEventDoor.self) {
        case .ok(let updated):
            cache.saveDoor(overlayQueued(on: updated), at: now())
            return .done(overlayQueued(on: updated))
        case .failed(_, let status) where status == nil || status! >= 500:
            return .refused("Taking that back needs a signal. Try again in a moment.")
        case .failed(let reason, _):
            return .refused(reason ?? "That couldn't be taken back just now.")
        case .sessionEnded:
            return .refused("Sign in again to run the door.")
        case .rateLimited:
            return .refused("Too many at once — wait a moment.")
        }
    }

    // ── what waits on the phone ──────────────────────────────────────────────

    public func waiting(for eventId: UUID) -> [QueuedDoorMove] {
        cache.loadOutbox().filter { $0.hostedEventId == eventId }
    }

    /// Sends every kept arrival, oldest first, with the time it happened. Stops at the first that cannot reach the
    /// server, so the order is kept; one the server refuses is dropped and reported.
    public func sendWaiting() async -> SendReport {
        guard !Self.sending.contains(cache.key) else { return SendReport(sent: 0, refused: [], stillWaiting: cache.loadOutbox().count) }
        Self.sending.insert(cache.key)
        defer { Self.sending.remove(cache.key) }

        var queue = cache.loadOutbox()
        var sent = 0
        var refused: [String] = []

        while let move = queue.first {
            let outcome: (keep: Bool, refusal: String?)
            switch move.kind {
            case .arrive:
                let request = MoveRequest(hostedEventBookingId: move.hostedEventBookingId, hostedEventNightId: move.nightId,
                                          people: move.people, arrivedUtc: move.arrivedUtc)
                switch await send(.post, "\(Self.base(move.organizationId, move.hostedEventId))/arrive", request) {
                case .ok: outcome = (false, nil)
                case .failed(let reason, let status?) where status < 500: outcome = (false, reason ?? "couldn't be recorded")
                default: outcome = (true, nil)
                }
            case .scan:
                let request = ScanRequest(token: move.token ?? "", checkIn: true, hostedEventNightId: move.nightId, arrivedUtc: move.arrivedUtc)
                let path = "api/organizations/\(Self.id(move.organizationId))/events/\(Self.id(move.hostedEventId))/bookings/door/scan"
                guard let endpoint = try? Endpoint.json(.post, path, payload: request) else { outcome = (false, "couldn't be read"); break }
                switch await api.load(endpoint, as: HostedEventScanResult.self) {
                case .ok(let result) where result.admitted:
                    // Only some of the party came in: the head count goes with it.
                    if let people = move.people {
                        _ = await send(.post, "\(Self.base(move.organizationId, move.hostedEventId))/arrive",
                                       MoveRequest(hostedEventBookingId: move.hostedEventBookingId, hostedEventNightId: move.nightId,
                                                   people: people, arrivedUtc: move.arrivedUtc))
                    }
                    outcome = (false, nil)
                case .ok(let result): outcome = (false, result.refusal ?? "the pass was refused")
                case .failed(let reason, let status?) where status < 500: outcome = (false, reason ?? "couldn't be recorded")
                default: outcome = (true, nil)
                }
            }

            if outcome.keep { break }
            queue.removeFirst()
            cache.saveOutbox(queue)
            if let refusal = outcome.refusal { refused.append("\(move.leadName): \(refusal)") } else { sent += 1 }
        }

        return SendReport(sent: sent, refused: refused, stillWaiting: queue.count)
    }

    /// Everything the door kept on this phone goes — on sign-out, so the next person holding it sees no guest list.
    public func forgetEverything() { cache.removeAll() }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private func enqueue(_ move: QueuedDoorMove) {
        var queue = cache.loadOutbox()
        // One kept arrival per party per night: a second scan of the same pass is the same arrival.
        guard !queue.contains(where: { $0.hostedEventBookingId == move.hostedEventBookingId && $0.nightId == move.nightId }) else { return }
        queue.append(move)
        cache.saveOutbox(queue)
    }

    /// A list read from the server, with the arrivals still waiting on the phone shown as in.
    private func overlayQueued(on door: HostedEventDoor) -> HostedEventDoor {
        cache.loadOutbox()
            .filter { $0.hostedEventId == door.hostedEventId && $0.nightId == door.nightId }
            .reduce(door) { shown, move in
                guard let party = shown.expected.first(where: { $0.hostedEventBookingId == move.hostedEventBookingId }), !party.isIn
                else { return shown }
                return Self.marking(shown, move.hostedEventBookingId, in: true, people: move.people, at: move.arrivedUtc)
            }
    }

    static func marking(_ door: HostedEventDoor, _ bookingId: UUID, in arriving: Bool, people: Int?, at: Date) -> HostedEventDoor {
        var door = door
        guard let index = door.expected.firstIndex(where: { $0.hostedEventBookingId == bookingId }) else { return door }
        var party = door.expected[index]
        let wasIn = party.isIn ? (party.peopleIn ?? party.partySize) : 0
        if arriving {
            party.arrivedUtc = party.arrivedUtc ?? at
            party.leftUtc = nil
            party.peopleIn = people
        } else {
            party.arrivedUtc = nil
            party.leftUtc = nil
            party.peopleIn = nil
        }
        let nowIn = party.isIn ? (party.peopleIn ?? party.partySize) : 0
        door.expected[index] = party
        door.peopleIn += nowIn - wasIn
        return door
    }

    private func scanEndpoint(_ orgId: UUID, _ door: HostedEventDoor, _ request: ScanRequest) -> Endpoint? {
        try? Endpoint.json(.post, "api/organizations/\(Self.id(orgId))/events/\(Self.id(door.hostedEventId))/bookings/door/scan", payload: request)
    }

    private func send<Body: Encodable>(_ method: HTTPMethod, _ path: String, _ body: Body) async -> LoadResult<HostedEventDoor> {
        guard let endpoint = try? Endpoint.json(method, path, payload: body) else { return .failed(reason: nil) }
        return await api.load(endpoint, as: HostedEventDoor.self)
    }

    private struct MoveRequest: Encodable {
        let hostedEventBookingId: UUID
        let hostedEventNightId: UUID
        let people: Int?
        let arrivedUtc: Date?
    }

    private struct ScanRequest: Encodable {
        let token: String
        let checkIn: Bool
        let hostedEventNightId: UUID
        let arrivedUtc: Date?
    }

    private struct WalkUpRequest: Encodable {
        let hostedEventNightId: UUID
        let people: Int
        let name: String?
        let note: String?
    }

    private static func base(_ orgId: UUID, _ eventId: UUID) -> String {
        "api/organizations/\(id(orgId))/events/\(id(eventId))/door"
    }

    private static func id(_ id: UUID) -> String { id.uuidString.lowercased() }
}

/// An arrival the door recorded without a signal, waiting to be sent.
public struct QueuedDoorMove: Codable, Sendable, Equatable, Identifiable {
    public enum Kind: String, Codable, Sendable { case arrive, scan }

    public var id: UUID
    public var organizationId: UUID
    public var hostedEventId: UUID
    public var nightId: UUID
    public var kind: Kind
    public var hostedEventBookingId: UUID
    /// The scanned pass, so the server checks the pass itself when it arrives, not just the name.
    public var token: String?
    public var people: Int?
    public var leadName: String
    public var arrivedUtc: Date
}

/// What the door keeps on the phone: the list of doors, each night's list, and the arrivals waiting to be sent.
/// Application Support, protected until the phone is first unlocked after a restart.
public struct DoorCache: Sendable {
    public struct Saved<Value: Codable & Sendable>: Codable, Sendable {
        public var value: Value
        public var savedAt: Date
    }

    private let directory: URL

    public init(directory: URL) { self.directory = directory }

    /// Which store on disk this is, so two screens sending from the same one take turns.
    var key: String { directory.standardizedFileURL.path }

    public static func applicationSupport() -> DoorCache {
        let base = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true))
            ?? FileManager.default.temporaryDirectory
        return DoorCache(directory: base.appendingPathComponent("EventDoors", isDirectory: true))
    }

    public func saveDuties(_ duties: [MyHostedEventDuty], at: Date) { write(Saved(value: duties, savedAt: at), to: directory.appendingPathComponent("duties.json")) }
    public func loadDuties() -> Saved<[MyHostedEventDuty]>? { read(directory.appendingPathComponent("duties.json")) }

    /// Saved under its night, and as the event's last night looked at — what a door opened with no signal and no
    /// night chosen shows.
    public func saveDoor(_ door: HostedEventDoor, at: Date) {
        let saved = Saved(value: door, savedAt: at)
        write(saved, to: eventFolder(door.hostedEventId).appendingPathComponent("\(Self.id(door.nightId)).json"))
        write(saved, to: eventFolder(door.hostedEventId).appendingPathComponent("last.json"))
    }

    public func loadDoor(_ eventId: UUID, night nightId: UUID?) -> Saved<HostedEventDoor>? {
        read(eventFolder(eventId).appendingPathComponent(nightId.map { "\(Self.id($0)).json" } ?? "last.json"))
    }

    public func removeDoors(for eventId: UUID) { try? FileManager.default.removeItem(at: eventFolder(eventId)) }

    public func loadOutbox() -> [QueuedDoorMove] { read(directory.appendingPathComponent("waiting.json")) ?? [] }
    public func saveOutbox(_ moves: [QueuedDoorMove]) { write(moves, to: directory.appendingPathComponent("waiting.json")) }

    public func removeAll() { try? FileManager.default.removeItem(at: directory) }

    private func eventFolder(_ eventId: UUID) -> URL { directory.appendingPathComponent(Self.id(eventId), isDirectory: true) }

    private func write<T: Encodable>(_ value: T, to file: URL) {
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            let data = try Self.encoder.encode(value)
            #if os(iOS)
            try data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
            #else
            try data.write(to: file, options: .atomic)
            #endif
        } catch {
            // Not kept is not shown offline; the door still works with a signal.
        }
    }

    private func read<T: Decodable>(_ file: URL) -> T? {
        guard let data = try? Data(contentsOf: file) else { return nil }
        return try? BenJSON.decoder.decode(T.self, from: data)
    }

    private static func id(_ id: UUID) -> String { id.uuidString.lowercased() }

    /// Keeps fractional seconds, so an arrival time read back is exactly the one kept.
    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(date.formatted(Date.ISO8601FormatStyle(includingFractionalSeconds: true)))
        }
        return encoder
    }()
}
