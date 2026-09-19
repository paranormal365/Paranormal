import Foundation
import Observation

/// Which slice of the feed a list shows. Mirrors the website's URL space: the
/// main modes, one hashtag, one experience type, one author.
public enum FeedFilter: Sendable, Equatable, Hashable {
    case forYou
    case latest
    case following
    case hashtag(String)
    case experienceType(UUID, name: String?)
    case author(UUID)

    var queryItems: [URLQueryItem] {
        switch self {
        case .forYou: [URLQueryItem(name: "mode", value: "foryou")]
        case .latest: [URLQueryItem(name: "mode", value: "all")]
        case .following: [URLQueryItem(name: "mode", value: "following")]
        case .hashtag(let tag): [URLQueryItem(name: "mode", value: "all"),
                                 URLQueryItem(name: "hashtag", value: tag)]
        case .experienceType(let id, _): [URLQueryItem(name: "mode", value: "all"),
                                          URLQueryItem(name: "type", value: id.uuidString.lowercased())]
        case .author(let id): [URLQueryItem(name: "author", value: id.uuidString.lowercased())]
        }
    }

    public var title: String {
        switch self {
        case .forYou: "For You"
        case .latest: "Latest"
        case .following: "Following"
        case .hashtag(let tag): "#\(tag)"
        case .experienceType(_, let name): name ?? "Category"
        case .author: "Posts"
        }
    }
}

/// One feed list's state: pages, the opaque cursor, and the de-dupe the For You
/// mode requires (its offset cursor can re-serve a post whose rank moved).
@Observable
@MainActor
public final class FeedStore {
    public enum State: Equatable {
        case loading
        case loaded
        /// The API 404s the WHOLE controller when `features.public-feed` is off.
        /// A switched-off feature is not an error and must never render as one.
        case featureUnavailable
        case failed(reason: String?)
        case rateLimited(retryAfter: TimeInterval?)
    }

    public private(set) var state: State = .loading
    public private(set) var posts: [FeedPostRecord] = []
    public private(set) var canPost = false
    public private(set) var isLoadingMore = false
    public var hasMore: Bool { nextCursor != nil }

    public let filter: FeedFilter

    private let api: APIClient
    private var nextCursor: String?
    private var seenIds: Set<UUID> = []

    public init(filter: FeedFilter, api: APIClient) {
        self.filter = filter
        self.api = api
    }

    public func load() async {
        state = .loading
        posts = []
        seenIds = []
        nextCursor = nil
        await fetchPage(cursor: nil)
    }

    public func loadMore() async {
        guard let cursor = nextCursor, !isLoadingMore else { return }
        isLoadingMore = true
        defer { isLoadingMore = false }
        await fetchPage(cursor: cursor)
    }

    /// Replaces one post in place — a like toggled, a recategorize answered.
    public func replace(_ post: FeedPostRecord) {
        if let index = posts.firstIndex(where: { $0.id == post.id }) {
            posts[index] = post
        }
    }

    /// A new post goes straight to the top of what the reader is looking at. On a filtered
    /// list it may not belong there at all — the caller decides; this only inserts.
    public func prepend(_ post: FeedPostRecord) {
        guard seenIds.insert(post.id).inserted else { return }
        posts.insert(post, at: 0)
    }

    /// Toggles a like optimistically and reconciles: the count moves under the reader's
    /// thumb, and a refused call puts it back rather than leaving a lie on screen.
    public func toggleLike(_ post: FeedPostRecord, actions: FeedActions) async {
        let wanted = !post.likedByCurrentUser
        applyLike(postId: post.id, liked: wanted)

        if !(await actions.setLiked(wanted, postId: post.id)) {
            applyLike(postId: post.id, liked: !wanted)   // rollback
        }
    }

    /// Follow state lives on every card by the same author, so all of them move together —
    /// a feed where one card says Following and the next says Follow reads as broken.
    public func toggleFollow(_ post: FeedPostRecord, actions: FeedActions) async {
        let wanted = !post.authorIsFollowedByCurrentUser
        applyFollow(authorId: post.authorAppUserId, following: wanted)

        if !(await actions.setFollowing(wanted, appUserId: post.authorAppUserId)) {
            applyFollow(authorId: post.authorAppUserId, following: !wanted)
        }
    }

    /// Blocks a post's author and removes everything of theirs already on screen.
    ///
    /// The local removal is not an optimistic nicety: the server filters the NEXT fetch, but the
    /// posts a person just blocked somebody over are the ones in front of them right now, and a
    /// block that leaves the abuse visible until a refresh reads as a block that did nothing.
    public func blockAuthor(of post: FeedPostRecord, actions: FeedActions) async -> Bool {
        guard await actions.block(appUserId: post.authorAppUserId) else { return false }
        posts.removeAll { $0.authorAppUserId == post.authorAppUserId }
        return true
    }

    /// Reporting is idempotent server-side; the card's control flips to "Reported" and stays.
    public func report(_ post: FeedPostRecord, reason: String?, actions: FeedActions) async -> Bool {
        guard await actions.report(postId: post.id, reason: reason) else { return false }
        if let index = posts.firstIndex(where: { $0.id == post.id }) {
            posts[index].reportedByCurrentUser = true
        }
        return true
    }

    private func applyLike(postId: UUID, liked: Bool) {
        guard let index = posts.firstIndex(where: { $0.id == postId }) else { return }
        // Counted from the row's own state so a double-tap can't drift the number.
        guard posts[index].likedByCurrentUser != liked else { return }
        posts[index].likedByCurrentUser = liked
        posts[index].likeCount = max(0, posts[index].likeCount + (liked ? 1 : -1))
    }

    private func applyFollow(authorId: UUID, following: Bool) {
        for index in posts.indices where posts[index].authorAppUserId == authorId {
            posts[index].authorIsFollowedByCurrentUser = following
        }
    }

    private func fetchPage(cursor: String?) async {
        var query = filter.queryItems
        if let cursor { query.append(URLQueryItem(name: "cursor", value: cursor)) }

        let result = await api.load(
            Endpoint(.get, "api/feed", query: query, requiresAuth: true),
            as: FeedPageRecord.self)

        switch result {
        case .ok(let page):
            // De-dupe on append: the For You offset cursor has no stable meaning
            // (ranks move between pages), so a repeat is expected, not a bug.
            let fresh = page.posts.filter { seenIds.insert($0.id).inserted }
            posts.append(contentsOf: fresh)
            nextCursor = page.nextCursor
            canPost = page.canPost
            state = .loaded
        case .failed(_, let statusCode) where statusCode == 404:
            state = .featureUnavailable
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .sessionEnded:
            // The feed reads anonymously; a dead token must not blank the page.
            // SessionStore has already raised its banner — retry as a visitor.
            let anonymous = await api.load(
                Endpoint(.get, "api/feed", query: query, requiresAuth: false),
                as: FeedPageRecord.self)
            if case .ok(let page) = anonymous {
                let fresh = page.posts.filter { seenIds.insert($0.id).inserted }
                posts.append(contentsOf: fresh)
                nextCursor = page.nextCursor
                canPost = false
                state = .loaded
            } else {
                state = .failed(reason: nil)
            }
        case .rateLimited(let retryAfter):
            state = .rateLimited(retryAfter: retryAfter)
        }
    }
}

/// One thread: the root post and its replies, plus the same feature-gate honesty.
@Observable
@MainActor
public final class FeedThreadStore {
    public private(set) var state: FeedStore.State = .loading
    public private(set) var posts: [FeedPostRecord] = []

    private let api: APIClient
    private let postId: UUID

    public init(postId: UUID, api: APIClient) {
        self.postId = postId
        self.api = api
    }

    public func load() async {
        state = .loading
        let result = await api.load(
            Endpoint(.get, "api/feed/posts/\(postId.uuidString.lowercased())"),
            as: [FeedPostRecord].self)
        switch result {
        case .ok(let thread):
            posts = thread
            state = .loaded
        case .failed(_, let statusCode) where statusCode == 404:
            // The whole feature off, or the post gone/hidden — either way there is
            // nothing here to show and nothing broken to apologize for.
            state = .featureUnavailable
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .sessionEnded:
            state = .failed(reason: nil)
        case .rateLimited(let retryAfter):
            state = .rateLimited(retryAfter: retryAfter)
        }
    }
}
