import Foundation

/// A lead's launch (item 252): the group's session somebody can join from a feed card, a push or
/// Field Kit's "Happening now". Ben, 2026-09-28: "instead of forcing them to join" — nothing opens
/// or records until the person taps Join and then Start.
public struct FieldLaunchRecord: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    /// `investigation`, `event` (a tour date or calendar event) or `hosted-event`.
    public var target: String
    public var investigationId: UUID?
    public var orgCalendarEventId: UUID?
    public var hostedEventId: UUID?
    public var title: String
    /// Where, for the session's label; nil when the exact place is withheld.
    public var locationLabel: String?
    public var organizationName: String
    public var launchedByName: String
    public var launchedUtc: Date
    public var endsUtc: Date
    public var expiresUtc: Date
    public var isPublic: Bool
    public var appLink: String
    /// The secret the lead's QR code carries, for somebody not registered to ask to join. Sent only
    /// to whoever may manage the launch; nil for everybody else, and for a home or a private case.
    public var joinToken: String?

    public init(id: UUID, target: String, investigationId: UUID? = nil, orgCalendarEventId: UUID? = nil,
                hostedEventId: UUID? = nil, title: String, locationLabel: String? = nil,
                organizationName: String, launchedByName: String, launchedUtc: Date, endsUtc: Date,
                expiresUtc: Date, isPublic: Bool, appLink: String, joinToken: String? = nil) {
        self.id = id; self.target = target; self.investigationId = investigationId
        self.orgCalendarEventId = orgCalendarEventId; self.hostedEventId = hostedEventId
        self.title = title; self.locationLabel = locationLabel; self.organizationName = organizationName
        self.launchedByName = launchedByName; self.launchedUtc = launchedUtc; self.endsUtc = endsUtc
        self.expiresUtc = expiresUtc; self.isPublic = isPublic; self.appLink = appLink
        self.joinToken = joinToken
    }
}

/// Something the signed-in lead may launch now.
public struct LaunchableRecord: Sendable, Codable, Equatable, Identifiable {
    public var target: String
    /// The investigation, calendar event or hosted event.
    public var id: UUID
    public var title: String
    public var startsUtc: Date
    public var endsUtc: Date
    public var timeZoneId: String?
    /// How many people are registered — who a launch reaches.
    public var people: Int
    public var lastLaunchedUtc: Date?
    public var isPublic: Bool
}

/// What pressing Launch did.
public struct LaunchOutcomeRecord: Sendable, Codable, Equatable {
    public var launch: FieldLaunchRecord
    public var people: Int
    public var peopleWithTheApp: Int
    public var phonesReached: Int
    /// False on a server with no push key: the card went up, nobody was pushed.
    public var pushConfigured: Bool
}

/// A feed card's launch: its title, the link that joins it, and when it goes.
public struct FeedLaunchCard: Sendable, Codable, Equatable {
    public var launchId: UUID
    public var title: String
    public var appLink: String
    public var expiresUtc: Date
}

/// Where somebody who scanned the lead's code stands (item 252).
public struct JoinStandingRecord: Sendable, Codable, Equatable {
    public var launchId: UUID
    public var title: String
    public var organizationName: String
    public var launchedByName: String
    /// `in` (with `launch`), `ask`, `pending`, `declined` or `sign-in`.
    public var standing: String
    public var launch: FieldLaunchRecord?

    public var isIn: Bool { standing == "in" }
}

/// Somebody asking to join, as the lead sees them.
public struct JoinRequestRecord: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var appUserId: UUID
    public var displayName: String
    /// `pending`, `approved` or `declined`.
    public var status: String
    public var requestedUtc: Date
    public var decidedUtc: Date?
}
