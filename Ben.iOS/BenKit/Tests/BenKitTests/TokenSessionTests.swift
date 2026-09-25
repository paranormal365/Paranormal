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

    /// `/refresh` is rate limited on the server. A 429 is "not now", not "you are refused" —
    /// the token stays, the request goes out without a header, and the next call tries again.
    @Test(arguments: [429, 408, 503])
    func aBusyServerDoesNotEndTheSession(status: Int) async {
        let transport = MockTransport(status: status)
        let storage = InMemoryTokenStorage(tokens: Self.expiredTokens())
        let session = TokenSession(storage: storage, transport: transport, environment: { .dev })

        #expect(await session.validAccessToken() == nil)
        #expect(await session.isSignedIn == true)
        #expect(storage.load()?.refreshToken == "r1")
        #expect(transport.requestCount(pathSuffix: "/refresh") == 1)

        // And the next caller tries again rather than reusing a dead answer.
        _ = await session.validAccessToken()
        #expect(transport.requestCount(pathSuffix: "/refresh") == 2)
    }

    /// A request that went out with the old token and came back 401 after a new one was adopted —
    /// a refresh, or a sign-in on top of a stale keychain — must not end the session it knows
    /// nothing about.
    @Test func aRefusalOfAReplacedTokenDoesNotEndTheSession() async {
        let storage = InMemoryTokenStorage(tokens: StoredTokens(
            accessToken: "old", refreshToken: "r1", expiresAt: Date(timeIntervalSinceNow: 600)))
        let session = TokenSession(storage: storage, transport: MockTransport(status: 500), environment: { .dev })
        await session.adopt(AccessTokenResponse(accessToken: "new", expiresIn: 3600, refreshToken: "r2"))

        await session.handleUnauthorized(bearer: "old")
        #expect(await session.isSignedIn == true)
        #expect(storage.load()?.accessToken == "new")

        // The token actually held being refused is still the session ending.
        await session.handleUnauthorized(bearer: "new")
        #expect(await session.isSignedIn == false)
    }

    /// And the client passes the token the request carried, not just the fact that it carried one.
    @Test func theClientReportsWhichTokenWasRefused() async {
        let storage = InMemoryTokenStorage(tokens: StoredTokens(
            accessToken: "old", refreshToken: "r1", expiresAt: Date(timeIntervalSinceNow: 600)))
        let tokens = TokenSession(storage: storage, transport: MockTransport(status: 500), environment: { .dev })
        // The request is built with "old", then a sign-in lands before the 401 comes back.
        let transport = MockTransport { request in
            await tokens.adopt(AccessTokenResponse(accessToken: "new", expiresIn: 3600, refreshToken: "r2"))
            return (Data(), MockTransport.response(for: request, status: 401))
        }
        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)

        _ = await api.send(Endpoint(.get, "api/me"))
        #expect(await tokens.isSignedIn == true)
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
