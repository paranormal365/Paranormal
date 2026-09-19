import Foundation

/// Sends what is waiting in the room outbox (item 235 phase 14d).
///
/// **Oldest first, and it stops at the first that cannot get through**, so photos arrive in the order they were
/// taken and a basement with no signal does not burn through every post with the same failure. A post the server
/// refuses — the room closed, photos kept to the team, a file it can't read — is kept with its sentence for the
/// person to see and remove, and the next one is tried.
@MainActor
public final class RoomOutboxSender {
    private let store: HostedEventsStore
    private let outbox: RoomOutbox

    /// One send at a time per outbox on disk, however many screens ask.
    private static var sending: Set<String> = []

    public init(store: HostedEventsStore, outbox: RoomOutbox = .shared()) {
        self.store = store
        self.outbox = outbox
    }

    public struct Report: Equatable, Sendable {
        public var sent: Int
        public var refused: Int
        public var stillWaiting: Int
        /// The room as the last successful post left it, per event — so an open room can show its new posts.
        public var rooms: [UUID: EventRoom]
    }

    public func sendWaiting() async -> Report {
        guard !Self.sending.contains(outbox.key) else {
            return Report(sent: 0, refused: 0, stillWaiting: outbox.waiting().count, rooms: [:])
        }
        Self.sending.insert(outbox.key)
        defer { Self.sending.remove(outbox.key) }

        var sent = 0
        var refused = 0
        var rooms: [UUID: EventRoom] = [:]

        for post in outbox.waiting() {
            let media = outbox.media(for: post)
            if post.storedFileName != nil && media.map({ FileManager.default.fileExists(atPath: $0.fileURL.path) }) != true {
                outbox.markRefused(post.id, because: "The photo is no longer on this phone, so there's nothing to send.")
                refused += 1
                continue
            }

            switch await store.post(post.hostedEventId, body: post.body, media: media,
                                    sendToHosts: post.sendToHosts, agreeToShow: post.agreeToShow) {
            case .ok(let room):
                outbox.remove(post.id)
                rooms[post.hostedEventId] = room
                sent += 1
            case .failed(let reason, let status?) where status < 500 && status != 408:
                outbox.markRefused(post.id, because: reason ?? Self.sentence(for: status))
                refused += 1
            default:
                // No signal, a server having a bad minute, or a session to sign back into: keep the rest for later.
                return Report(sent: sent, refused: refused, stillWaiting: outbox.waiting().count, rooms: rooms)
            }
        }
        return Report(sent: sent, refused: refused, stillWaiting: outbox.waiting().count, rooms: rooms)
    }

    private static func sentence(for status: Int) -> String {
        switch status {
        case 404: "You're no longer one of the people at this event, so the room can't take it."
        case 413: "That file is too large for the site to take."
        default: "The room wouldn't take it."
        }
    }
}

/// The events this person could add photos to, written by the app for the Share Extension to offer (phase 14d).
///
/// The extension has no sign-in of its own, so it cannot ask the server; the app writes what it already knows —
/// events the person is confirmed at or helping at, and, once the room has been opened, whether it takes their
/// photos and what the photo notice says — and the extension reads it.
public struct ShareableEvents: Sendable {
    public struct Event: Codable, Sendable, Equatable, Identifiable {
        public var hostedEventId: UUID
        public var eventName: String
        public var organizationName: String?
        public var startsOn: Date
        public var endsOn: Date
        /// Nil until the room has been opened in the app.
        public var canAddPhotos: Bool?
        public var needsPhotoConsent: Bool?
        public var photoNotice: String?
        public var hostNames: [String]

        public var id: UUID { hostedEventId }

        public init(hostedEventId: UUID, eventName: String, organizationName: String?, startsOn: Date, endsOn: Date,
                    canAddPhotos: Bool? = nil, needsPhotoConsent: Bool? = nil, photoNotice: String? = nil, hostNames: [String] = []) {
            self.hostedEventId = hostedEventId
            self.eventName = eventName
            self.organizationName = organizationName
            self.startsOn = startsOn
            self.endsOn = endsOn
            self.canAddPhotos = canAddPhotos
            self.needsPhotoConsent = needsPhotoConsent
            self.photoNotice = photoNotice
            self.hostNames = hostNames
        }
    }

    private let file: URL

    public init(file: URL) { self.file = file }

    public static func shared() -> ShareableEvents {
        ShareableEvents(file: SharedContainer.url().appendingPathComponent("shareable-events.json"))
    }

    /// Replaces the list with these events, keeping what an earlier room read learned about each.
    public func save(_ events: [Event]) {
        let known = Dictionary(load().map { ($0.hostedEventId, $0) }, uniquingKeysWith: { first, _ in first })
        let merged = events.map { event -> Event in
            guard let earlier = known[event.hostedEventId], event.canAddPhotos == nil else { return event }
            var kept = event
            kept.canAddPhotos = earlier.canAddPhotos
            kept.needsPhotoConsent = earlier.needsPhotoConsent
            kept.photoNotice = earlier.photoNotice
            if kept.hostNames.isEmpty { kept.hostNames = earlier.hostNames }
            return kept
        }
        write(merged)
    }

    /// What opening the room learned: whether it takes this person's photos, and what they must agree to. `event`
    /// adds it to the list when it isn't there yet — the room can be opened before the app has written the list.
    public func learn(from room: EventRoom, for eventId: UUID, event: Event? = nil) {
        var events = load()
        if !events.contains(where: { $0.hostedEventId == eventId }), let event { events.append(event) }
        guard let index = events.firstIndex(where: { $0.hostedEventId == eventId }) else { return }
        events[index].canAddPhotos = room.canAddPhotos && room.canPost
        events[index].needsPhotoConsent = room.needsPhotoConsent
        events[index].photoNotice = room.photoNotice
        events[index].hostNames = room.hostNames
        write(events)
    }

    public func load() -> [Event] {
        guard let data = try? Data(contentsOf: file) else { return [] }
        return (try? Self.decoder.decode([Event].self, from: data)) ?? []
    }

    /// The ones worth offering: not known to refuse photos, and not over by more than a week — the room closes then.
    public func offerable(now: Date = Date()) -> [Event] {
        load().filter { $0.canAddPhotos != false && $0.endsOn.addingTimeInterval(8 * 86_400) >= now }
            .sorted { abs($0.startsOn.timeIntervalSince(now)) < abs($1.startsOn.timeIntervalSince(now)) }
    }

    public func removeAll() { try? FileManager.default.removeItem(at: file) }

    private func write(_ events: [Event]) {
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            let data = try Self.encoder.encode(events)
            #if os(iOS)
            try data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
            #else
            try data.write(to: file, options: .atomic)
            #endif
        } catch {}
    }

    private static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()

    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }()
}
