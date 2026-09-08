import Foundation

// The GROUP side of a case, as a member on the roster sees it — ports of the org endpoints the
// website's case page already uses. Distinct from `MyCaseSummary` and the rest of
// CaseRecords.swift, which are the CLIENT's view of their own case and deliberately narrower in
// a different direction.
//
// iOS-8 of the 2026-09-06 evaluation: the app had no group-side case view at all, so a member
// rostered on a visit could not open the case from the phone. My Cases is the client's list and
// shows them nothing.
//
// **Narrow on purpose.** These decode a handful of fields out of records that carry dozens. Swift
// ignores keys it was not asked about, so the app takes what it shows and nothing else — no
// second contract to keep in step, and a field added on the server cannot break the phone.

/// Where a case is in its life, as the GROUP counts it.
///
/// Not `CaseStatus` — that one is the client's four-value view of their own case, and the numbers
/// mean different things. The group's 2 is Active; the client's 2 is Closed. Reusing the type
/// would have shown a member "Closed" over a case being worked on.
public enum GroupCaseStatus: Int, Codable, Sendable, Equatable {
    case proposed = 0
    case accepted = 1
    case active = 2
    case summarized = 3
    case closed = 4
    case publicCase = 5
    case haunted = 6
    case transferred = 7
    case paused = 8
    case unknown = -1

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = GroupCaseStatus(rawValue: raw) ?? .unknown
    }

    public var label: String {
        switch self {
        case .proposed: "Proposed"
        case .accepted: "Accepted"
        case .active: "Active"
        case .summarized: "Summarised"
        case .closed: "Closed"
        case .publicCase: "Public"
        case .haunted: "Haunted"
        case .transferred: "Transferred"
        case .paused: "Paused"
        case .unknown: "—"
        }
    }
}

/// One case, as its investigating group sees it.
public struct GroupCase: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var organizationId: UUID
    public var title: String
    public var caseYear: Int
    public var orgCaseNumber: Int
    public var city: String?
    public var state: String?
    public var status: GroupCaseStatus
    public var dateCaseOpened: Date?
    public var caseManagerDisplayName: String?
    public var isPublic: Bool?

    /// `#2026-003`, the way every screen on the site writes it.
    public var reference: String { String(format: "#%d-%03d", caseYear, orgCaseNumber) }

    public var placeLabel: String? {
        let parts = [city, state].compactMap { ($0?.isEmpty == false) ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }
}

/// What kind of thing a timeline entry is. Append-only server-side; `unknown` absorbs the rest.
public enum GroupTimelineEntryType: Int, Codable, Sendable, Equatable {
    case clientReport = 0
    case investigatorNote = 1
    case evidence = 2
    case instrumentReading = 3
    case interview = 4
    case researchNote = 5
    case unknown = -1

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = GroupTimelineEntryType(rawValue: raw) ?? .unknown
    }

    /// The words a person reads. The website learned the same lesson (W-A11): the raw enum name
    /// leaked onto the screen as "InstrumentReading".
    public var label: String {
        switch self {
        case .clientReport: "Client report"
        case .investigatorNote: "Note"
        case .evidence: "Evidence"
        case .instrumentReading: "Instrument reading"
        case .interview: "Interview"
        case .researchNote: "Research"
        case .unknown: "Entry"
        }
    }
}

/// A file hanging off a timeline entry.
public struct GroupTimelineFile: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var fileName: String?
    public var contentType: String?

    /// The server names it `fileId`, exactly as it does for the client's own case files — and
    /// getting that wrong once made a whole case fail to decode over one photo.
    private enum CodingKeys: String, CodingKey {
        case id = "fileId", fileName, contentType
    }

    public var isImage: Bool { contentType?.hasPrefix("image/") ?? false }
}

/// One entry on the case's timeline.
public struct GroupTimelineEntry: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var entryType: GroupTimelineEntryType
    public var eventDateTime: Date?
    public var title: String?
    public var body: String?
    public var authorDisplayName: String?
    public var dateCreated: Date
    public var files: [GroupTimelineFile]?

    /// When it happened, falling back to when it was written down.
    public var occurredAt: Date { eventDateTime ?? dateCreated }
}

/// A file on the case's Files tab.
public struct GroupCaseFile: Sendable, Codable, Equatable, Identifiable {
    /// The link row's own id — which is NOT the file to download.
    public var id: UUID
    /// The upload to fetch bytes for. A case file is a link, and the two ids are different.
    public var uploadFileId: UUID
    public var fileName: String?
    public var contentType: String?
    public var fileSize: Int64?
    public var dateCreated: Date?

    public var isImage: Bool { contentType?.hasPrefix("image/") ?? false }
}

/// Which side of the conversation a message came from.
public enum GroupMessageSide: Int, Codable, Sendable, Equatable {
    case client = 0
    case organization = 1
    case unknown = -1

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = GroupMessageSide(rawValue: raw) ?? .unknown
    }
}

/// One message between the group and its client.
public struct GroupCaseMessage: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var body: String?
    public var senderSide: GroupMessageSide
    public var authorDisplayName: String?
    public var dateCreated: Date

    public var isFromTheGroup: Bool { senderSide == .organization }
}
