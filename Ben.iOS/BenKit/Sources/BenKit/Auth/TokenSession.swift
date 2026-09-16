import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Owns the bearer tokens: adoption after login, proactive single-flight
/// refresh, and the session-ended signal. Port of the C# handler's semantics
/// (`WebApiBearerTokenHandler.cs`): refresh when `expiresAt <= now`; N
/// concurrent callers share ONE refresh; a failed refresh signs the user out.
public actor TokenSession {
    public enum Event: Sendable {
        case signedIn
        case sessionEnded
    }

    private let storage: TokenStorage
    private let transport: Transport
    private let environment: @Sendable () -> APIEnvironment
    private let now: @Sendable () -> Date

    private var tokens: StoredTokens?
    private var refreshTask: Task<String?, Never>?
    private var continuations: [UUID: AsyncStream<Event>.Continuation] = [:]

    public init(
        storage: TokenStorage,
        transport: Transport,
        environment: @escaping @Sendable () -> APIEnvironment,
        now: @escaping @Sendable () -> Date = { Date() }
    ) {
        self.storage = storage
        self.transport = transport
        self.environment = environment
        self.now = now
        self.tokens = storage.load()
    }

    public var isSignedIn: Bool { tokens != nil }

    /// Observe sign-in/session-ended transitions (SessionStore subscribes).
    /// Subscribes to session events.
    ///
    /// An event emitted while NOBODY is listening is held and delivered to the next
    /// subscriber, exactly once. Subscription happens on an actor hop — `SessionStore`
    /// creates its listener in `init`, and the `Task` that attaches it runs later — so
    /// without this a session-ended fired in those first instants is dropped and the UI
    /// silently keeps looking signed in. An event that WAS delivered is not replayed: a
    /// subscriber attaching an hour later must not be handed a stale banner.
    public func events() -> AsyncStream<Event> {
        let id = UUID()
        return AsyncStream { continuation in
            continuations[id] = continuation
            if let missed = undeliveredEvent {
                undeliveredEvent = nil
                continuation.yield(missed)
            }
            continuation.onTermination = { [weak self] _ in
                Task { await self?.removeContinuation(id) }
            }
        }
    }

    /// An event that fired before any subscriber existed. Only the most recent is kept —
    /// these are state transitions, and the latest is the one that is still true.
    private var undeliveredEvent: Event?

    private func removeContinuation(_ id: UUID) {
        continuations[id] = nil
    }

    private func emit(_ event: Event) {
        guard !continuations.isEmpty else {
            undeliveredEvent = event
            return
        }
        for continuation in continuations.values { continuation.yield(event) }
    }

    /// Called after a successful `/login` or `/refresh`.
    public func adopt(_ response: AccessTokenResponse) {
        let stored = StoredTokens(
            accessToken: response.accessToken,
            refreshToken: response.refreshToken,
            expiresAt: now().addingTimeInterval(response.expiresIn - 30))
        tokens = stored
        storage.save(stored)
        emit(.signedIn)
    }

    /// The current access token, refreshed first if it has (nearly) expired.
    /// Nil means signed out — the caller sends the request without a header.
    public func validAccessToken() async -> String? {
        guard let current = tokens else { return nil }
        if current.expiresAt > now() { return current.accessToken }

        // Single-flight: the first expired caller starts the refresh; everyone
        // else awaits the same task and gets the same answer.
        if let inFlight = refreshTask { return await inFlight.value }
        let task = Task<String?, Never> { await self.refresh(using: current.refreshToken) }
        refreshTask = task
        let result = await task.value
        refreshTask = nil
        return result
    }

    private func refresh(using refreshToken: String) async -> String? {
        guard let url = environment().url(for: Endpoint(.post, "refresh", requiresAuth: false)) else {
            endSession()
            return nil
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? BenJSON.encoder.encode(["refreshToken": refreshToken])

        guard let (data, response) = try? await transport.send(request) else {
            // Unreachable, not refused: the refresh token may be perfectly good. Keep it for the
            // next attempt rather than signing somebody out for having no signal.
            return nil
        }
        // "Not now" is not "no". A 5xx, a 429 or a 408 says nothing about the refresh token, so it is
        // kept for the next attempt. `/refresh` sits behind the auth rate limiter, and a team sharing
        // one venue Wi-Fi shares its partition — signing them all out for a 429 would end a night's
        // investigation over a header.
        if response.statusCode >= 500 || response.statusCode == 429 || response.statusCode == 408 { return nil }
        guard (200..<300).contains(response.statusCode),
              let refreshed = try? BenJSON.decoder.decode(AccessTokenResponse.self, from: data),
              !refreshed.accessToken.isEmpty
        else {
            // The server refused the refresh token. This is the session ending, exactly once.
            endSession()
            return nil
        }

        let stored = StoredTokens(
            accessToken: refreshed.accessToken,
            refreshToken: refreshed.refreshToken,
            expiresAt: now().addingTimeInterval(refreshed.expiresIn - 30))
        tokens = stored
        storage.save(stored)
        return stored.accessToken
    }

    /// A 401 despite a live-looking token means it was revoked server-side — IF it is the token
    /// still held. A request can outlive a refresh, or a sign-in: it went out with the old token,
    /// the session adopted a new one, and then the old one's 401 arrived. That 401 says nothing
    /// about the token now held, and ending the session for it signed people out in the moment
    /// they had just signed in (seen live on 2026-09-16: a stale keychain token's `api/me` refused
    /// after `-autoSignIn` had already succeeded). `bearer` is the token the refused request
    /// carried; nil keeps the old behaviour for a caller that cannot say.
    public func handleUnauthorized(bearer: String? = nil) {
        guard let current = tokens else { return }
        if let bearer, bearer != current.accessToken { return }
        endSession()
    }

    public func endSession() {
        let wasSignedIn = tokens != nil
        tokens = nil
        storage.clear()
        if wasSignedIn { emit(.sessionEnded) }
    }
}
