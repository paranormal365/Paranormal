import Foundation

/// Everything the feed lets a participant DO: post, like, follow, report, recategorize
/// (item 186 F2–F7). Every method answers with the honest outcome — a refusal keeps the
/// server's sentence, because "posting on the feed is for people who belong here…" is
/// something a person can act on and "couldn't post" is not.
public struct FeedActions: Sendable {
    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// What the composer needs to offer categories. An empty list is a working composer
    /// without a picker — the category is optional, so a failed taxonomy fetch must not
    /// block posting.
    public func experienceTaxonomy() async -> [ExperienceCategoryWithTypes] {
        let result = await api.load(
            Endpoint(.get, "api/experience-categories/with-types", requiresAuth: false),
            as: [ExperienceCategoryWithTypes].self)
        return result.value?.selectable ?? []
    }

    /// A new post, or a reply. Media rides the same multipart door the website uses, so a
    /// phone upload is ingested, stripped, screened and scored exactly like any other.
    ///
    /// - Returns: the created post, or the server's refusal sentence.
    public func createPost(
        body: String,
        parentPostId: UUID? = nil,
        experienceTypeId: UUID? = nil,
        media: MediaUpload? = nil
    ) async -> Result<FeedPostRecord, FeedActionError> {
        var parts: [MultipartBody.Part] = [.field("Body", body)]
        if let parentPostId {
            parts.append(.field("ParentMessageId", parentPostId.uuidString.lowercased()))
        }
        if let experienceTypeId {
            parts.append(.field("ExperienceTypeId", experienceTypeId.uuidString.lowercased()))
        }
        if let media {
            parts.append(.file("media", filename: media.filename,
                               contentType: media.contentType, url: media.fileURL))
        }

        let endpoint = Endpoint(.post, "api/feed/posts", body: .multipart(MultipartBody(parts: parts)))
        return outcome(await api.upload(endpoint, as: FeedPostRecord.self))
    }

    /// Liking twice is liking once (the server's composite key decides), so the caller may
    /// be optimistic and reconcile from the boolean.
    public func setLiked(_ liked: Bool, postId: UUID) async -> Bool {
        let path = "api/feed/posts/\(postId.uuidString.lowercased())/like"
        let result = await api.send(Endpoint(liked ? .post : .delete, path))
        return result.isOk
    }

    public func setFollowing(_ following: Bool, appUserId: UUID) async -> Bool {
        let path = "api/feed/follow/\(appUserId.uuidString.lowercased())"
        let result = await api.send(Endpoint(following ? .post : .delete, path))
        return result.isOk
    }

    /// Reporting twice is one report, and the answer is the same either way — a reporter
    /// cannot learn whether their first report was already acted on.
    public func report(postId: UUID, reason: String?) async -> Bool {
        guard let endpoint = try? Endpoint.json(
            .post, "api/feed/posts/\(postId.uuidString.lowercased())/report",
            payload: ReportRequest(reason: reason))
        else { return false }
        return await api.send(endpoint).isOk
    }

    /// The author's answer to the mismatch nudge (item 186 F6). Nil clears the category.
    public func recategorize(
        postId: UUID, experienceTypeId: UUID?
    ) async -> Result<FeedPostRecord, FeedActionError> {
        guard let endpoint = try? Endpoint.json(
            .put, "api/feed/posts/\(postId.uuidString.lowercased())/experience-type",
            payload: RecategorizeRequest(experienceTypeId: experienceTypeId))
        else { return .failure(.failed(reason: nil)) }
        return outcome(await api.load(endpoint, as: FeedPostRecord.self))
    }

    private func outcome(_ result: LoadResult<FeedPostRecord>) -> Result<FeedPostRecord, FeedActionError> {
        switch result {
        case .ok(let post): .success(post)
        case .failed(let reason, _): .failure(.failed(reason: reason))
        case .sessionEnded: .failure(.sessionEnded)
        case .rateLimited(let retryAfter): .failure(.rateLimited(retryAfter: retryAfter))
        }
    }

    private struct ReportRequest: Encodable { let reason: String? }
    private struct RecategorizeRequest: Encodable { let experienceTypeId: UUID? }
}

/// Why a write did not happen, in terms the UI can put in front of a person.
public enum FeedActionError: Error, Sendable, Equatable {
    /// Carries the server's own sentence when it wrote one — the participation refusal,
    /// the 1000-character cap, "that category isn't available".
    case failed(reason: String?)
    case sessionEnded
    case rateLimited(retryAfter: TimeInterval?)

    public var message: String {
        switch self {
        case .failed(let reason):
            reason ?? "Couldn't post that. Try again in a moment."
        case .sessionEnded:
            "Your session ended — sign in again to post."
        case .rateLimited(let retryAfter):
            retryAfter.map { "Too many requests — try again in \(Int($0.rounded(.up))) seconds." }
                ?? "Too many requests — try again shortly."
        }
    }
}

/// A file staged for upload. Held as a URL, not bytes: a 200 MB video must not sit on the
/// heap while somebody finishes typing their caption.
public struct MediaUpload: Sendable, Equatable {
    public var fileURL: URL
    public var filename: String
    public var contentType: String
    /// For the composer's own display; the server does its own validation.
    public var byteCount: Int64

    public init(fileURL: URL, filename: String, contentType: String, byteCount: Int64) {
        self.fileURL = fileURL
        self.filename = filename
        self.contentType = contentType
        self.byteCount = byteCount
    }

    public var isVideo: Bool { contentType.hasPrefix("video/") }

    public var displaySize: String {
        let bytes = Double(byteCount)
        if bytes >= 1_048_576 { return String(format: "%.1f MB", bytes / 1_048_576) }
        if bytes >= 1024 { return String(format: "%.0f KB", bytes / 1024) }
        return "\(byteCount) bytes"
    }
}
