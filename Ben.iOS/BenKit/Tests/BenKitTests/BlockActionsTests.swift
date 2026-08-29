import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Blocking an abusive user (App Review 1.2): the routes, the local removal, and the list.
@Suite("Blocking — the reader's own act")
@MainActor
struct BlockActionsTests {

    private static func actions(_ transport: MockTransport) -> FeedActions {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return FeedActions(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func blockAndUnblockHitTheMeBlocksRoutes() async {
        let transport = MockTransport { request in
            (Data(), MockTransport.response(for: request, status: 204))
        }
        let victim = UUID()

        #expect(await Self.actions(transport).block(appUserId: victim))
        #expect(await Self.actions(transport).unblock(appUserId: victim))

        let paths = transport.requests.map { "\($0.httpMethod ?? "") \($0.url?.path ?? "")" }
        // api/me, not api/feed: a block is a fact about two people, kept even while the feed
        // feature is dark, which is why it lives beside the account rather than the feed.
        #expect(paths.contains("POST /api/me/blocks/\(victim.uuidString.lowercased())"))
        #expect(paths.contains("DELETE /api/me/blocks/\(victim.uuidString.lowercased())"))
    }

    @Test func theBlockListDecodesTheServersShape() async {
        // Shape captured from MyBlocksController.BlockedUserRecord via the Web serializer —
        // same discipline as AccountClosureContractTests: never an invented fixture.
        let json = """
        [{"appUserId":"11111111-1111-1111-1111-111111111111","displayName":"A former member",
          "dateCreated":"2026-08-28T12:00:00Z"}]
        """
        let transport = MockTransport { request in
            (Data(json.utf8), MockTransport.response(for: request, status: 200))
        }

        let list = await Self.actions(transport).blockedUsers()
        #expect(list?.count == 1)
        #expect(list?.first?.displayName == "A former member")
    }

    @Test func aFailedListIsNilNotEmpty() async {
        // "Couldn't load your list" and "you block nobody" are different sentences.
        let transport = MockTransport { request in
            (Data(), MockTransport.response(for: request, status: 500))
        }
        #expect(await Self.actions(transport).blockedUsers() == nil)
    }
}
