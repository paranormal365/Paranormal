import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// The feed slice (iOS Slice 3): the models match the live API, the For You
/// de-dupe holds, and a switched-off feature renders as exactly that.
@Suite("Feed fixtures — Swift models match the live feed contract")
struct FeedFixtureTests {

    private func fixture(_ name: String) throws -> Data {
        try Fixtures.data(name, in: Bundle.module)
    }

    @Test func feedPageDecodesFromLiveCapture() throws {
        let page = try BenJSON.decoder.decode(FeedPageRecord.self, from: fixture("feed-page"))
        #expect(!page.posts.isEmpty)
        // The capture was made signed-out: reader-relative flags resolve to the visitor's answers.
        #expect(page.canPost == false)
        #expect(page.posts.allSatisfy { !$0.isOwnPost && !$0.likedByCurrentUser })
    }

    @Test func theRichPostCarriesTheArcsWholeSurface() throws {
        // The thread fixture was chosen for the post wearing everything F4–F7 added.
        let thread = try BenJSON.decoder.decode([FeedPostRecord].self, from: fixture("feed-thread"))
        let root = try #require(thread.first)
        #expect(root.hasMedia)
        #expect(root.mediaKind == .image)
        #expect(root.attributedOrgName == "BenCo")
        #expect(root.attributedOrgUrlName == "benco")
        #expect(root.groupVerified)
        // Auto-screened, not human-reviewed — the two marks stay distinct.
        #expect(!root.moderatorReviewed)
    }

    @Test func profileDecodesFromLiveCapture() throws {
        let profile = try BenJSON.decoder.decode(FeedProfileRecord.self, from: fixture("feed-profile"))
        #expect(profile.displayName == "James Thornton")
        #expect(profile.postCount > 0)
    }

    @Test func forYouPageCarriesItsOffsetCursor() throws {
        let page = try BenJSON.decoder.decode(FeedPageRecord.self, from: fixture("feed-page-foryou"))
        #expect(page.nextCursor != nil)
    }

    @Test func unknownMediaKindSurvivesDecoding() throws {
        let raw = Data(#"{"kind": 99}"#.utf8)
        struct Box: Decodable { let kind: FeedMediaKind }
        #expect(try BenJSON.decoder.decode(Box.self, from: raw).kind == .unknown)
    }
}

@Suite("FeedStore — paging, de-dupe, and the honest states")
@MainActor
struct FeedStoreTests {

    private nonisolated static func post(_ id: UUID, date: String = "2026-08-24T12:00:00Z") -> String {
        """
        {"id":"\(id.uuidString.lowercased())","authorAppUserId":"\(UUID().uuidString.lowercased())",
         "authorDisplayName":"A","parentMessageId":null,"body":"post","dateCreated":"\(date)",
         "replyCount":0,"mentions":[],"hashtags":[],"authorIsFollowedByCurrentUser":false,
         "isOwnPost":false,"reportedByCurrentUser":false,"likeCount":0,"likedByCurrentUser":false,
         "hasMedia":false,"mediaAwaitingReview":false,"mediaKind":0,
         "experienceTypeId":null,"experienceTypeName":null,"categoryMatchDegraded":false,
         "attributedOrgName":null,"attributedOrgUrlName":null,
         "groupVerified":false,"moderatorReviewed":false}
        """
    }

    private nonisolated static func page(_ posts: [String], cursor: String?) -> Data {
        Data("""
        {"posts":[\(posts.joined(separator: ","))],
         "nextCursor":\(cursor.map { "\"\($0)\"" } ?? "null"),"canPost":true}
        """.utf8)
    }

    private static func store(_ transport: MockTransport, filter: FeedFilter = .forYou) -> FeedStore {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return FeedStore(filter: filter,
                         api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func forYouPagesDeDupeTheOverlap() async {
        let shared = UUID()   // appears on BOTH pages: its rank moved between requests
        let first = UUID(), second = UUID()

        let transport = MockTransport { request in
            let query = request.url?.query ?? ""
            let body = query.contains("cursor")
                ? Self.page([Self.post(shared), Self.post(second)], cursor: nil)
                : Self.page([Self.post(first), Self.post(shared)], cursor: "ZjoyNQ==")
            return (body, MockTransport.response(for: request, status: 200))
        }

        let store = Self.store(transport)
        await store.load()
        #expect(store.posts.count == 2)
        #expect(store.hasMore)

        await store.loadMore()
        #expect(store.posts.map(\.id) == [first, shared, second]) // no duplicate, order kept
        #expect(!store.hasMore)
    }

    @Test func the404IsAFeatureStateNotAnError() async {
        let store = Self.store(MockTransport(status: 404))
        await store.load()
        #expect(store.state == .featureUnavailable)
    }

    @Test func aRefusalKeepsItsSentence() async {
        let store = Self.store(MockTransport(status: 403, body: Data("Not for you.".utf8)))
        await store.load()
        #expect(store.state == .failed(reason: "Not for you."))
    }

    @Test func aDeadTokenFallsBackToReadingAsAVisitor() async {
        // First request (authed) 401s; the anonymous retry succeeds — the feed reads
        // anonymously by design, and a dead token must not blank the front door.
        let post = UUID()
        let transport = MockTransport { request in
            if request.value(forHTTPHeaderField: "Authorization") != nil {
                return (Data(), MockTransport.response(for: request, status: 401))
            }
            return (Self.page([Self.post(post)], cursor: nil),
                    MockTransport.response(for: request, status: 200))
        }
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(tokens: StoredTokens(
                accessToken: "stale", refreshToken: "r",
                expiresAt: Date(timeIntervalSinceNow: 600))),
            transport: transport, environment: { .dev })
        let store = FeedStore(filter: .latest,
                              api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))

        await store.load()
        #expect(store.state == .loaded)
        #expect(store.posts.map(\.id) == [post])
        #expect(store.canPost == false)
    }

    @Test func filtersCarryTheirQueryShape() {
        #expect(FeedFilter.hashtag("evp").queryItems.contains(URLQueryItem(name: "hashtag", value: "evp")))
        let typeId = UUID()
        #expect(FeedFilter.experienceType(typeId, name: "Apparition").queryItems
            .contains(URLQueryItem(name: "type", value: typeId.uuidString.lowercased())))
        #expect(FeedFilter.forYou.queryItems == [URLQueryItem(name: "mode", value: "foryou")])
    }
}
