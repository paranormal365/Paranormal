import Foundation

// Ports of Ben.Service.Models/Feed/FeedRecords.cs — the feed as a reader sees it,
// including everything the F6–F9 arc added (categories, attribution, badges).

/// What kind of media a post carries. `unknown` absorbs any future server value
/// so a new kind never breaks decoding.
public enum FeedMediaKind: Int, Codable, Sendable, Equatable {
    case none = 0
    case image = 1
    case video = 2
    case unknown = -1

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = FeedMediaKind(rawValue: raw) ?? .unknown
    }
}

/// An account named in a post: the id it resolved to (rename-proof), the typed
/// handle (how the body text matches back), and the current display name.
public struct FeedMentionRecord: Sendable, Codable, Equatable, Hashable {
    public var appUserId: UUID
    public var handle: String
    public var displayName: String
}

/// One post in the public feed, as THIS reader sees it. Several fields are
/// reader-relative (`isOwnPost`, `likedByCurrentUser`, `mediaAwaitingReview`,
/// `categoryMatchDegraded`) — never cache a record across sign-in changes.
public struct FeedPostRecord: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var authorAppUserId: UUID
    public var authorDisplayName: String
    public var parentMessageId: UUID?
    /// Plain text by contract — render as text, linkify from `mentions`/`hashtags`.
    public var body: String
    public var dateCreated: Date
    public var replyCount: Int
    public var mentions: [FeedMentionRecord]
    public var hashtags: [String]
    public var authorIsFollowedByCurrentUser: Bool
    public var isOwnPost: Bool
    public var reportedByCurrentUser: Bool
    public var likeCount: Int
    public var likedByCurrentUser: Bool
    /// True only when media exists AND is approved — the media route serves nothing else.
    public var hasMedia: Bool
    /// AUTHOR-ONLY: media attached but unscreened. Everyone else sees a plain text post.
    public var mediaAwaitingReview: Bool
    public var mediaKind: FeedMediaKind
    /// What the author says this shows, from the experience taxonomy (item 186 F6).
    public var experienceTypeId: UUID?
    public var experienceTypeName: String?
    /// AUTHOR-ONLY: the content doesn't look like its chosen type — the recategorize nudge.
    public var categoryMatchDegraded: Bool
    /// The claiming group (item 186 F7) — present ONLY when claimed; absence is structural.
    public var attributedOrgName: String?
    public var attributedOrgUrlName: String?
    public var groupVerified: Bool
    public var moderatorReviewed: Bool
}

/// A page of the feed. `nextCursor` is opaque — pass it back unchanged, never construct one.
public struct FeedPageRecord: Sendable, Codable, Equatable {
    public var posts: [FeedPostRecord]
    public var nextCursor: String?
    /// Server-authoritative: whether THIS reader may write. The composer renders from
    /// nothing else, so the UI and the gate can never disagree.
    public var canPost: Bool
}

/// Somebody's feed profile.
public struct FeedProfileRecord: Sendable, Codable, Equatable {
    public var appUserId: UUID
    public var displayName: String
    public var postCount: Int
    public var followerCount: Int
    public var followingCount: Int
    public var isFollowedByCurrentUser: Bool
    public var isSelf: Bool
}
