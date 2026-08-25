import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

@Suite("IdentityAuthClient — /login outcome mapping")
struct IdentityAuthClientTests {

    private func client(_ transport: MockTransport) -> IdentityAuthClient {
        IdentityAuthClient(environment: { .dev }, transport: transport)
    }

    private static let tokenBody = Data(
        #"{"tokenType":"Bearer","accessToken":"AT","expiresIn":3600,"refreshToken":"RT"}"#.utf8)

    @Test func successReturnsTokens() async {
        let transport = MockTransport(status: 200, body: authTokenBody)
        let outcome = await client(transport).login(LoginRequest(email: "a@b.c", password: "p"))
        guard case .success(let tokens) = outcome else {
            Issue.record("expected success, got \(outcome)"); return
        }
        #expect(tokens.accessToken == "AT")
        #expect(tokens.refreshToken == "RT")
        // The call went to the API ROOT, not under api/.
        #expect(transport.requests.first?.url?.path == "/login")
    }

    @Test func requiresTwoFactorIsDistinguishedFromBadPassword() async {
        let twoFactor = MockTransport(status: 401, body: Data(
            #"{"title":"Unauthorized","status":401,"detail":"RequiresTwoFactor"}"#.utf8))
        #expect(await client(twoFactor).login(LoginRequest(email: "a@b.c", password: "p"))
                == .requiresTwoFactor)

        let badPassword = MockTransport(status: 401, body: Data(
            #"{"title":"Unauthorized","status":401,"detail":"Failed"}"#.utf8))
        #expect(await client(badPassword).login(LoginRequest(email: "a@b.c", password: "p"))
                == .invalidCredentials)
    }

    @Test func rateLimitedCarriesRetryAfter() async {
        let transport = MockTransport(status: 429, headers: ["Retry-After": "37"])
        #expect(await client(transport).login(LoginRequest(email: "a@b.c", password: "p"))
                == .rateLimited(retryAfter: 37))
    }

    @Test func loginBodyOmitsNilTwoFactorFields() async {
        let transport = MockTransport(status: 200, body: authTokenBody)
        _ = await client(transport).login(LoginRequest(email: "a@b.c", password: "p"))
        let sent = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(!sent.contains("twoFactor"))
    }
}

// File-scope (nonisolated) so the Sendable MockTransport closures can read them
// from the @MainActor test suite below.
private let authTokenBody = Data(
    #"{"tokenType":"Bearer","accessToken":"AT","expiresIn":3600,"refreshToken":"RT"}"#.utf8)
private let authMeBody = Data(
    #"{"userId":"11111111-2222-3333-4444-555555555555","email":"james.thornton@benco.dev","isSuperAdmin":false,"isAdmin":false}"#.utf8)

/// A transport that answers /login and /api/me like the real API.
private func happyAuthTransport() -> MockTransport {
    MockTransport { request in
        let path = request.url?.path ?? ""
        let body: Data = path.hasSuffix("/login") ? authTokenBody
            : path.hasSuffix("/api/me") ? authMeBody : Data()
        return (body, MockTransport.response(for: request, status: 200))
    }
}

@Suite("SessionStore — the auth state machine")
@MainActor
struct SessionStoreTests {

    private static func makeStore(
        transport: MockTransport, storage: TokenStorage = InMemoryTokenStorage()
    ) -> (SessionStore, TokenSession) {
        let tokens = TokenSession(storage: storage, transport: transport, environment: { .dev })
        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)
        let auth = IdentityAuthClient(environment: { .dev }, transport: transport)
        return (SessionStore(auth: auth, tokens: tokens, api: api), tokens)
    }

    @Test func happyPathReachesSignedInWithIdentity() async {
        let (store, _) = Self.makeStore(transport: happyAuthTransport())
        await store.signIn(email: "james.thornton@benco.dev", password: "pw")
        #expect(store.me?.email == "james.thornton@benco.dev")
        #expect(store.me?.isSuperAdmin == false)
        #expect(store.errorMessage == nil)
    }

    @Test func badPasswordLandsSignedOutWithMessage() async {
        let transport = MockTransport(status: 401, body: Data(#"{"detail":"Failed"}"#.utf8))
        let (store, _) = Self.makeStore(transport: transport)
        await store.signIn(email: "a@b.c", password: "wrong")
        #expect(store.state == .signedOut)
        #expect(store.errorMessage == "Invalid email or password.")
        #expect(store.sessionEndedBanner == false)
    }

    @Test func twoFactorChallengeThenSuccess() async {
        let transport = MockTransport { request in
            let path = request.url?.path ?? ""
            if path.hasSuffix("/login") {
                let sent = String(decoding: request.httpBody ?? Data(), as: UTF8.self)
                if sent.contains("twoFactorCode") {
                    return (authTokenBody, MockTransport.response(for: request, status: 200))
                }
                return (Data(#"{"detail":"RequiresTwoFactor"}"#.utf8),
                        MockTransport.response(for: request, status: 401))
            }
            return (authMeBody, MockTransport.response(for: request, status: 200))
        }
        let (store, _) = Self.makeStore(transport: transport)

        await store.signIn(email: "a@b.c", password: "pw")
        #expect(store.state == .twoFactorChallenge)
        #expect(store.errorMessage == nil)

        // Spaces/hyphens are stripped before sending, like the server does.
        await store.submitTwoFactor(code: "123 456", isRecoveryCode: false)
        #expect(store.me != nil)

        let retry = transport.requests.filter { $0.url?.path.hasSuffix("/login") == true }.last
        let sent = String(decoding: retry?.httpBody ?? Data(), as: UTF8.self)
        #expect(sent.contains(#""twoFactorCode":"123456""#))
        #expect(!sent.contains("twoFactorRecoveryCode"))
    }

    @Test func rateLimitLandsSignedOutWithCountdownNotError() async {
        let transport = MockTransport(status: 429, headers: ["Retry-After": "42"])
        let (store, _) = Self.makeStore(transport: transport)
        await store.signIn(email: "a@b.c", password: "pw")
        #expect(store.state == .signedOut)
        #expect(store.retryAfter == 42)
        #expect(store.errorMessage == nil)
    }

    @Test func restoreWithLiveTokensSignsInQuietly() async {
        let storage = InMemoryTokenStorage(tokens: StoredTokens(
            accessToken: "AT", refreshToken: "RT",
            expiresAt: Date(timeIntervalSinceNow: 600)))
        let (store, _) = Self.makeStore(transport: happyAuthTransport(), storage: storage)
        await store.restore()
        #expect(store.me?.email == "james.thornton@benco.dev")
    }

    @Test func restoreWithDeadTokensLandsQuietlyInSignedOut() async {
        // Reinstall case: Keychain has tokens, server says no. No error dialog.
        let storage = InMemoryTokenStorage(tokens: StoredTokens(
            accessToken: "stale", refreshToken: "stale",
            expiresAt: Date(timeIntervalSinceNow: 600)))
        let transport = MockTransport(status: 401)
        let (store, _) = Self.makeStore(transport: transport, storage: storage)
        await store.restore()
        #expect(store.state == .signedOut)
        #expect(store.errorMessage == nil)
    }

    @Test func deliberateSignOutRaisesNoBanner() async {
        let (store, _) = Self.makeStore(transport: happyAuthTransport())
        await store.signIn(email: "a@b.c", password: "pw")
        #expect(store.me != nil)
        await store.signOut()
        // Give the async event stream a beat to deliver.
        try? await Task.sleep(for: .milliseconds(50))
        #expect(store.state == .signedOut)
        #expect(store.sessionEndedBanner == false)
    }

    @Test func anEventFiredBeforeAnyoneIsListeningIsNotLost() async {
        // SessionStore attaches its listener on an actor hop, so a session ending in the
        // first instants after launch would otherwise vanish and leave a signed-in-looking
        // UI. The event is held for the first subscriber and delivered exactly once.
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(tokens: StoredTokens(
                accessToken: "AT", refreshToken: "RT",
                expiresAt: Date(timeIntervalSinceNow: 600))),
            transport: MockTransport(status: 200), environment: { .dev })

        await tokens.endSession()          // nobody is listening yet

        // Bounded: without the fix this stream never yields, and a test that HANGS reports a
        // regression as a timeout instead of a failure. Race the wait against a deadline so
        // the answer is "no event arrived", which is the actual finding.
        let stream = await tokens.events()  // subscribes AFTER the fact
        let received = await withTaskGroup(of: TokenSession.Event?.self) { group -> TokenSession.Event? in
            group.addTask {
                for await event in stream { return event }
                return nil
            }
            group.addTask {
                try? await Task.sleep(for: .milliseconds(500))
                return nil
            }
            let first = await group.next() ?? nil
            group.cancelAll()
            return first
        }
        #expect(received == .sessionEnded)

        // And it is not replayed to the next subscriber — a stale banner an hour later
        // would be worse than the miss it fixes.
        let second = await tokens.events()
        var late: [TokenSession.Event] = []
        let waiter = Task {
            for await event in second { late.append(event); break }
        }
        try? await Task.sleep(for: .milliseconds(60))
        waiter.cancel()
        #expect(late.isEmpty)
    }

    @Test func refreshFailureRaisesTheInterruptBanner() async {
        // Signed in, then every request 401s: the next refresh kills the session.
        let storage = InMemoryTokenStorage(tokens: StoredTokens(
            accessToken: "AT", refreshToken: "RT",
            expiresAt: Date(timeIntervalSinceNow: -60))) // already expired
        let transport = MockTransport(status: 401)
        let (store, tokens) = Self.makeStore(transport: transport, storage: storage)
        _ = await tokens.validAccessToken() // triggers the failing refresh
        try? await Task.sleep(for: .milliseconds(50))
        #expect(store.sessionEndedBanner == true)
        #expect(store.state == .signedOut)
    }
}
