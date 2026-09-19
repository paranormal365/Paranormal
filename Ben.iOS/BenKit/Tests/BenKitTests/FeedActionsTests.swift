import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Feed participation (iOS Slice 4): the multipart door's exact shape, optimistic
/// like/follow with rollback, and refusals that keep the server's own words.
@Suite("FeedActions — the write surface")
@MainActor
struct FeedActionsTests {

    private nonisolated static func postJSON(_ id: UUID = UUID(), liked: Bool = false, likes: Int = 0) -> String {
        """
        {"id":"\(id.uuidString.lowercased())","authorAppUserId":"\(UUID().uuidString.lowercased())",
         "authorDisplayName":"A","parentMessageId":null,"body":"b","dateCreated":"2026-08-24T12:00:00Z",
         "replyCount":0,"mentions":[],"hashtags":[],"authorIsFollowedByCurrentUser":false,
         "isOwnPost":false,"reportedByCurrentUser":false,"likeCount":\(likes),
         "likedByCurrentUser":\(liked),"hasMedia":false,"mediaAwaitingReview":false,"mediaKind":0,
         "experienceTypeId":null,"experienceTypeName":null,"categoryMatchDegraded":false,
         "attributedOrgName":null,"attributedOrgUrlName":null,
         "groupVerified":false,"moderatorReviewed":false}
        """
    }

    private static func actions(_ transport: MockTransport) -> FeedActions {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return FeedActions(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    // ── The multipart door ──────────────────────────────────────────────────

    @Test func textPostSendsMultipartWithPascalCaseFields() async {
        let transport = MockTransport { request in
            (Data(Self.postJSON().utf8), MockTransport.response(for: request, status: 200))
        }
        _ = await Self.actions(transport).createPost(body: "a cold spot #evp")

        let sent = transport.requests.first
        let contentType = sent?.value(forHTTPHeaderField: "Content-Type") ?? ""
        #expect(contentType.hasPrefix("multipart/form-data; boundary="))

        let body = String(decoding: sent?.httpBody ?? Data(), as: UTF8.self)
        // PascalCase because ASP.NET binds FORM fields by the C# parameter names — unlike
        // JSON responses, which are camelCase. Getting this wrong posts an empty body.
        #expect(body.contains("name=\"Body\""))
        #expect(body.contains("a cold spot #evp"))
        #expect(!body.contains("name=\"body\""))
    }

    @Test func replyWithCategoryAndMediaCarriesEveryPart() async throws {
        let file = FileManager.default.temporaryDirectory
            .appendingPathComponent("slice4-\(UUID().uuidString).jpg")
        try Data([0xFF, 0xD8, 0xFF, 0xE0]).write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }

        let parent = UUID(), type = UUID()
        let transport = MockTransport { request in
            (Data(Self.postJSON().utf8), MockTransport.response(for: request, status: 200))
        }

        _ = await Self.actions(transport).createPost(
            body: "the landing again",
            parentPostId: parent,
            experienceTypeId: type,
            media: MediaUpload(fileURL: file, filename: "shot.jpg",
                               contentType: "image/jpeg", byteCount: 4))

        let body = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains("name=\"ParentMessageId\""))
        #expect(body.contains(parent.uuidString.lowercased()))
        #expect(body.contains("name=\"ExperienceTypeId\""))
        #expect(body.contains(type.uuidString.lowercased()))
        // The file part is named exactly `media` — the controller's IFormFile parameter.
        #expect(body.contains("name=\"media\"; filename=\"shot.jpg\""))
        #expect(body.contains("Content-Type: image/jpeg"))
    }

    @Test func aRefusalKeepsTheServersSentence() async {
        // The participation gate's actual wording — the thing a person can act on.
        let refusal = "Posting on the feed is for people who belong here — members of an "
                    + "investigation group, and clients whose case is being worked."
        let transport = MockTransport(status: 400, body: Data(refusal.utf8))

        let result = await Self.actions(transport).createPost(body: "hello")
        guard case .failure(let error) = result else {
            Issue.record("expected a refusal"); return
        }
        #expect(error.message == refusal)
    }

    @Test func aRateLimitedPostCountsDownRatherThanBlamingTheUser() async {
        let transport = MockTransport(status: 429, headers: ["Retry-After": "30"])
        let result = await Self.actions(transport).createPost(body: "hello")
        guard case .failure(let error) = result else {
            Issue.record("expected a refusal"); return
        }
        #expect(error == .rateLimited(retryAfter: 30))
        #expect(error.message.contains("30 seconds"))
    }

    // ── Optimistic like / follow ────────────────────────────────────────────

    private static func storeWithOnePost(
        _ transport: MockTransport, liked: Bool = false, likes: Int = 3
    ) async -> (FeedStore, FeedActions, FeedPostRecord) {
        let id = UUID()
        let page = Data("""
        {"posts":[\(Self.postJSON(id, liked: liked, likes: likes))],"nextCursor":null,"canPost":true}
        """.utf8)

        // The page load answers 200; every later write answers from the caller's transport.
        // Method-based rather than call-counting: no shared mutable state to make Sendable,
        // and the store only ever GETs the page.
        let composite = MockTransport { request in
            request.httpMethod == "GET"
                ? (page, MockTransport.response(for: request, status: 200))
                : try await transport.send(request)
        }
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: composite, environment: { .dev })
        let api = APIClient(environment: { .dev }, transport: composite, tokens: tokens)
        let store = FeedStore(filter: .latest, api: api)
        await store.load()
        return (store, FeedActions(api: api), store.posts[0])
    }

    @Test func likeMovesTheCountAtOnce() async {
        let (store, actions, post) = await Self.storeWithOnePost(MockTransport(status: 204))
        await store.toggleLike(post, actions: actions)
        #expect(store.posts[0].likedByCurrentUser)
        #expect(store.posts[0].likeCount == 4)
    }

    @Test func aRefusedLikeRollsBackRatherThanLeavingALieOnScreen() async {
        let (store, actions, post) = await Self.storeWithOnePost(MockTransport(status: 403))
        await store.toggleLike(post, actions: actions)
        #expect(!store.posts[0].likedByCurrentUser)
        #expect(store.posts[0].likeCount == 3)
    }

    @Test func unlikeCannotDriveTheCountNegative() async {
        let (store, actions, post) = await Self.storeWithOnePost(
            MockTransport(status: 204), liked: true, likes: 0)
        await store.toggleLike(post, actions: actions)   // unlike a 0-count post
        #expect(store.posts[0].likeCount == 0)
    }

    @Test func followMovesEveryCardByThatAuthor() async {
        // Two posts, one author: a feed where one card says Following and the next says
        // Follow reads as broken.
        let author = UUID()
        func postBy(_ id: UUID) -> String {
            """
            {"id":"\(id.uuidString.lowercased())","authorAppUserId":"\(author.uuidString.lowercased())",
             "authorDisplayName":"A","parentMessageId":null,"body":"b","dateCreated":"2026-08-24T12:00:00Z",
             "replyCount":0,"mentions":[],"hashtags":[],"authorIsFollowedByCurrentUser":false,
             "isOwnPost":false,"reportedByCurrentUser":false,"likeCount":0,"likedByCurrentUser":false,
             "hasMedia":false,"mediaAwaitingReview":false,"mediaKind":0,
             "experienceTypeId":null,"experienceTypeName":null,"categoryMatchDegraded":false,
             "attributedOrgName":null,"attributedOrgUrlName":null,
             "groupVerified":false,"moderatorReviewed":false}
            """
        }
        let page = Data("""
        {"posts":[\(postBy(UUID())),\(postBy(UUID()))],"nextCursor":null,"canPost":true}
        """.utf8)
        let transport = MockTransport { request in
            request.httpMethod == "GET"
                ? (page, MockTransport.response(for: request, status: 200))
                : (Data(), MockTransport.response(for: request, status: 204))
        }
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)
        let store = FeedStore(filter: .latest, api: api)
        await store.load()

        await store.toggleFollow(store.posts[0], actions: FeedActions(api: api))
        let allFollowing = store.posts.allSatisfy(\.authorIsFollowedByCurrentUser)
        #expect(allFollowing)
    }

    @Test func reportFlipsTheControlAndStays() async {
        let (store, actions, post) = await Self.storeWithOnePost(MockTransport(status: 204))
        let reported = await store.report(post, reason: "spam", actions: actions)
        #expect(reported)
        #expect(store.posts[0].reportedByCurrentUser)
    }

    // ── Taxonomy ────────────────────────────────────────────────────────────

    @Test func taxonomyFixtureDecodesAndFiltersToWhatIsChoosable() throws {
        let data = try Fixtures.data("experience-taxonomy", in: Bundle.module)
        let groups = try BenJSON.decoder.decode([ExperienceCategoryWithTypes].self, from: data)

        #expect(groups.count >= 5)
        let audible = try #require(groups.first { $0.category.name == "Audible" })
        #expect(audible.selectableTypes.contains { $0.name == "Voices / Whispering" })
        // Everything offered is choosable — the server would refuse anything else.
        let allChoosable = groups.selectable.allSatisfy { group in
            group.category.isActive && group.selectableTypes.allSatisfy { $0.isActive && $0.isApproved }
        }
        #expect(allChoosable)
    }

    @Test func aFailedTaxonomyFetchLeavesAWorkingComposer() async {
        // No picker, still postable: the category is optional and must never be a gate.
        let taxonomy = await Self.actions(MockTransport(status: 500)).experienceTaxonomy()
        #expect(taxonomy.isEmpty)
    }
}
