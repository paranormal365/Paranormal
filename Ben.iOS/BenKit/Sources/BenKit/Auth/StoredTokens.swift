import Foundation

/// What the Keychain holds. `expiresAt` is computed at adoption time as
/// `now + expiresIn − 30s`, matching `WebApiBearerTokenHandler.cs` — refresh
/// proactively, half a minute before the server would say no.
public struct StoredTokens: Sendable, Codable, Equatable {
    public var accessToken: String
    public var refreshToken: String
    public var expiresAt: Date

    public init(accessToken: String, refreshToken: String, expiresAt: Date) {
        self.accessToken = accessToken
        self.refreshToken = refreshToken
        self.expiresAt = expiresAt
    }
}

/// Where tokens persist. Keychain in the app; in-memory in tests.
public protocol TokenStorage: Sendable {
    func load() -> StoredTokens?
    func save(_ tokens: StoredTokens)
    func clear()
}

/// The response shape of Identity's `POST /login` and `POST /refresh`.
public struct AccessTokenResponse: Sendable, Codable {
    public var tokenType: String?
    public var accessToken: String
    public var expiresIn: Double
    public var refreshToken: String

    public init(tokenType: String? = "Bearer", accessToken: String, expiresIn: Double, refreshToken: String) {
        self.tokenType = tokenType
        self.accessToken = accessToken
        self.expiresIn = expiresIn
        self.refreshToken = refreshToken
    }
}
