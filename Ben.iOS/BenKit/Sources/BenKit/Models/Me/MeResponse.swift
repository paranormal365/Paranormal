import Foundation

/// `GET api/me` — the canonical post-login call.
public struct MeResponse: Sendable, Codable, Equatable {
    public var userId: UUID
    public var email: String
    public var isSuperAdmin: Bool
    public var isAdmin: Bool

    /// `Guid.Empty` marks an Entra identity with no linked local account —
    /// the client must run account setup (offer "set a password").
    public var isEntraOnly: Bool { userId.isEmptyGuid }

    public init(userId: UUID, email: String, isSuperAdmin: Bool, isAdmin: Bool) {
        self.userId = userId
        self.email = email
        self.isSuperAdmin = isSuperAdmin
        self.isAdmin = isAdmin
    }
}
