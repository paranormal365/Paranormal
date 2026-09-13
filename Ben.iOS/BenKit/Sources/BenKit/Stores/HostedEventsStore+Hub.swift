import Foundation

/// What a guest does DURING a hosted event (item 235 phase 14b): read the programme and sign up for its
/// sessions, read the menus, open the downloads, and take part in the room.
///
/// **Absent is not failed.** An event with no published programme answers 404, a guest who is not yet
/// confirmed is refused the menus, and somebody not at the event is not in its room. Each of those comes
/// back as `.ok(nil)`, so the hub leaves the row out instead of drawing an error for something that simply
/// isn't there. An unreachable server is still `.failed`.
///
/// **Refusals are the server's sentences.** "That session is full — you're on the waiting list" and "Only
/// the person who posted it can send it on" are things a person can act on; they reach the screen unchanged.
extension HostedEventsStore {

    // ── the programme ────────────────────────────────────────────────────────

    public func loadProgramme(_ eventId: UUID) async -> LoadResult<HostedEventProgramme?> {
        absentIsNil(await api.load(Endpoint(.get, "\(Self.base(eventId))/programme"), as: HostedEventProgramme.self))
    }

    /// Signs up `people` of the party. A full session puts them on its waiting list; the answer says which.
    public func signUp(_ eventId: UUID, session sessionId: UUID, people: Int) async -> LoadResult<HostedEventProgramme> {
        guard let endpoint = try? Endpoint.json(
            .post, "\(Self.base(eventId))/sessions/\(Self.id(sessionId))/sign-up", payload: SignUpRequest(people: people))
        else { return .failed(reason: nil) }
        return await api.load(endpoint, as: HostedEventProgramme.self)
    }

    /// Gives the place up; the first person waiting is moved up and told.
    public func leave(_ eventId: UUID, session sessionId: UUID) async -> LoadResult<HostedEventProgramme> {
        await api.load(Endpoint(.delete, "\(Self.base(eventId))/sessions/\(Self.id(sessionId))/sign-up"),
                       as: HostedEventProgramme.self)
    }

    /// The guest has looked, so the bell's "the programme changed" row clears.
    @discardableResult
    public func markProgrammeSeen(_ eventId: UUID) async -> Bool {
        await api.send(Endpoint(.post, "\(Self.base(eventId))/programme/seen")).isOk
    }

    /// The whole programme as one calendar file. Anonymous, so Safari or Calendar can open it directly.
    public static func programmeCalendarEndpoint(_ eventId: UUID) -> Endpoint {
        Endpoint(.get, "\(base(eventId))/programme.ics", requiresAuth: false)
    }

    // ── menus ────────────────────────────────────────────────────────────────

    /// Nil when the venue does not publish menus to this reader yet (their place is not agreed).
    public func loadMenus(_ eventId: UUID) async -> LoadResult<HostedEventMenus?> {
        absentIsNil(await api.load(Endpoint(.get, "\(Self.base(eventId))/menus"), as: HostedEventMenus.self))
    }

    // ── downloads ────────────────────────────────────────────────────────────

    /// Only the files that reach this reader. None, or no event, is an empty list.
    public func loadFiles(_ eventId: UUID) async -> LoadResult<[HostedEventFile]> {
        switch await api.load(Endpoint(.get, "\(Self.base(eventId))/files"), as: [HostedEventFile].self) {
        case .ok(let files): return .ok(files)
        case .failed(_, 404): return .ok([])
        case .failed(let reason, let status): return .failed(reason: reason, statusCode: status)
        case .sessionEnded: return .sessionEnded
        case .rateLimited(let after): return .rateLimited(retryAfter: after)
        }
    }

    /// Downloads one file into Caches under its own name, so Quick Look and the share sheet show that name.
    public func download(_ eventId: UUID, file: HostedEventFile) async -> LoadResult<URL> {
        let folder = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("EventDownloads/\(Self.id(file.id))", isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let destination = folder.appendingPathComponent(Self.safeFileName(file.fileName))
        return await api.download(Endpoint(.get, "\(Self.base(eventId))/files/\(Self.id(file.id))/download"), to: destination)
    }

    // ── the room ─────────────────────────────────────────────────────────────

    /// Nil when this reader is not one of the people at the event. `before` pages back through older posts.
    public func loadRoom(_ eventId: UUID, before: Date? = nil) async -> LoadResult<EventRoom?> {
        var query: [URLQueryItem] = []
        if let before { query.append(URLQueryItem(name: "before", value: Self.isoWithFraction.string(from: before))) }
        return absentIsNil(await api.load(Endpoint(.get, "\(Self.base(eventId))/room", query: query), as: EventRoom.self))
    }

    /// One post: words, a photo or video, or both. `agreeToShow` answers the once-per-event photo notice;
    /// `sendToHosts` shares the photo with the organizers and venue — it stays the poster's either way.
    public func post(_ eventId: UUID, body: String, media: MediaUpload?,
                     sendToHosts: Bool, agreeToShow: Bool) async -> LoadResult<EventRoom> {
        var parts: [MultipartBody.Part] = [
            .field("body", body),
            .field("sendToHosts", sendToHosts ? "true" : "false"),
            .field("agreeToShow", agreeToShow ? "true" : "false"),
        ]
        if let media {
            parts.append(.file("media", filename: media.filename, contentType: media.contentType, url: media.fileURL))
        }
        return await api.upload(Endpoint(.post, "\(Self.base(eventId))/room", body: .multipart(MultipartBody(parts: parts))),
                                as: EventRoom.self)
    }

    public func sendToHosts(_ eventId: UUID, message messageId: UUID) async -> LoadResult<EventRoom> {
        await api.load(Endpoint(.post, "\(Self.base(eventId))/room/messages/\(Self.id(messageId))/send-to-hosts"), as: EventRoom.self)
    }

    /// Takes a post down. Its photo stays in the poster's own files.
    public func takeDown(_ eventId: UUID, message messageId: UUID) async -> LoadResult<EventRoom> {
        await api.load(Endpoint(.delete, "\(Self.base(eventId))/room/messages/\(Self.id(messageId))"), as: EventRoom.self)
    }

    public func report(_ eventId: UUID, message messageId: UUID, reason: String?) async -> LoadResult<EventRoom> {
        guard let endpoint = try? Endpoint.json(
            .post, "\(Self.base(eventId))/room/messages/\(Self.id(messageId))/report", payload: ReportRequest(reason: reason))
        else { return .failed(reason: nil) }
        return await api.load(endpoint, as: EventRoom.self)
    }

    /// A post's photo or video, for the people in the room.
    public static func roomMediaEndpoint(_ eventId: UUID, message messageId: UUID) -> Endpoint {
        Endpoint(.get, "\(base(eventId))/room/messages/\(id(messageId))/media")
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private struct SignUpRequest: Encodable { let people: Int }
    private struct ReportRequest: Encodable { let reason: String? }

    private static func base(_ eventId: UUID) -> String { "api/public/hosted-events/\(id(eventId))" }
    private static func id(_ id: UUID) -> String { id.uuidString.lowercased() }

    /// A refused read of something the reader has no part in (403/404) is "not there", not an error.
    private func absentIsNil<T>(_ result: LoadResult<T>) -> LoadResult<T?> {
        switch result {
        case .ok(let value): return .ok(value)
        case .failed(_, let status?) where status == 403 || status == 404: return .ok(nil)
        case .failed(let reason, let status): return .failed(reason: reason, statusCode: status)
        case .sessionEnded: return .sessionEnded
        case .rateLimited(let after): return .rateLimited(retryAfter: after)
        }
    }

    /// A file name from somebody else, made safe to be the last path component.
    static func safeFileName(_ name: String) -> String {
        let cleaned = name.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "-")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return cleaned.isEmpty || cleaned.hasPrefix(".") ? "download" + cleaned : cleaned
    }

    private static let isoWithFraction: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()
}
