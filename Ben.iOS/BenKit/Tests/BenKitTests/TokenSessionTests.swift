import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

@Suite("TokenSession — single-flight refresh (WebApiBearerTokenHandler.cs parity)")
struct TokenSessionTests {

    private static func expiredTokens() -> StoredTokens {
        StoredTokens(accessToken: "old", refreshToken: "r1",
                     expiresAt: Date(timeIntervalSinceNow: -60))
    }

    private static func tokenResponseData(access: String) -> Data {
        Data("""
        {"tokenType":"Bearer","accessToken":"\(access)","expiresIn":3600,"refreshToken":"r2"}
        """.utf8)
    }

    @Test func tenConcurrentCallersShareOneRefresh() async {
        let transport = MockTransport { request in
            // Slow refresh so all ten callers pile up behind it.
            try await Task.sleep(for: .milliseconds(50))
            return (Self.tokenResponseData(access: "new"),
                    MockTransport.response(for: request, status: 200))
        }
        let session = TokenSession(
            storage: InMemoryTokenStorage(tokens: Self.expiredTokens()),
            transport: transport,
            environment: { .dev })

        let results = await withTaskGroup(of: String?.self) { group in
            for _ in 0..<10 {
                group.addTask { await session.validAccessToken() }
            }
            var collected: [String?] = []
            for await value in group { collected.append(value) }
            return collected
        }

        #expect(results.allSatisfy { $0 == "new" })
        #expect(transport.requestCount(pathSuffix: "/refresh") == 1)
    }

    @Test func unexpiredTokenSkipsTheNetworkEntirely() async {
        let transport = MockTransport(status: 500)
        let session = TokenSession(
            storage: InMemoryTokenStorage(tokens: StoredTokens(
                accessToken: "live", refreshToken: "r1",
                expiresAt: Date(timeIntervalSinceNow: 600))),
            transport: transport,
            environment: { .dev })

        #expect(await session.validAccessToken() == "live")
        #expect(transport.requests.isEmpty)
    }

    @Test func failedRefreshEndsTheSessionExactlyOnce() async {
        let transport = MockTransport(status: 401)
        let storage = InMemoryTokenStorage(tokens: Self.expiredTokens())
        let session = TokenSession(storage: storage, transport: transport, environment: { .dev })

        let events = await session.events()
        #expect(await session.validAccessToken() == nil)
        #expect(await session.isSignedIn == false)
        #expect(storage.load() == nil)

        var iterator = events.makeAsyncIterator()
        let first = await iterator.next()
        #expect(first == .sessionEnded)
    }

    @Test func adoptStoresWithThirtySecondSafetyMargin() async {
        let fixedNow = Date(timeIntervalSince1970: 1_000_000)
        let storage = InMemoryTokenStorage()
        let session = TokenSession(
            storage: storage, transport: MockTransport(status: 500),
            environment: { .dev }, now: { fixedNow })

        await session.adopt(AccessTokenResponse(
            accessToken: "a", expiresIn: 3600, refreshToken: "r"))

        #expect(storage.load()?.expiresAt == fixedNow.addingTimeInterval(3570))
    }

    @Test func refreshRequestCarriesTheRefreshToken() async {
        let transport = MockTransport { request in
            (Self.tokenResponseData(access: "new"),
             MockTransport.response(for: request, status: 200))
        }
        let session = TokenSession(
            storage: InMemoryTokenStorage(tokens: Self.expiredTokens()),
            transport: transport, environment: { .dev })

        _ = await session.validAccessToken()
        let body = transport.requests.first?.httpBody.flatMap {
            try? JSONDecoder().decode([String: String].self, from: $0)
        }
        #expect(body?["refreshToken"] == "r1")
    }
}

extension TokenSession.Event: Equatable {
    public static func == (lhs: TokenSession.Event, rhs: TokenSession.Event) -> Bool {
        switch (lhs, rhs) {
        case (.signedIn, .signedIn), (.sessionEnded, .sessionEnded): true
        default: false
        }
    }
}
