import Foundation

/// `GET api/me/closure` — whether this account can be deleted, and what is in the way.
///
/// App Review Guideline 5.1.1(v) requires deletion from inside the app, and it also requires
/// that a blocked path explain itself. Exactly one owner exists per organization; anonymising
/// them would leave a group with nobody able to administer it or reach its billing, so an owner
/// is refused until they have handed the group over. `blockingOrganizations` is what lets the
/// screen say *which* groups rather than just "no".
public struct AccountClosureCheck: Codable, Sendable, Equatable {

    /// The word the server requires in the delete body. Not a UI detail — the API rejects
    /// anything else, so the two must agree.
    public static let confirmationWord = "DELETE"

    public let canClose: Bool
    public let ownedOrganizations: [BlockingOrganization]

    public struct BlockingOrganization: Codable, Sendable, Equatable, Identifiable {
        public let organizationId: UUID
        public let name: String
        public let urlName: String

        public var id: UUID { organizationId }

        public init(organizationId: UUID, name: String, urlName: String) {
            self.organizationId = organizationId
            self.name = name
            self.urlName = urlName
        }
    }

    public init(canClose: Bool, ownedOrganizations: [BlockingOrganization]) {
        self.canClose = canClose
        self.ownedOrganizations = ownedOrganizations
    }
}
