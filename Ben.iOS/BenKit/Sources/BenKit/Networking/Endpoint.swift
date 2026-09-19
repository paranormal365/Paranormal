import Foundation

public enum HTTPMethod: String, Sendable {
    case get = "GET", post = "POST", put = "PUT", delete = "DELETE"
}

/// One API call, described declaratively. Paths are RELATIVE with no leading
/// slash: the Identity endpoints (`login`, `refresh`, `forgotPassword`,
/// `resetPassword`) live at the API root, everything else under `api/`.
public struct Endpoint: Sendable {
    public enum RequestBody: Sendable {
        case none
        /// Encoded with `BenJSON.encoder` at send time.
        case json(Data)
        case multipart(MultipartBody)
    }

    public var method: HTTPMethod
    public var path: String
    public var query: [URLQueryItem]
    public var body: RequestBody
    /// When true the bearer token is attached (and its absence is fine — some
    /// routes like `api/upload-files/{id}/download` are [AllowAnonymous] but
    /// still audience-checked, answering 401/403 by entitlement).
    public var requiresAuth: Bool

    public init(
        _ method: HTTPMethod,
        _ path: String,
        query: [URLQueryItem] = [],
        body: RequestBody = .none,
        requiresAuth: Bool = true
    ) {
        precondition(!path.hasPrefix("/"), "Endpoint paths are relative — a leading slash would drop the base path")
        self.method = method
        self.path = path
        self.query = query
        self.body = body
        self.requiresAuth = requiresAuth
    }

    /// Convenience for a JSON-bodied endpoint.
    public static func json<T: Encodable>(
        _ method: HTTPMethod,
        _ path: String,
        payload: T,
        query: [URLQueryItem] = [],
        requiresAuth: Bool = true
    ) throws -> Endpoint {
        Endpoint(method, path, query: query,
                 body: .json(try BenJSON.encoder.encode(payload)),
                 requiresAuth: requiresAuth)
    }
}
