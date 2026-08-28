import Foundation

/// `GET api/me/blocks` — one row of your block list.
///
/// Carries the display name so the Settings list is readable without a second fetch; it is the
/// name as it stands now, so a blocked account that later closes reads "A former member" here
/// like everywhere else.
public struct BlockedUserRecord: Codable, Sendable, Equatable, Identifiable {
    public let appUserId: UUID
    public let displayName: String
    public let dateCreated: Date

    public var id: UUID { appUserId }

    public init(appUserId: UUID, displayName: String, dateCreated: Date) {
        self.appUserId = appUserId
        self.displayName = displayName
        self.dateCreated = dateCreated
    }
}
