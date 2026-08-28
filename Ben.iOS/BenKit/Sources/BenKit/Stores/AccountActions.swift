import Foundation

/// Getting an account, and looking after it (iOS Slice 8).
///
/// Registration and confirmation are ANONYMOUS; password and two-step need the session.
/// Every refusal keeps the server's own sentence — "that name is taken" and "choose a
/// password" send a person to different places, and "something went wrong" sends them nowhere.
public struct AccountActions: Sendable {
    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// Checked as the person types, so they don't fill the whole form to be told the name
    /// they chose was never available. Failure answers "can't tell" rather than a wrong yes
    /// or no: the server re-checks at submit either way.
    public func handleAvailability(_ handle: String) async -> HandleAvailability? {
        let trimmed = handle.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return nil }
        let endpoint = Endpoint(
            .get, "api/account/handle-available",
            query: [URLQueryItem(name: "handle", value: trimmed)],
            requiresAuth: false)
        return await api.load(endpoint, as: HandleAvailability.self).value
    }

    /// Signs up. Note the server answers the SAME way whether or not the address was already
    /// registered — it will not confirm to a stranger which addresses have accounts — so a
    /// success here means "we've sent an email if that address was free", nothing more.
    public func register(_ request: RegisterRequest) async -> Result<RegisterResponse, FeedActionError> {
        guard let endpoint = try? Endpoint.json(.post, "api/account/register",
                                                payload: request, requiresAuth: false)
        else { return .failure(.failed(reason: nil)) }

        // Read the BODY, not the status. This endpoint answers with the same
        // RegisterResponse shape either way — 200 for success, 400 for "that name is taken",
        // "choose a password" and the rest — so the prose mapper is deliberately bypassed:
        // it would replace the one sentence a person can act on with "The server answered
        // 400 (bad request)."
        guard let (data, statusCode) = await api.loadRaw(endpoint) else {
            return .failure(.failed(reason: nil))
        }
        if statusCode == 429 { return .failure(.rateLimited(retryAfter: nil)) }

        if let response = try? BenJSON.decoder.decode(RegisterResponse.self, from: data) {
            return response.succeeded ? .success(response) : .failure(.failed(reason: response.message))
        }
        // Not the documented shape — a proxy error page, a 500. Say so generically rather
        // than showing whatever HTML came back.
        return .failure(.failed(reason: nil))
    }

    /// The emailed link's two halves. Answers 200 with `succeeded: false` for a bad or spent
    /// link rather than an error status — so read the body, not the status.
    public func confirmEmail(userId: UUID, code: String) async -> ConfirmEmailResponse? {
        struct Body: Encodable { let userId: UUID; let code: String }
        guard let endpoint = try? Endpoint.json(
            .post, "api/account/confirm-email",
            payload: Body(userId: userId, code: code), requiresAuth: false)
        else { return nil }
        return await api.load(endpoint, as: ConfirmEmailResponse.self).value
    }

    // ── Looking after it ────────────────────────────────────────────────────

    public func twoFactorStatus() async -> TwoFactorStatus? {
        await api.load(Endpoint(.get, "api/me/2fa"), as: TwoFactorStatus.self).value
    }

    /// Begins setup: the key to type into an authenticator, and the URI to scan.
    public func beginTwoFactorSetup() async -> TwoFactorSetup? {
        await api.load(Endpoint(.post, "api/me/2fa/setup"), as: TwoFactorSetup.self).value
    }

    /// Confirms a code and switches two-step on, returning the recovery codes — which are
    /// shown ONCE and never retrievable again.
    public func enableTwoFactor(code: String) async -> Result<TwoFactorEnabled, FeedActionError> {
        struct Body: Encodable { let code: String }
        guard let endpoint = try? Endpoint.json(
            .post, "api/me/2fa/enable", payload: Body(code: Self.normalizeCode(code)))
        else { return .failure(.failed(reason: nil)) }

        switch await api.load(endpoint, as: TwoFactorEnabled.self) {
        case .ok(let enabled): return .success(enabled)
        case .failed(let reason, _): return .failure(.failed(reason: reason))
        case .sessionEnded: return .failure(.sessionEnded)
        case .rateLimited(let after): return .failure(.rateLimited(retryAfter: after))
        }
    }

    public func disableTwoFactor(code: String) async -> Result<Void, FeedActionError> {
        struct Body: Encodable { let code: String }
        guard let endpoint = try? Endpoint.json(
            .post, "api/me/2fa/disable", payload: Body(code: Self.normalizeCode(code)))
        else { return .failure(.failed(reason: nil)) }

        switch await api.send(endpoint) {
        case .ok: return .success(())
        case .failed(let reason, _): return .failure(.failed(reason: reason))
        case .sessionEnded: return .failure(.sessionEnded)
        case .rateLimited(let after): return .failure(.rateLimited(retryAfter: after))
        }
    }

    public func changePassword(current: String, new: String) async -> Result<Void, FeedActionError> {
        struct Body: Encodable { let currentPassword: String; let newPassword: String }
        guard let endpoint = try? Endpoint.json(
            .post, "api/me/password", payload: Body(currentPassword: current, newPassword: new))
        else { return .failure(.failed(reason: nil)) }

        switch await api.send(endpoint) {
        case .ok: return .success(())
        case .failed(let reason, _): return .failure(.failed(reason: reason))
        case .sessionEnded: return .failure(.sessionEnded)
        case .rateLimited(let after): return .failure(.rateLimited(retryAfter: after))
        }
    }

    // ── Closing it ──────────────────────────────────────────────────────────

    /// What stands in the way of deleting this account, if anything.
    ///
    /// Asked BEFORE the destructive screen is shown, not after the button is pressed. Exactly
    /// one owner exists per organization, and anonymising them would strand the group — so an
    /// owner is refused, and the only useful thing to do with that refusal is name the groups
    /// on the screen where the person is standing. `nil` means the question could not be asked
    /// (offline, a dead session); the screen says so rather than guessing a yes.
    public func accountClosureCheck() async -> AccountClosureCheck? {
        await api.load(Endpoint(.get, "api/me/closure"), as: AccountClosureCheck.self).value
    }

    /// Deletes the account. There is no undo.
    ///
    /// The confirmation word is required by the SERVER, not just the screen — this is the one
    /// call where a stray retry destroys something nobody can restore. Anything the person
    /// authored stays with their group, attributed to a name that is nobody; their identity,
    /// credentials and contact details are gone.
    public func closeAccount() async -> Result<Void, FeedActionError> {
        struct Body: Encodable { let confirmation: String }
        guard let endpoint = try? Endpoint.json(
            .delete, "api/me", payload: Body(confirmation: AccountClosureCheck.confirmationWord))
        else { return .failure(.failed(reason: nil)) }

        switch await api.send(endpoint) {
        case .ok: return .success(())
        // The server's own sentence names the groups to hand over. Losing it here would leave
        // the person with a button that fails and no idea why.
        case .failed(let reason, _): return .failure(.failed(reason: reason))
        case .sessionEnded: return .failure(.sessionEnded)
        case .rateLimited(let after): return .failure(.rateLimited(retryAfter: after))
        }
    }

    /// Spaces and hyphens are how people read a code back off a screen; the server wants
    /// neither. Stripping here means a correct code typed comfortably is not rejected.
    static func normalizeCode(_ code: String) -> String {
        code.replacingOccurrences(of: " ", with: "")
            .replacingOccurrences(of: "-", with: "")
    }
}
