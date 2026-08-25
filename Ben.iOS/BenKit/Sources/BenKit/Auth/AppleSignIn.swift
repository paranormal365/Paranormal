import Foundation

/// Sign in with Apple, from the app's side of the conversation.
///
/// Apple's own sheet runs in the app target (it needs UIKit and an entitlement); everything
/// after it — what the identity token means, and what the server said back — lives here, where
/// it can be tested without a device or a developer account.
public enum AppleSignInOutcome: Sendable, Equatable {
    /// Signed in. The token session has already adopted the credentials.
    case signedIn
    /// No account here yet, and one cannot be made without a name and a handle. Apple hands the
    /// real name over on the FIRST authorization only, so whatever it gave is passed back for
    /// the form to prefill — after that, nobody can recover it.
    case needsProfile(suggestedName: String?, email: String?, handleProblem: String?)
    case failed(reason: String)
}

/// What the server answers with when an account still needs a name and a handle.
struct AppleNeedsProfile: Sendable, Decodable {
    var needsProfile: Bool
    var suggestedDisplayName: String?
    var email: String?
    var isPrivateEmail: Bool
    var handleProblem: String?
}

public struct AppleSignInClient: Sendable {
    private let api: APIClient
    private let tokens: TokenSession

    public init(api: APIClient, tokens: TokenSession) {
        self.api = api
        self.tokens = tokens
    }

    /// Posts Apple's identity token. `displayName` and `handle` are only read when an account
    /// has to be created, and are ignored entirely for anyone who already has one.
    public func signIn(
        identityToken: String, displayName: String? = nil, handle: String? = nil
    ) async -> AppleSignInOutcome {
        struct Body: Encodable {
            let identityToken: String
            let displayName: String?
            let handle: String?
        }
        guard let endpoint = try? Endpoint.json(
            .post, "api/auth/apple",
            payload: Body(identityToken: identityToken, displayName: displayName, handle: handle),
            requiresAuth: false)
        else { return .failed(reason: "That sign-in couldn't be sent.") }

        guard let (data, status) = await api.loadRaw(endpoint) else {
            return .failed(reason: "The server couldn't be reached.")
        }

        switch status {
        case 200:
            // The body is the same bearer-token response /login returns, deliberately, so the
            // session adopts it with no special case.
            guard let response = try? BenJSON.decoder.decode(AccessTokenResponse.self, from: data) else {
                return .failed(reason: "The server's answer couldn't be read.")
            }
            await tokens.adopt(response)
            return .signedIn

        case 409:
            guard let needs = try? BenJSON.decoder.decode(AppleNeedsProfile.self, from: data) else {
                return .failed(reason: "The server's answer couldn't be read.")
            }
            return .needsProfile(
                suggestedName: needs.suggestedDisplayName,
                // A Hide-My-Email address is real and works, but it is not worth showing to
                // somebody as "your email" — it is a relay they did not choose to read.
                email: needs.isPrivateEmail ? nil : needs.email,
                handleProblem: needs.handleProblem)

        default:
            // The server writes plain sentences here; keep them rather than paraphrasing a code.
            let prose = ResponseMapping.prose(fromBody: String(data: data, encoding: .utf8))
            return .failed(reason: prose ?? "That sign-in couldn't be completed.")
        }
    }
}
