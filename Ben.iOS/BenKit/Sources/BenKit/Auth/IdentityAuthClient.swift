import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// What one `POST /login` attempt came back as. Identity answers 401 for BOTH
/// a wrong password and a 2FA account that hasn't presented a code yet — only
/// the ProblemDetails `detail` field tells them apart, so this client owns the
/// body parsing rather than going through the generic `LoadResult` mapping
/// (which would collapse every 401 into `.sessionEnded`).
public enum LoginOutcome: Sendable, Equatable {
    case success(AccessTokenResponse)
    /// 401 with `detail == "RequiresTwoFactor"` — retry the SAME call with a code.
    case requiresTwoFactor
    /// 401 without the 2FA marker: wrong email or password.
    case invalidCredentials
    case rateLimited(retryAfter: TimeInterval?)
    case failed(reason: String?)

    public static func == (lhs: LoginOutcome, rhs: LoginOutcome) -> Bool {
        switch (lhs, rhs) {
        case (.success(let a), .success(let b)): a.accessToken == b.accessToken
        case (.requiresTwoFactor, .requiresTwoFactor),
             (.invalidCredentials, .invalidCredentials): true
        case (.rateLimited(let a), .rateLimited(let b)): a == b
        case (.failed(let a), .failed(let b)): a == b
        default: false
        }
    }
}

/// Talks to the Identity endpoints at the API ROOT (`/login`), mirroring
/// `WebApiIdentityClient.TryLoginAsync` on the web side.
public struct IdentityAuthClient: Sendable {
    private let environment: @Sendable () -> APIEnvironment
    private let transport: Transport

    public init(environment: @escaping @Sendable () -> APIEnvironment, transport: Transport) {
        self.environment = environment
        self.transport = transport
    }

    /// One login attempt. Nil 2FA fields are OMITTED from the JSON entirely —
    /// an empty string burns a 2FA attempt (`LoginRequest.encode` guarantees it).
    public func login(_ request: LoginRequest) async -> LoginOutcome {
        guard let url = environment().url(for: Endpoint(.post, "login", requiresAuth: false)),
              let body = try? BenJSON.encoder.encode(request)
        else { return .failed(reason: nil) }

        var urlRequest = URLRequest(url: url)
        urlRequest.httpMethod = "POST"
        urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        urlRequest.httpBody = body

        guard let (data, response) = try? await transport.send(urlRequest) else {
            return .failed(reason: nil)
        }

        switch response.statusCode {
        case 200..<300:
            guard let tokens = try? BenJSON.decoder.decode(AccessTokenResponse.self, from: data),
                  !tokens.accessToken.isEmpty
            else { return .failed(reason: "The server's answer couldn't be read.") }
            return .success(tokens)
        case 401:
            let problem = try? BenJSON.decoder.decode(ProblemDetailsBody.self, from: data)
            return problem?.requiresTwoFactor == true ? .requiresTwoFactor : .invalidCredentials
        case 429:
            let header = (response.allHeaderFields["Retry-After"] as? String)
                ?? (response.allHeaderFields["retry-after"] as? String)
            return .rateLimited(retryAfter: ResponseMapping.retryAfter(header))
        default:
            let prose = ResponseMapping.prose(fromBody: String(data: data, encoding: .utf8))
            return .failed(reason: prose ?? ResponseMapping.statusFallback(response.statusCode))
        }
    }
}
