import Foundation

/// Sending a recorded session to the server.
///
/// **Two phases, and a file at a time.** The document goes first and creates the record; each
/// recording follows separately. A night of video is gigabytes over whatever connection somebody
/// got home to, and one monolithic upload bets the whole session on none of it dropping. This
/// way a failure costs one file, and the rest can be retried without re-sending what landed.
public struct FieldUploadClient: Sendable {

    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// What the server holds for a session, once its document has arrived.
    public struct ServerSession: Sendable, Codable, Equatable {
        public var id: UUID
        public var investigationId: UUID?
        public var deviceSessionId: UUID
        public var readingCount: Int
        public var markerCount: Int
        public var recordedByName: String?
        public var files: [ServerFile]
    }

    public struct ServerFile: Sendable, Codable, Equatable, Identifiable {
        public var id: UUID
        public var relativePath: String
        public var fileSize: Int64
        public var sha256: String?
        /// False when the bytes that arrived did not match the digest sent with them. Surfaced
        /// rather than swallowed: a truncated upload nobody noticed is worse than a refused one.
        public var digestMatched: Bool
    }

    /// Sends the session document. Safe to repeat — the device's own session id makes a retry
    /// find its existing record instead of making a second copy.
    public func submitDocument(_ document: Data,
                               deviceSessionId: UUID,
                               investigationId: UUID?,
                               recordedByAppUserId: UUID?,
                               recordedByName: String?) async -> Result<ServerSession, FeedActionError> {
        var parts: [MultipartBody.Part] = [
            .file("file", filename: "data.json", contentType: "application/json", data: document),
            .field("deviceSessionId", deviceSessionId.uuidString),
        ]
        // Omitted rather than sent empty: no investigation is an ordinary state, not a blank.
        if let investigationId { parts.append(.field("investigationId", investigationId.uuidString)) }
        if let recordedByAppUserId {
            parts.append(.field("recordedByAppUserId", recordedByAppUserId.uuidString))
        }
        if let recordedByName, !recordedByName.isEmpty {
            parts.append(.field("recordedByName", recordedByName))
        }

        let endpoint = Endpoint(.post, "api/field-sessions/document",
                                body: .multipart(MultipartBody(parts: parts)))
        return await result(of: await api.upload(endpoint, as: ServerSession.self))
    }

    /// Sends one recording, with the digest the device computed so the server can check it.
    public func submitFile(sessionId: UUID, fileURL: URL, relativePath: String,
                           contentType: String,
                           sha256: String?) async -> Result<ServerFile, FeedActionError> {
        var parts: [MultipartBody.Part] = [
            .file("file", filename: (relativePath as NSString).lastPathComponent,
                  contentType: contentType, url: fileURL),
            .field("relativePath", relativePath),
        ]
        if let sha256 { parts.append(.field("sha256", sha256)) }

        let endpoint = Endpoint(
            .post, "api/field-sessions/\(sessionId.uuidString.lowercased())/files",
            body: .multipart(MultipartBody(parts: parts)))
        return await result(of: await api.upload(endpoint, as: ServerFile.self))
    }

    public func mySessions() async -> LoadResult<[ServerSession]> {
        await api.load(Endpoint(.get, "api/field-sessions/mine"), as: [ServerSession].self)
    }

    private func result<T: Sendable>(of load: LoadResult<T>) -> Result<T, FeedActionError> {
        switch load {
        case .ok(let value): .success(value)
        case .failed(let reason, _): .failure(.failed(reason: reason))
        case .sessionEnded: .failure(.sessionEnded)
        case .rateLimited(let after): .failure(.rateLimited(retryAfter: after))
        }
    }
}
