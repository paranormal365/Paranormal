import Foundation
import Observation

/// The auth state machine the whole app hangs off:
///
///     signedOut → authenticating → (twoFactorChallenge →) fetchingIdentity → signedIn
///
/// Session-ended (a failed refresh, or a 401 on a live token) is an INTERRUPT:
/// it lands back in `signedOut` with a banner, and anonymous surfaces keep
/// working — never a sign-in wall.
@Observable
@MainActor
public final class SessionStore {
    public enum State: Equatable {
        case signedOut
        case authenticating
        /// Password was right; a code is needed. Credentials are held in
        /// MEMORY ONLY for the retry, never persisted.
        case twoFactorChallenge
        case fetchingIdentity
        case signedIn(MeResponse)
    }

    public private(set) var state: State = .signedOut
    /// A human sentence for the sign-in form (wrong password, server prose).
    public private(set) var errorMessage: String?
    /// Non-nil while the auth endpoint has us in a 429 window.
    public private(set) var retryAfter: TimeInterval?
    /// Set when the session ended out from under the user; cleared on sign-in.
    public private(set) var sessionEndedBanner = false

    private let auth: IdentityAuthClient
    private let tokens: TokenSession
    private let api: APIClient
    private let identity: IdentityStorage

    /// Signed in from the identity kept on the phone, because the server couldn't be reached at launch. The next
    /// `restore()` asks the server again.
    public private(set) var identityIsKept = false

    // Held only between the password step and the 2FA retry.
    private var pendingEmail: String?
    private var pendingPassword: String?
    private var eventTask: Task<Void, Never>?
    /// Set around a deliberate sign-out so the session-ended event it emits
    /// (delivered async on the stream) doesn't raise the interrupt banner.
    private var expectingDeliberateEnd = false

    public var me: MeResponse? {
        if case .signedIn(let me) = state { return me }
        return nil
    }

    public init(auth: IdentityAuthClient, tokens: TokenSession, api: APIClient, identity: IdentityStorage = InMemoryIdentityStorage()) {
        self.auth = auth
        self.tokens = tokens
        self.api = api
        self.identity = identity
        // Surface refresh failures from anywhere in the app as the interrupt.
        eventTask = Task { [weak self] in
            let stream = await tokens.events()
            for await event in stream {
                guard let self else { return }
                if case .sessionEnded = event { self.handleSessionEnded() }
            }
        }
    }

    /// Cold start: tokens in the Keychain mean optimistic sign-in pending
    /// `api/me`. A stale token (reinstall, revocation) lands QUIETLY in
    /// signed-out — no error dialog on first launch.
    ///
    /// With no signal, a person whose identity is kept on the phone is signed in as that person, and a later
    /// `restore()` (the app coming back to the foreground) confirms it with the server.
    public func restore() async {
        guard await tokens.isSignedIn else { return }
        if identityIsKept, case .signedIn = state {
            await fetchIdentity(quietOnFailure: true)
            return
        }
        guard state == .signedOut else { return }
        state = .fetchingIdentity
        await fetchIdentity(quietOnFailure: true)
    }

    public func signIn(email: String, password: String) async {
        guard state == .signedOut || state == .twoFactorChallenge else { return }
        errorMessage = nil
        retryAfter = nil
        state = .authenticating
        await attempt(LoginRequest(email: email, password: password),
                      rememberFor2FA: (email, password))
    }

    /// The 2FA retry: same `/login` call, same credentials, plus exactly one
    /// of the two code fields.
    public func submitTwoFactor(code: String, isRecoveryCode: Bool) async {
        guard state == .twoFactorChallenge,
              let email = pendingEmail, let password = pendingPassword else { return }
        errorMessage = nil
        state = .authenticating
        let trimmed = code.replacingOccurrences(of: " ", with: "")
            .replacingOccurrences(of: "-", with: "")
        let request = LoginRequest(
            email: email, password: password,
            twoFactorCode: isRecoveryCode ? nil : trimmed,
            twoFactorRecoveryCode: isRecoveryCode ? trimmed : nil)
        await attempt(request, rememberFor2FA: (email, password))
    }

    public func cancelTwoFactor() {
        guard state == .twoFactorChallenge || state == .authenticating else { return }
        clearPending()
        state = .signedOut
        errorMessage = nil
    }

    /// Called when somebody signs out on purpose — not when a session merely expires — so what the app keeps on the
    /// phone for them (saved event passes) goes before the next person picks the phone up.
    public var onDeliberateSignOut: (@MainActor () -> Void)?

    public func signOut() async {
        onDeliberateSignOut?()
        clearPending()
        // Signing out on purpose is not an interrupt — the event this emits
        // must not raise the banner.
        expectingDeliberateEnd = true
        await tokens.endSession()
        forgetIdentity()
        sessionEndedBanner = false
        state = .signedOut
        errorMessage = nil
    }

    public func dismissSessionEndedBanner() {
        sessionEndedBanner = false
    }

    // MARK: - Internals

    private func attempt(_ request: LoginRequest, rememberFor2FA: (String, String)) async {
        switch await auth.login(request) {
        case .success(let response):
            clearPending()
            await tokens.adopt(response)
            sessionEndedBanner = false
            state = .fetchingIdentity
            await fetchIdentity(quietOnFailure: false)
        case .requiresTwoFactor:
            (pendingEmail, pendingPassword) = rememberFor2FA
            // A wrong 2FA code comes back as requiresTwoFactor again; say so
            // when the user has already been on this screen.
            if request.twoFactorCode != nil || request.twoFactorRecoveryCode != nil {
                errorMessage = "That code didn't work — try the current one."
            }
            state = .twoFactorChallenge
        case .invalidCredentials:
            clearPending()
            errorMessage = "Invalid email or password."
            state = .signedOut
        case .rateLimited(let after):
            clearPending()
            retryAfter = after ?? 60
            errorMessage = nil
            state = .signedOut
        case .failed(let reason):
            clearPending()
            errorMessage = reason ?? "The server couldn't be reached."
            state = .signedOut
        }
    }

    /// Adopts a session that some OTHER door established — today, Sign in with Apple, which
    /// puts the credentials in the token session itself and never sees a password. The state
    /// machine still has to catch up, or the app stays on its sign-in sheet holding a valid token.
    public func adoptExternalSignIn() async {
        clearPending()
        errorMessage = nil
        retryAfter = nil
        sessionEndedBanner = false
        state = .fetchingIdentity
        await fetchIdentity(quietOnFailure: false)
    }

    private func fetchIdentity(quietOnFailure: Bool) async {
        let result = await api.load(Endpoint(.get, "api/me"), as: MeResponse.self)
        switch result {
        case .ok(let me):
            identity.save(me)
            identityIsKept = false
            state = .signedIn(me)
        case .sessionEnded:
            // The token died between adoption and /me (or was stale on restore).
            forgetIdentity()
            state = .signedOut
            if !quietOnFailure { errorMessage = "The session ended before it began — try again." }
        case .failed(_, let status) where quietOnFailure && (status == nil || status! >= 500):
            // A cold start with no signal, or a server having a bad minute, is not a dead session:
            // keep the tokens, so a saved event pass opened at a door with no bars does not sign
            // the person out behind it — and be the person kept on the phone, so tonight's door
            // and the programme are still theirs. The next restore tries the server again.
            keepGoingOffline()
        case .failed(let reason, _):
            // A sign-in that can't resolve /me is reported on the form, not
            // as the session-ended interrupt.
            expectingDeliberateEnd = true
            await tokens.endSession()
            forgetIdentity()
            state = .signedOut
            if !quietOnFailure { errorMessage = reason ?? "The server couldn't be reached." }
        case .rateLimited(let after):
            if quietOnFailure {
                keepGoingOffline()
            } else {
                state = .signedOut
                retryAfter = after ?? 60
            }
        }
    }

    /// The server couldn't be asked: stay the person kept on the phone if there is one, anonymous otherwise.
    private func keepGoingOffline() {
        if let kept = identity.load() {
            identityIsKept = true
            state = .signedIn(kept)
        } else {
            state = .signedOut
        }
    }

    private func forgetIdentity() {
        identity.clear()
        identityIsKept = false
    }

    private func handleSessionEnded() {
        forgetIdentity()
        if expectingDeliberateEnd {
            expectingDeliberateEnd = false
            return
        }
        state = .signedOut
        sessionEndedBanner = true
    }

    private func clearPending() {
        pendingEmail = nil
        pendingPassword = nil
    }
}
