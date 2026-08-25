import Foundation

// Investigations a member is on, and the ones they attended — ports of api/my-investigations.

/// Whether somebody has answered an invitation. Append-only server-side.
public enum InvestigationRsvp: Int, Codable, Sendable, Equatable {
    case noAnswer = 0
    case going = 1
    case notGoing = 2
    case maybe = 3
    case unknown = -1

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = InvestigationRsvp(rawValue: raw) ?? .unknown
    }

    public var label: String {
        switch self {
        case .noAnswer: "No answer yet"
        case .going: "Going"
        case .notGoing: "Not going"
        case .maybe: "Maybe"
        case .unknown: "—"
        }
    }
}

/// One investigation this person is on the roster for.
public struct MyInvestigation: Sendable, Codable, Equatable, Identifiable {
    public var attendeeId: UUID
    public var investigationId: UUID
    public var caseId: UUID?
    public var caseReference: String?
    public var caseTitle: String?
    public var orgId: UUID
    public var orgName: String
    public var orgUrlName: String?
    public var title: String
    public var scheduledDateTime: Date?
    public var endDateTime: Date?
    public var location: String?
    public var status: Int
    /// What they are there to do — "EMF Specialist" and the like. Null when unassigned.
    public var assignedRole: String?
    public var rsvp: InvestigationRsvp
    public var didAttend: Bool
    /// When evidence from this investigation is due, for anyone who owes some.
    public var evidenceDueDate: Date?

    public var id: UUID { attendeeId }

    /// Still ahead of us — what a roster screen leads with.
    public func isUpcoming(now: Date = Date()) -> Bool {
        guard let start = scheduledDateTime else { return false }
        return (endDateTime ?? start) >= now
    }
}

/// One investigation this person actually attended — the map's data.
public struct AttendedInvestigation: Sendable, Codable, Equatable, Identifiable {
    public var investigationId: UUID
    public var title: String
    public var scheduledDateTime: Date?
    public var organizationId: UUID
    public var organizationName: String
    public var caseId: UUID?
    public var caseReference: String?
    public var placeId: UUID?
    public var placeName: String?
    public var placeCity: String?
    public var placeState: String?
    /// Null when the place has no coordinates — such a visit simply has no pin, which is
    /// different from a pin at (0,0) in the Gulf of Guinea.
    public var latitude: Double?
    public var longitude: Double?
    public var geocodeNote: String?
    public var wasLead: Bool

    public var id: UUID { investigationId }

    public var placeLabel: String {
        placeName ?? [placeCity, placeState].compactMap { $0 }.joined(separator: ", ")
    }

    public var hasCoordinates: Bool { latitude != nil && longitude != nil }
}
