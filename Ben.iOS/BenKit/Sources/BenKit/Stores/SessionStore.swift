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

    public init(auth: IdentityAuthClient, tokens: TokenSession, api: APIClient) {
        self.auth = auth
        self.tokens = tokens
        self.api = api
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
    public func restore() async {
        guard await tokens.isSignedIn, state == .signedOut else { return }
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

    public func signOut() async {
        clearPending()
        // Signing out on purpose is not an interrupt — the event this emits
        // must not raise the banner.
        expectingDeliberateEnd = true
        await tokens.endSession()
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

    private func fetchIdentity(quietOnFailure: Bool) async {
        let result = await api.load(Endpoint(.get, "api/me"), as: MeResponse.self)
        switch result {
        case .ok(let me):
            state = .signedIn(me)
        case .sessionEnded:
            // The token died between adoption and /me (or was stale on restore).
            state = .signedOut
            if !quietOnFailure { errorMessage = "The session ended before it began — try again." }
        case .failed(let reason, _):
            // A sign-in that can't resolve /me is reported on the form, not
            // as the session-ended interrupt.
            expectingDeliberateEnd = true
            await tokens.endSession()
            state = .signedOut
            if !quietOnFailure { errorMessage = reason ?? "The server couldn't be reached." }
        case .rateLimited(let after):
            state = .signedOut
            if !quietOnFailure { retryAfter = after ?? 60 }
        }
    }

    private func handleSessionEnded() {
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
