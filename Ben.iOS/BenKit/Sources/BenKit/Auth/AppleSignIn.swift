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
    /// Apple vouched for them and the address it gave already belongs to an account here, so a
    /// second one must not be made. Only reachable with an address Apple did NOT verify — a
    /// verified one is linked by the server itself — which is exactly when joining the two
    /// without a password would be wrong.
    case addressTaken(reason: String)
    /// The password was right and the account has a second factor. NOT a failure: the password
    /// was correct, and saying otherwise sends somebody hunting for a mistake they did not make.
    case needsTwoFactor
    case failed(reason: String)
}

/// What the server answers with when an account still needs a name and a handle.
struct AppleNeedsProfile: Sendable, Decodable {
    var needsProfile: Bool
    var suggestedDisplayName: String?
    var email: String?
    var isPrivateEmail: Bool
    var handleProblem: String?
    /// Appended by the server after these fields; older builds ignore them, newer ones route on
    /// them. See `aNeedsProfileBodyWithNewerFieldsStillDecodes`.
    var shouldLinkInstead: Bool?
    var emailProblem: String?
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
            // The address already belongs to somebody here. Creating a second account is not
            // the answer and the server would refuse it - route to the link door instead of
            // showing a handle complaint about a handle that is fine.
            if needs.shouldLinkInstead == true {
                return .addressTaken(
                    reason: needs.emailProblem
                        ?? "That email address already has an account here. Sign in to it once and we'll join the two.")
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

// MARK: - Claiming an account that already exists

extension AppleSignInClient {
    /// Joins this Apple identity to an account somebody already has here, and signs in.
    ///
    /// This is the door for the case sign-in cannot solve on its own. The server only links by
    /// address when Apple's own VERIFIED address matches one, and two entirely ordinary situations
    /// defeat that: Hide My Email, whose relay address will never match anything, and an Apple ID
    /// that is simply at a different address from the one somebody signed up with. Without this
    /// they end at "create an account" and quietly get a SECOND one holding none of their cases,
    /// groups or history.
    ///
    /// - Parameters:
    ///   - identityToken: Apple's token, held from the sheet. Proves the Apple identity.
    ///   - email: The account being claimed. NOT necessarily the address Apple gave — that is the
    ///     entire point of this call.
    ///   - password: Proves the account being claimed belongs to them.
    ///   - twoFactorCode: Sent only after a previous attempt answered ``needsTwoFactor``.
    ///   - recoveryCode: One of the printed codes, instead of an app code. Single use.
    public func link(
        identityToken: String,
        email: String,
        password: String,
        twoFactorCode: String? = nil,
        recoveryCode: String? = nil
    ) async -> AppleSignInOutcome {
        struct Body: Encodable {
            let identityToken: String
            let email: String
            let password: String
            let twoFactorCode: String?
            let twoFactorRecoveryCode: String?

            // Declared, because writing encode(to:) by hand suppresses the synthesised ones.
            enum CodingKeys: String, CodingKey {
                case identityToken, email, password, twoFactorCode, twoFactorRecoveryCode
            }

            // Nil fields are OMITTED, not sent empty. Identity reads an empty string as an attempt
            // with a wrong code, which spends a failure against an account that may have no second
            // factor at all.
            func encode(to encoder: Encoder) throws {
                var container = encoder.container(keyedBy: CodingKeys.self)
                try container.encode(identityToken, forKey: .identityToken)
                try container.encode(email, forKey: .email)
                try container.encode(password, forKey: .password)
                try container.encodeIfPresent(twoFactorCode, forKey: .twoFactorCode)
                try container.encodeIfPresent(twoFactorRecoveryCode, forKey: .twoFactorRecoveryCode)
            }
        }

        guard let endpoint = try? Endpoint.json(
            .post, "api/auth/apple/link",
            payload: Body(
                identityToken: identityToken,
                email: email,
                password: password,
                twoFactorCode: twoFactorCode,
                twoFactorRecoveryCode: recoveryCode),
            requiresAuth: false)
        else { return .failed(reason: "That sign-in couldn't be sent.") }

        guard let (data, status) = await api.loadRaw(endpoint) else {
            return .failed(reason: "The server couldn't be reached.")
        }

        switch status {
        case 200:
            guard let response = try? BenJSON.decoder.decode(AccessTokenResponse.self, from: data) else {
                return .failed(reason: "The server's answer couldn't be read.")
            }
            await tokens.adopt(response)
            return .signedIn

        case 401:
            // One status, four entirely different meanings, exactly as on /login: wait, enter your
            // code, confirm your email, or fix your password. The problem-detail is the only thing
            // that separates them, and three of the four are useless advice if the fourth is guessed.
            let detail = (try? BenJSON.decoder.decode(ProblemDetailsBody.self, from: data))?.detail

            switch detail {
            case "RequiresTwoFactor": return .needsTwoFactor
            case "LockedOut":
                return .failed(reason: "That account is locked after too many attempts. Waiting is the only thing that helps.")
            case "NotAllowed":
                return .failed(reason: "That account's email address hasn't been confirmed yet. Use the link we sent, or ask for another.")
            case "Failed":
                return .failed(reason: "That email address and password don't match an account.")
            default:
                // Unreadable is UNKNOWN, never "your password is wrong" — that sends somebody to
                // reset a password that was always right.
                return .failed(reason: "That sign-in was refused without a reason. Try again — and if it keeps happening, it isn't your password.")
            }

        default:
            let prose = ResponseMapping.prose(fromBody: String(data: data, encoding: .utf8))
            return .failed(reason: prose ?? "That account couldn't be linked.")
        }
    }
}
