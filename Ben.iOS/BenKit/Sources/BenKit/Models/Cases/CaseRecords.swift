import Foundation

// The client's view of their own case — ports of what api/my-cases returns. This is the
// CLIENT side: what the person who asked for help sees, which is deliberately narrower than
// what the investigating group sees of the same case.

/// Where a case is in its life. Append-only on the server; `unknown` absorbs anything added
/// later so a new status never breaks decoding.
public enum CaseStatus: Int, Codable, Sendable, Equatable {
    case pending = 0
    case active = 1
    case closed = 2
    case declined = 3
    case unknown = -1

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = CaseStatus(rawValue: raw) ?? .unknown
    }

    public var label: String {
        switch self {
        case .pending: "Pending"
        case .active: "Active"
        case .closed: "Closed"
        case .declined: "Declined"
        case .unknown: "—"
        }
    }
}

/// What kind of entry an occurrence is. The client logs experiences; investigators add notes.
public enum CaseEntryType: Int, Codable, Sendable, Equatable {
    case occurrence = 0
    case note = 1
    case unknown = -1

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = CaseEntryType(rawValue: raw) ?? .unknown
    }
}

/// One case in the client's list.
public struct MyCaseSummary: Sendable, Codable, Equatable, Identifiable {
    public var caseId: UUID
    public var caseReference: String
    public var title: String
    public var city: String?
    public var state: String?
    public var status: CaseStatus
    public var caseManagerDisplayName: String?
    public var dateCaseOpened: Date
    public var nextInvestigationDate: Date?

    public var id: UUID { caseId }

    public var placeLabel: String? {
        [city, state].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: ", ")
            .isEmpty ? nil : [city, state].compactMap { $0 }.joined(separator: ", ")
    }
}

/// A file attached to an occurrence. The bytes are fetched from an authenticated route —
/// there is no public URL, which is why the app must send its token to show a thumbnail.
public struct MyCaseFile: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var fileName: String?
    public var contentType: String?
    public var fileSize: Int64?

    /// The server calls it `fileId`, not `id` — OccurrenceFileItem and CaseTimelineFileRecord
    /// both do. Without this a case with a single attached photo failed to decode ENTIRELY,
    /// so the whole case read as "the server's answer couldn't be read".
    private enum CodingKeys: String, CodingKey {
        case id = "fileId", fileName, contentType, fileSize
    }

    public var isImage: Bool { contentType?.hasPrefix("image/") ?? false }
}

/// What `POST api/my-cases/{id}/occurrences` answers with: `CaseTimelineEntryRecord`, which is
/// NOT the shape the case detail returns for the same entry. The detail projects a client's view
/// (`ClientCaseOccurrence`, with `fromInvestigators`); the write echoes the raw timeline row.
/// Decoding one as the other is what a mock cannot catch and the live suite did.
public struct CaseTimelineEntryRecord: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var caseId: UUID
    public var authorAppUserId: UUID
    public var authorDisplayName: String?
    public var entryType: CaseEntryType
    public var eventDateTime: Date?
    public var title: String?
    public var body: String?
    public var experienceTypeIds: [UUID]
    public var files: [MyCaseFile]
    public var dateCreated: Date

    /// The same entry as the case timeline shows it. `authorId` is who is reading — the client
    /// who just wrote it — so the side is decided by comparison, never assumed.
    public func asOccurrence(readerId: UUID?) -> MyCaseOccurrence {
        MyCaseOccurrence(
            id: id, entryType: entryType, eventDateTime: eventDateTime,
            title: title, body: body,
            fromInvestigators: readerId.map { $0 != authorAppUserId } ?? false,
            dateCreated: dateCreated, files: files, experienceTypeIds: experienceTypeIds)
    }
}

/// One entry on the case timeline — something that happened, or a note from the group.
public struct MyCaseOccurrence: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var entryType: CaseEntryType
    public var eventDateTime: Date?
    public var title: String?
    public var body: String?
    /// True when the group wrote it, false when the client did. Which side is speaking is
    /// the first thing a reader needs from a timeline.
    public var fromInvestigators: Bool
    public var dateCreated: Date
    public var files: [MyCaseFile]
    public var experienceTypeIds: [UUID]
}

/// An investigation on the client's case, as the client may see it.
public struct MyCaseInvestigation: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID?
    public var investigationId: UUID?
    public var title: String?
    public var scheduledStart: Date?
    public var status: Int?

    public var identity: UUID { investigationId ?? id ?? UUID() }
}

/// Somebody the client can contact about this case.
public struct MyCaseContact: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID?
    public var displayName: String?
    public var roleName: String?
    public var email: String?
    public var phone: String?

    public var identity: UUID { id ?? UUID() }
}

/// The client's full view of one case.
public struct MyCaseDetail: Sendable, Codable, Equatable, Identifiable {
    public var caseId: UUID
    public var caseReference: String
    public var title: String
    public var city: String?
    public var state: String?
    public var status: CaseStatus
    public var description: String?
    public var caseManagerDisplayName: String?
    public var dateCaseOpened: Date
    public var dateCaseClosed: Date?
    public var occurrences: [MyCaseOccurrence]
    public var investigations: [MyCaseInvestigation]
    public var unreadMessageCount: Int
    /// False for a co-client the case was shared with — they may read and log, but the
    /// primary client is the one the group answers to.
    public var isPrimaryClient: Bool
    public var contacts: [MyCaseContact]

    public var id: UUID { caseId }

    /// The timeline, newest first — what happened, not the order it was typed in.
    public var timeline: [MyCaseOccurrence] {
        occurrences.sorted {
            ($0.eventDateTime ?? $0.dateCreated) > ($1.eventDateTime ?? $1.dateCreated)
        }
    }
}

/// A published report on the client's case (`GET api/my-cases/{id}/reports`).
///
/// Only PUBLISHED reports ever reach here — the server filters, so there is no draft to hide
/// client-side and no status to interpret. Shape captured from the dev API.
public struct MyCaseReport: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var caseId: UUID
    public var title: String
    public var expectedDeliveryDate: Date?
    public var publishedAt: Date?
    public var dateCreated: Date

    /// The date to show. `publishedAt` is the one that means something to a reader; the created
    /// date is when the group started writing, which is not their business.
    public var readerDate: Date { publishedAt ?? dateCreated }
}

/// Which side of a case wrote a message. Mirrors `CaseMessageSide`.
public enum CaseMessageSide: Int, Sendable, Codable, Equatable {
    case client = 0
    case organization = 1
    /// A side this build doesn't know about is shown as the group's, never as the reader's own —
    /// mistaking somebody else's words for your own is the worse failure.
    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = CaseMessageSide(rawValue: raw) ?? .organization
    }
}

/// One message on a case (`GET api/my-cases/{id}/messages`). Shape captured from the dev API.
public struct MyCaseMessage: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var caseId: UUID
    public var authorAppUserId: UUID
    public var authorDisplayName: String
    public var body: String
    public var senderSide: CaseMessageSide
    public var isReadByClient: Bool
    public var isReadByOrg: Bool
    public var dateCreated: Date

    /// Written by the client reading it. Drives which side of the screen it sits on.
    public var isMine: Bool { senderSide == .client }
}
